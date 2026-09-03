# 三层插件宿主：AOT 常驻宿主 + JIT 扩展主机 + 独立进程插件

> **一句话定调：宿主只做“不崩、秒开、管全局”的事；五区本身降级为第一个内部插件；独立插件按统一规则与宿主对话。**

v0.2.0 的 `Mew.Host(AOT) + IMewToolModule(编译期)` 解决了主框架分叉，但把“秒开(AOT)”与“可扩展(DLL 运行时加载)”锁死在同一进程：AOT 不支持 `Assembly.Load`，五区又与 Overlay/托盘/热键耦合在同一 `MewHost` 进程，任一富插件崩溃即拖垮常驻能力。v0.2.1 起拆为三层，以进程边界换隔离与发布自由度。

## Considered Options

- **单进程三合一（T1+T2+T3 同进程）**：一个 `Mew.Host.exe` 同时支持编译期、DLL 运行时、IPC exe。物理上不可行——AOT 进程无法加载含托管代码的新程序集（见 ADR-000101-04），要支持 T2 只能全切 JIT，牺牲浮层秒开。
- **单进程双产物（AOT 版 T1+T3 / JIT 版 T1+T2+T3，同一源码两发布）**：能保 AOT 启动速度，但五区仍在宿主进程内，DLL 插件崩溃仍拖垮托盘/热键/浮层；且宿主要同时承载“系统入口”与“Workbench 重 UI”，职责不清。→ 作为 v0.2.0 的过渡评估，未选为终态。
- **uTools 式 Electron 隔离渲染进程**：每个插件独立 Renderer，通过 `plugin.json` 声明 `features/cmds`，只能调 `utools.*` 受限 API。隔离与声明式做得彻底，但强绑定 Electron/前端技术栈，与 MewUI/.NET/AOT 路线不匹配。→ 仅作参照。
- **FluentLauncher 式 JIT + Assembly.LoadFrom**：`LocalFolder/Extensions/*.dll` 递归扫描，`Assembly.LoadFrom` + 反射找 `IExtension`，`ENABLE_LOAD_EXTENSIONS` 编译开关控制是否打包扩展页。验证了 .NET 桌面用 DLL 插件的可行性，但 WinUI 对 `AssemblyLoadContext` 不友好、退化为 `LoadFrom` 即无隔离，且单进程仍无故障隔离。→ 经验复用，不照搬。
- **三层拆分（选定）**：见下。

## 决策

拆为三个进程/产物层级，同一仓库同一源码，按发布配置产出不同组合：

```
┌─ Layer 1  宿主 Mew.Host (AOT) ──────────────────────┐  常驻、轻、AOT
│  Mew.Host.exe  (NativeAOT, win-x64)                  │
│  职责 = 系统级 + 跨插件，不含五区重 UI：              │
│  • Overlay 浮层框架（UI 空壳 + 结果混排 + 行渲染）    │
│  • 全局热键中央注册表 IHotkeyService（冲突检测）      │
│  • 托盘、单实例锁、开机自启、关于/菜单/标题栏外壳      │
│  • 设置文档根节（themeMode / overlayHotkey）与持久化  │
│  • 插件发现与生命周期（扫目录/读清单/拉起/杀掉/重启）  │
│  • IPC 路由（NamedPipe，宿主为 server）               │
└──────────────┬──────────────────────────────────────┘
               │ NamedPipe / JSON-RPC (宿主 ↔ 扩展主机/独立插件)
┌─ Layer 2  扩展主机 Mew.PluginHost (JIT) ────────────┐  重、可扩展、JIT
│  Mew.PluginHost.exe（或 Mew.Host.JIT.exe）            │
│  职责 = Workbench 五区容器：                          │
│  • 承载 Workbench 五区（ActivityBar/SideBar/         │
│    EditorArea/Panel/StatusBar）与主题/布局            │
│  • 作为“内部插件”：五区本身是第一个内部插件            │
│  • 承载 T1 编译期工具模块 + T2 DLL 运行时插件          │
│    （AssemblyLoadContext / LoadFrom，隔离按目录）      │
│  • 向 Layer 1 注册搜索源/设置节/热键（代理转发）       │
│  • 崩溃不影响 Layer 1 的 Overlay/热键/托盘             │
└──────────────┬──────────────────────────────────────┘
               │ 同一 IPC 协议
┌─ Layer 3  独立插件 (各 exe, 任意语言) ───────────────┐  完全独立
│  my-tool.exe  独立可双击运行；被宿主拉起时以            │
│  --mew-plugin 模式常驻后台，不弹主窗口                   │
│  • 满足同一插件清单 + IPC 规则即可被发现                │
│  • 能力声明式，宿主聚合其搜索结果到浮层                  │
└───────────────────────────────────────────────────────┘
```

**发布形态：**

- 默认分发：`Mew.Host(AOT).exe + Mew.PluginHost(JIT).exe` 双 exe，同目录发布。AOT 宿主负责秒开常驻，JIT 扩展主机按需拉起（首次打开五区或加载 DLL 插件时）。
- 单文件回退：仅 `Mew.Host(AOT).exe` 时，T2 DLL 插件在“设置→插件”中置灰并提示“需 JIT 扩展主机”；T3 独立 exe 仍可用。

## 插件规则（统一清单 + IPC）

三层共用一套 `plugin.json` 清单与 IPC 契约，T2/T3 差异仅在“发现方式”：

**1. 清单 `plugin.json`（声明式，类 uTools `features`）：**

```json
{
  "id": "clipboard-history",
  "displayName": "剪贴板历史",
  "version": "0.1.0",
  "entry": { "type": "dll", "path": "Clipboard.dll" }, // T2
  // 或 { "type": "exe", "path": "Clipboard.exe", "args": "--mew-plugin" } // T3
  "capabilities": {
    "search": { "providerId": "clipboard", "displayName": "剪贴板" },
    "settingsSection": { "id": "clipboard", "title": "剪贴板" },
    "hotkeys": [{ "id": "quick-paste", "default": "Ctrl+Shift+V", "label": "快速粘贴" }]
  },
  "permissions": ["search", "settings", "hotkeys"]
}
```

`id` 全局唯一（用于设置分节、热键冲突检测、搜索来源标记）；`version` 遵循 semver；`capabilities` 未声明即无该能力，宿主拒绝越权调用。

**2. 发现与生命周期：**

- 扫描目录：`%APPDATA%/Mew/Plugins/<id>/plugin.json` + 宿主安装目录 `Plugins/`（T2 DLL 与 T3 exe 同目录结构，FluentLauncher 式 `Extensions/` 递归扫描为先例）。
- 校验：清单 schema 校验 + `id` 唯一性校验，失败项在“设置→插件”标红，不加载。
- 拉起：T2 由 Layer 2 `AssemblyLoadContext` 加载；T3 由 Layer 1 `Process.Start` 拉起并建立 NamedPipe 会话。
- 启用/禁用：宿主持久化 `plugins.json`（`{id: enabled}`），禁用即不拉起，已拉起的发 `shutdown` 后杀进程/卸载 ALC。
- 热更新：T3 无需重启宿主，重启插件进程即可；T2 需重启 Layer 2（ALC 卸载限制）。

**3. IPC 协议（宿主为 server，JSON-RPC over NamedPipe）：**

```
插件 → 宿主: register { id, capabilities }
宿主 → 插件: searchRequest { query, maxResults, requestId }
插件 → 宿主: searchResponse { requestId, results: SearchResult[] }
宿主 → 插件: activate { resultId }
宿主 → 插件: settingsChanged { section, json }
宿主 → 插件: hotkeyTriggered { hotkeyId }
双向:   ping/pong, shutdown, log
```

`SearchResult = { id, title, subtitle, icon?, payload }`，行渲染仍由宿主 Overlay 统一（图标+主行+副行+来源标记），与 `ISearchSource` 语义一致，T2 可直接复用 `ISearchSource` 接口，T3 经 IPC 序列化。

**4. 能力分级（与 IMewToolModule 对齐）：**

| 能力 | Layer 2 (T1/T2 富插件) | Layer 3 (T3 瘦插件) | 说明 |
|---|---|---|---|
| 贡献五区 UI（直接 MewUI 拼） | ✅ 完全（`ToolModuleContext.Workbench`） | ❌ 禁止，仅可提供声明式设置节/搜索 | 进程外无法直接操作 Workbench 视觉树 |
| 注册搜索源 | ✅ `IOverlayService.AddSearchSource` | ✅ IPC `searchResponse` | 唯一对三层都一致的能力 |
| 注册设置节 | ✅ `SettingsSectionRegistry` | ✅ IPC `settingsChanged` + 宿主代渲染 | 宿主统一呈现设置侧边栏 |
| 注册全局热键 | ✅ `IHotkeyService` | ✅ 宿主代注册，触发后 IPC 通知 | 冲突检测仍在 Layer 1 集中 |
| 托盘/标题栏/主题 | 宿主独占 | 宿主独占 | 按 v0.2.0 已收归宿主 |

**5. 故障与版本：**

- 隔离：Layer 2 崩溃 → Layer 1 捕获进程退出，Overlay/热键/托盘保持可用，弹 Toast “扩展主机已重启”并自动拉起；Layer 3 崩溃 → 仅该插件标为“已崩溃”，不影响其他。
- 契约版本：IPC 引入 `protocolVersion`（当前 1），宿主与插件握手时校验，不匹配则拒绝并提示更新。
- 语言无关：T3 可为 Python/Go/Rust exe，只要实现 NamedPipe JSON-RPC 即可。

## Consequences

- **ADR-000101-04 保持有效但限缩范围**：AOT 仅约束 Layer 1（秒开路径），Layer 2 明确为 JIT（支持 `Assembly.Load`），双产物发布替代“全 AOT 或全 JIT”二选一。
- **ADR-000101-03 演进**：编译期组合仍是 T1 的方式，T2/T3 以清单 + IPC 扩展为运行时组合；`IMewToolModule` 保留，新增 `plugin.json` 清单为 T2/T3 的声明式等价物。
- **ADR-000200 机制后置解除**：v0.2.0 的“发现器可替换”在 v0.2.1 落地为 Layer 1 的插件发现器 + Layer 2 的 ALC 加载器。
- `Mew.Workbench` 职责收缩：五区与主题/布局保留在 Workbench，但宿主不再直接 `new Workbench().Build()` 承载五区，改为由 Layer 2 扩展主机承载；Layer 1 仅依赖 `OverlayWindow` 空壳 + 服务。
- 设置文档保持“根节+模块节”结构（ADR-000200 已迁移），新增 `plugins.json` 记录启用态；`plugin.json` 为只读清单，不与用户设置混淆。
- 发布复杂度上升：需维护 `Mew.Host(AOT)` + `Mew.PluginHost(JIT)` 双 exe，CI 需两条 `dotnet publish` 流水线（AOT/Trim 与 JIT ReadyToRun）；单 AOT 回退需测试覆盖。
- 术语：`CONTEXT.md` 中 `工具模块` 保留指代 T1/T2 富插件，`独立插件` 新增指代 T3 进程外插件，`扩展主机` 指代 Layer 2；`插件` 一词不再留空，统一指代 T2+T3 运行时插件，与 `工具模块` 并列。

## Alternatives Rejected

- 见 Considered Options；uTools/FluentLauncher 经验已吸收为清单声明式与目录扫描细节，不作为整体方案照搬。
