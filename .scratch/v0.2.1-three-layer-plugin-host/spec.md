# v0.2.1 三层插件宿主规格

Status: ready-for-agent

> 版本: v0.2.1
> 对应 ADR: `docs/adr/000201-01-three-layer-plugin-host.md`

## Problem Statement

`v0.2.0` 以 `Mew.Host(AOT) + IMewToolModule(编译期)` 终结了主框架分叉，所有工具模块编译期并入同一进程、共享同一份 `Mew.Workbench`。但该结构把两对矛盾锁死在同一进程内：其一，**秒开要求 AOT**，而 `Assembly.Load / AssemblyLoadContext` 运行时加载要求 JIT，二者在单进程内互斥，导致 `T2 运行期 DLL 插件` 无法落地；其二，**常驻能力（呼出浮层、全局热键、托盘）要求不崩**，而 `Workbench` 五区与富插件重 UI 运行在同一进程，任一插件崩溃或布局异常即拖垮托盘与浮层。即便以“同一源码双产物（AOT 版 T1+T3 / JIT 版 T1+T2+T3）”过渡，五区仍与宿主同进程，故障隔离仍为零。对个人自用的长期演进而言，需要在不牺牲浮层秒开的前提下同时支持编译期、运行期 DLL、运行期独立进程三种插件，并以进程边界换隔离。

## Solution

`v0.2.1` 起拆为**三层插件宿主**（洋葱结构）：

- **Layer 1 宿主 `Mew.Host`(AOT，常驻)**：只做“不崩、秒开、管全局”的事——呼出浮层框架（UI 空壳 + 结果混排 + 行渲染）、全局热键中央注册表、托盘/单实例/开机自启、设置文档根节、插件发现与生命周期、IPC 路由（NamedPipe server）。不承载 `Workbench` 五区重 UI。
- **Layer 2 扩展主机 `Mew.PluginHost`(JIT，可重启)**：承载 `Mew.Workbench` 五区（活动栏、侧边栏、编辑器区、底部面板、状态栏）与主题/布局；五区本身降级为第一个内部插件；承载 `T1 编译期工具模块` 与 `T2 运行期 DLL 插件`（`AssemblyLoadContext` 按目录隔离）；通过 IPC 向 Layer 1 代理注册搜索源/设置节/热键；崩溃不影响 Layer 1。
- **Layer 3 独立插件（各 exe，任意语言）**：本身是完整应用，可双击独立运行；被宿主以 `--mew-plugin` 常驻拉起时按统一 `plugin.json` 清单 + IPC 规则与宿主对话，仅提供声明式能力（搜索、设置节、热键），不直接操作五区视觉树。

三层共用一套 `plugin.json` 声明式清单与 `JSON-RPC over NamedPipe` IPC 契约，`T2` 与 `T3` 差异仅在发现方式（ALC 加载 vs `Process.Start`）。宿主聚合所有来源的搜索结果到同一呼出浮层，统一做来源标记与热键冲突检测。

## User Stories

1. As 用户, I want 呼出浮层一按即开（AOT 秒开不受插件数量影响）, so that 高频搜索体感不回归。
2. As 用户, I want 托盘/全局热键/浮层在扩展主机或任一插件崩溃后仍可用, so that 常驻能力不被重 UI 拖垮。
3. As 用户, I want 扩展主机崩溃后自动重启并恢复五区布局, so that 我无需手动拉起。
4. As 用户, I want 在“设置→插件”中看到所有已发现插件的启用态与健康态（已加载/已禁用/清单错误/已崩溃）, so that 插件状态一目了然。
5. As 用户, I want 禁用某插件后其搜索源、设置节、热键立即从宿主侧消失且进程被回收, so that 禁用即生效。
6. As 用户, I want 启用插件后无需重启宿主即可生效（T3 立即拉起，T2 提示需重启扩展主机时一键重启）, so that 试用成本低。
7. As 框架主人, I want 宿主不承载 Workbench 五区重 UI, so that 常驻进程保持轻量与稳定。
8. As 框架主人, I want 五区本身是第一个内部插件（与外部插件同等注册路径）, so that 宿主与扩展主机职责可被同一清单机制描述。
9. As 工具作者, I want 已有的 `IMewToolModule` 编译期模块零改动继续在扩展主机中运行, so that `v0.2.0` 资产不作废。
10. As 工具作者, I want 通过 `plugin.json` 声明 `id/displayName/version/entry/capabilities/permissions` 即可被发现, so that 接入只需一份清单。
11. As 工具作者, I want `capabilities` 未声明的能力被宿主拒绝调用, so that 越权可被拦截。
12. As 工具作者, I want `T2 DLL` 插件复用 `ISearchSource / ISettingsService / IHotkeyService` 既有契约（由扩展主机代理转发）, so that 富插件写法与编译期一致。
13. As 工具作者, I want `T3 exe` 插件以任意语言实现 `JSON-RPC over NamedPipe` 即可提供搜索, so that 不绑定 .NET。
14. As 用户, I want 全局浮层 `Ctrl+Alt+Space` 聚合所有已启用插件的搜索结果（扁平混排 + 行尾来源标记）, so that 一次输入跨工具命中。
15. As 用户, I want 唯一搜索源时浮层行为与 `v0.2.0` 单源时代一致（行数上限、样式不变）, so that 零 UX 回归。
16. As 用户, I want 多源结果按 `每源上限 = 全局上限 / 源数` 限流后再全局截断, so that 单一插件不霸屏。
17. As 工具作者, I want 通过清单声明 `search.providerId/displayName` 即注册搜索源, so that 搜索接入声明式。
18. As 用户, I want 在设置文档中按插件 `id` 分节隔离（根节归宿主，模块节归各插件）, so that 多插件设置互不踩键。
19. As 用户, I want 插件的设置节出现在宿主“设置”侧边栏（与外观/热键/Launcher 数据节同列）, so that 设置入口统一。
20. As 工具作者, I want 插件收到 `settingsChanged` 通知后可刷新自身行为, so that 设置变更可联动。
21. As 用户, I want 插件声明的全局热键由宿主 `IHotkeyService` 统一注册并做跨插件冲突检测, so that 热键不互踩。
22. As 用户, I want 热键冲突时提示占用方 `label`（如“浮层呼出键”/“启动项『X』”/“插件『剪贴板』快速粘贴”）, so that 冲突可定位。
23. As 用户, I want 插件清单校验失败（schema 错误 / `id` 重复 / 版本非法）在设置页标红且不加载, so that 错误清单不拖垮宿主。
24. As 用户, I want 启用态持久化于独立 `plugins.json`（与 `settings.json` 分离）, so that 清单只读、开关可写。
25. As 开发者, I want IPC 引入 `protocolVersion` 握手校验，不匹配时拒绝并提示更新, so that 契约演进可控。
26. As 开发者, I want 宿主与扩展主机/独立插件间 `ping/pong/shutdown/log` 可观测, so that 崩溃与卡死可诊断。
27. As 开发者, I want 默认分发为 `Mew.Host(AOT).exe + Mew.PluginHost(JIT).exe` 双 exe，同目录发布即完整能力, so that 分发清晰。
28. As 用户, I want 仅有 `Mew.Host(AOT).exe` 时 `T2 DLL` 插件置灰并提示“需 JIT 扩展主机”, so that 单文件回退可用。
29. As 开发者, I want 扩展主机按需拉起（首次打开五区或加载首个 `T2` 时）, so that 无重 UI 场景下不额外占资源。
30. As 用户, I want `T3` 独立 exe 双击可独立运行（有自有窗口），被宿主拉起时以无窗口常驻态提供搜索, so that 插件兼具独立工具价值。

## Implementation Decisions

- **三层进程模型**：`Mew.Host(AOT)` 为常驻 server，`Mew.PluginHost(JIT)` 为扩展主机子进程，`T3 exe` 为独立插件子进程；宿主负责 IPC 路由与生命周期，扩展主机仅为 Workbench 容器，不再由宿主直接 `new Workbench().Build()` 承载五区。
- **职责切分**：Layer 1 保留呼出浮层框架（UI 空壳+`OverlaySearchAggregator` 混排+行渲染）、`HotkeyService` 中央注册表、`SettingsService` 根节、`TrayIcon`/`NativeChromeWindow` 外壳、插件发现器、IPC server；Layer 2 保留 `Workbench` 五区、`WorkbenchTheme`、`WorkbenchView`、布局持久化，新增 `AssemblyLoadContext` 加载器与 IPC client；Layer 3 不依赖 `Mew.Workbench`。
- **统一清单 `plugin.json`**：字段 `id（全局唯一，kebab-case）/displayName/version(semver)/entry{type:dll|exe, path, args}/capabilities{search, settingsSection, hotkeys}/permissions`；`capabilities` 未声明即无该能力，宿主拒绝越权 `register`。
- **发现与校验**：扫描 `%APPDATA%/Mew/Plugins/<id>/plugin.json` 与安装目录 `Plugins/`（递归一层，类 FluentLauncher `Extensions/`）；清单经 JSON Schema 校验 + `id` 唯一性校验，失败项仅标红不加载；`id` 冲突时后发现者拒绝。
- **生命周期**：`plugins.json`（`{ [id]: enabled }`）持久化启用态；启用→拉起（T2 经 ALC 加载、T3 经 `Process.Start` + NamedPipe 握手），禁用→发 `shutdown` 后回收（T2 卸载 ALC、T3 杀进程）；设置页提供启用/禁用开关与“重启扩展主机”按钮（T2 热更新需重启 Layer 2）。
- **IPC 协议**：`JSON-RPC over NamedPipe`，宿主为 server，管道名 `mew-host-<user-sid>`；消息 `register {id, capabilities, protocolVersion}` / `searchRequest {query, maxResults, requestId}` / `searchResponse {requestId, results: SearchResult[]}` / `activate {resultId}` / `settingsChanged {section, json}` / `hotkeyTriggered {hotkeyId}` / 双向 `ping/pong/shutdown/log`；`SearchResult {id, title, subtitle, icon?, payload}` 行渲染仍由宿主统一，`T2` 复用 `ISearchSource` 接口经扩展主机序列化转发。
- **搜索聚合**：沿用 `OverlaySearchAggregator` 策略（每源上限 = `DefaultGlobalCap / 源数`，全局再截断，扁平混排+来源标记）；Layer 1 为唯一聚合点，Layer 2/T3 仅为搜索源，不自做聚合。
- **设置**：`settings.json` 保持“根节+模块节”结构（`themeMode/overlayHotkey` 归根节，各插件按 `id` 分节），新增 `plugins.json` 记录启用态；插件设置节经 `SettingsSectionRegistry` 统一列入设置侧边栏，T3 设置变更经 `settingsChanged` 推送。
- **热键**：`IHotkeyService` 保留在 Layer 1，全局唯一；Layer 2/T3 声明的热键由宿主代注册，触发后经 `hotkeyTriggered` 通知归属插件；冲突检测集中，`FindOwner` 返回占用方 `label` 供 UI 点名。
- **故障隔离**：Layer 2 进程退出由 Layer 1 捕获，Overlay/热键/托盘保持可用，弹 Toast 并自动拉起；Layer 3 崩溃仅标该插件为“已崩溃”，不影响其他源；所有进程退出均记 `log`。
- **契约版本**：IPC 首包握手校验 `protocolVersion`（当前 1），不匹配拒绝并在设置页提示“协议版本不匹配，请更新插件/宿主”。
- **发布**：默认双 exe 发布（`Mew.Host.exe` AOT + `Mew.PluginHost.exe` JIT），CI 两条 `dotnet publish` 流水线；单 AOT 回退为受支持降级路径，T2 置灰逻辑需覆盖。
- **术语与文档**：`CONTEXT.md` 中 `工具模块` 保留指代 `T1/T2` 富插件（可操作五区），`独立插件` 新增指代 `T3` 进程外插件，`扩展主机` 指代 Layer 2，`插件` 统一指代 `T2+T3` 运行时插件；`呼出浮层` 仍为框架级机制（多搜索源聚合）。
- **兼容性**：`v0.2.0` 的 `IMewToolModule`、`ISearchSource`、`ISettingsService` 契约保持有效，`LauncherModule` 作为首个 `T1` 编译期内置于扩展主机；旧 `settings.json` 迁移逻辑（根节+模块节）保持。

## Testing Decisions

- **好测试的标准**：只测外部行为——发现结果（清单校验成败、`id` 冲突拒绝）、启用态持久化、IPC 搜索聚合的输出（顺序/每源上限/全局上限/来源标记）、热键冲突判定、崩溃隔离后宿主仍可用——不测实现细节（ALC 内部、NamedPipe 帧格式）；纯逻辑优先，可无窗口复现。
- **单一高位缝**：沿用 `tests/Mew.Host.Tests` 为主缝并扩展为“宿主+扩展主机+IPC”黑盒缝（新增 `PluginDiscoveryTests` / `IpcSearchAggregationTests` / `PluginLifecycleTests`），以 `RecordingOverlay` / `FakeModule` / 环回 NamedPipe fake 插件驱动，无需真实窗口或子进程；`Mew.Workbench` 的 `OverlaySearchAggregator` 单元测试保留为纯逻辑补充。新增缝数量控制为一，`Mew.Launcher.Tests` 领域测试保持原位。
  - 发现与校验：合法清单被发现、可加载；`id` 重复/ schema 非法/版本非法的清单被拒绝且在设置模型中标红。
  - 启用态：`plugins.json` 读写往返；禁用后搜索源与设置节从宿主侧消失。
  - 搜索聚合（跨 IPC）：多源（T1+T2 fake + T3 fake）→ 扁平混排、每源上限、全局上限、来源标记；唯一源时行数与 `v0.2.0` 一致。
  - 热键：跨插件热键注册冲突被拒绝，`FindOwner` 返回占用方 `label`。
  - 隔离：模拟扩展主机进程退出，断言宿主 Overlay/热键仍可触发且自动拉起标记被置位；模拟独立插件崩溃仅该源消失。
- **既有缝不变**：`tests/Mew.Launcher.Tests` 继续测 Launcher 领域（引用对象不变）；`Workbench` 配置校验与 `HotkeyService` / `SettingsService` 单测随职责迁移调整归属但用例语义不变。
- **先例**：`Mew.Host.Tests.HostCompositionTests` 的“真实 Launcher + 假模块 Build 校验”风格与 `OverlaySearchAggregatorTests` 的纯逻辑聚合校验风格；`FluentLauncher` 的 `Extensions` 递归扫描与 `ENABLE_LOAD_EXTENSIONS` 开关测试为参考。

## Out of Scope

- **插件市场/自动更新/签名校验**：发现仅做本地目录扫描与清单校验，不做下载、市场、签名链验证。
- **托盘菜单贡献**：契约暂不含托盘菜单扩展，待有真实工具需要时再扩。
- **自定义浮层行渲染**：行渲染仍由宿主统一（图标+主行+副行+来源标记），不开放插件自定义行组件。
- **权限沙箱**：`permissions` 仅做能力声明与越权拒绝，不做 OS 级沙箱或文件/网络隔离。
- **浮层分组呈现**：保持扁平混排+来源标记，不引入按源分组或排序定制。
- **插件间直接通信**：插件仅与宿主 IPC，不支持插件互调。
- **WebView/声明式 UI**：扩展主机外的插件不提供五区 UI 能力，不引入 JSON UI schema 或 WebView 容器。
- **单进程三合一**：不评估让单 AOT 进程同时支持 `Assembly.Load` 的方案（与 ADR-000101-04 冲突）。

## Further Notes

- 版本号常量 `AppVersion` 在实现开始时提升为 `v0.2.1`（宿主内 `Mew.Host/MewHost.cs` 单行改动，先例 `ba2885e`/`6e02e1a`）。
- 实现前按 `issue-tracker` 约定将本规格拆为 `.scratch/v0.2.1-three-layer-plugin-host/issues/NN-*.md`，`Status: ready-for-agent`。
- `CONTEXT.md` 与 `docs/adr/000201-01-three-layer-plugin-host.md` 已同步三层术语；`ADR-000101-03/04` 保持有效但限缩范围（AOT 仅约束 Layer 1，编译期组合为 T1 方式）。
- 兼容性：`v0.2.0` 的 `settings.json` 根节+模块节结构与 `LauncherModule` 零改动复用；新增 `plugins.json` 不影响旧设置。
- 发布验证需覆盖双 exe 完整能力与单 AOT 回退（T2 置灰）两条路径。
