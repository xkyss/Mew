# 运行与发布

> 对应版本：`v0.2.2` 宿主独立启动（`docs/adr/000202-01-host-independent-startup.md`）

## 先决条件

- Windows 10/11（`Direct2D` + `Win32` 热键/托盘/浮层）
- .NET SDK `10.0.400+`（`dotnet --version`）
- WSL/Linux 仅可跑 `tests`，不可跑 UI；Linux 产物进 `.build-linux/`，与 Windows 的 `.build/` 隔离，互不干扰

## 项目结构

```
src/Mew.Host/        — 宿主常驻（AOT，托盘/热键/浮层框架/插件发现/IPC 路由）
src/Mew.PluginHost/  — 主界面（JIT，Workbench 五区 + T1/T2 工具模块）
src/Mew.Workbench/   — 主框架（五区、主题、布局、Overlay 聚合、IPC 契约）
src/Mew.Launcher/    — 示例工具模块（T1 编译期）
Mew.slnx             — 4 工程（Host/PluginHost/Workbench/Launcher）+ 2 测试
```

## 开发期运行（JIT，无需 AOT）

```bash
# 仓库根 mew/
dotnet build Mew.slnx
dotnet run --project src/Mew.Host/Mew.Host.csproj
```

`Mew.Host` 启动后直接隐藏到托盘（仅托盘与呼出浮层常驻），并自动拉起主界面：

- 宿主主窗口首启不展示；主界面随宿主启动（崩溃仍不自愈，需手动重启）
- 托盘左键呼出搜索浮层，右键菜单可打开/重启主界面
- 开发期 fallback 路径：`Host` 会到 `.build/Mew.PluginHost/bin/Debug/net10.0-windows/Mew.PluginHost.exe`
- 托盘常驻：首启即隐藏，左键呼出/隐藏浮层，右键 `打开主界面` / `重启主界面` / `退出`，浮层 `Alt+Space` 按一次呼出、再按隐藏；失败告警（热键被占、主界面缺失）会亮出主窗口
- 退出宿主不再终止主界面进程；运行中主界面退出后仅标记，需手动重启（自动拉起只发生在宿主启动时）

单独调试主界面：

```bash
dotnet run --project src/Mew.PluginHost/Mew.PluginHost.csproj
```

## 发布（双 exe）

默认分发为双 exe 同目录：

```bash
dotnet publish src/Mew.Host -c Release -r win-x64 /p:PublishAot=true -o publish
dotnet publish src/Mew.PluginHost -c Release -r win-x64 -o publish
# 产物：
# publish/Mew.Host.exe       — AOT，秒开常驻
# publish/Mew.PluginHost.exe — JIT，可加载 DLL 插件
```

单 AOT 回退：仅分发 `Mew.Host.exe` 时，`T2`（`type=dll`）插件在 `设置 → 插件` 置灰并提示“需 JIT 主界面”，`T3`（`type=exe`）仍可用。

## 插件放置

扫描目录（递归一层）：

```
%APPDATA%\Mew\Plugins\<id>\plugin.json   # 用户目录
<exe-dir>\Plugins\<id>\plugin.json       # 安装目录
```

额外插件目录（开发期免复制联调）：两种方式，合并生效。最终扫描顺序 = 环境变量 → 配置列表 → 安装目录
（重复 `id` 以靠前的目录为准）：

1. **设置页**：`设置 → 插件 → 插件目录`，完整可配置的目录列表，第一项为默认目录：每行可 `设为默认`/
   `删除`，底部输入框可添加（单插件目录或 `<id>/` 根目录均可）。落盘于 `settings.json` 根节
   `pluginDirs`；列表为空/缺省时回退到用户目录（`%APPDATA%\Mew\Plugins`）；`安装目录`
   （`<exe-dir>\Plugins`）随包内置、恒为末尾。目录增减需重启宿主（刷新快照）与主界面（加载 DLL）
   生效，不存在的目录会被忽略并标出。
2. **环境变量** `MEW_PLUGINS_EXTRA`（多目录用 `;` Windows / `:` Linux 分隔，`Path.PathSeparator`），
   宿主启动时并入扫描并记入 `host.log`。额外目录支持两种形态：

```powershell
# 直接指向单个插件目录（本身含 plugin.json，如 Mxd 构建输出）
$env:MEW_PLUGINS_EXTRA="D:\code\Mxd\.build\Mxd.UI\bin\Debug\net10.0-windows"
# 或指向含多个 <id>/ 子目录的根目录
$env:MEW_PLUGINS_EXTRA="D:\dev-plugins"
```

指向构建输出时：改代码后只需 `dotnet build` + 重启主界面（T2 的 ALC 限制），无需复制、无需重启宿主
（首次指向新目录需重启宿主以刷新快照）。

`plugin.json` 最小示例（`T3` 独立进程）：

```json
{
  "id": "clipboard-history",
  "displayName": "剪贴板历史",
  "version": "0.1.0",
  "entry": { "type": "exe", "path": "Clipboard.exe", "args": "--mew-plugin" },
  "capabilities": {
    "search": { "providerId": "clipboard", "displayName": "剪贴板" }
  },
  "permissions": ["search"],
  "protocolVersion": 1
}
```

`T2` DLL 示例：

```json
{
  "id": "todo-dll",
  "displayName": "待办",
  "version": "0.1.0",
  "entry": { "type": "dll", "path": "Todo.dll" },
  "capabilities": {
    "search": { "providerId": "todo", "displayName": "待办" },
    "settingsSection": { "id": "todo-options", "title": "待办选项" },
    "hotkeys": [{ "id": "quick-add", "default": "Ctrl+Shift+T", "label": "快速新建" }]
  },
  "permissions": ["search", "settings", "hotkeys"],
  "protocolVersion": 1
}
```

字段约束：`id` 为 `kebab-case` 且全局唯一，`version` 为 `semver`，`protocolVersion` 须为 `1`（不匹配时校验失败并提示“请更新插件/宿主”），`capabilities` 未声明的能力越权注册会被拒绝，`id` 重复时后发现者拒绝。

启用态（宿主为唯一来源，主界面按单加载）：

```
%APPDATA%\Mew\plugins.json          # { "clipboard-history": true, "todo-dll": false }，默认启用
%APPDATA%\Mew\plugin-snapshot.json   # 宿主扫描后写入的全量名单快照（主界面优先按此加载）
```

`T2` 启用/禁用需 `设置 → 插件 → 退出主界面进程` 后再经宿主托盘手动打开（ALC 卸载限制）；`T3` 无需重启宿主，重启插件进程即可。主界面不在插件列表中，不提供禁用。

五区视图命名约束（底部面板页签等）：标题必须带模块前缀（如 `指令台输出`，`id` 如 `mxd-output` 全局唯一），
不得使用通用名（`输出` 为 Launcher 历史遗留首占，后来者一律前缀）；`id` 冲突后注册者拒绝，标题重名仅靠约定避免。
插件多了页签成排时再迁往 VSCode 式共享通道（宿主独占「输出」面板 + `ILogService` 按通道写行，届时「插件日志」亦降级为一条通道）。

## 设置与日志

```
%APPDATA%\Mew\settings.json   # 根节 themeMode/overlayHotkey + 模块节（按插件 id 分）
%APPDATA%\Mew\layout.json     # Workbench 布局（含 settings 文档迁移）
%APPDATA%\Mew\host.log        # 呼出热键注册结果、插件目录与快照项数、主界面拉起/退出、插件崩溃/断开、ping/pong 可观测
%APPDATA%\Mew\plugin-host.log  # 主界面启动与未处理异常（崩溃排障先看它），另含插件来源（快照/本地扫描）与每个插件的
                              # 生命周期：加载成功（DLL 路径/大小/写入时间 → 模块类型）/ 未加载（原因）/ 跳过（exe 由宿主管理）；
                              # 同源内容亦展示于主界面底部面板「插件日志」视图（行内文本框，可选中复制）
```

设置入口：`设置 → 外观 / 热键 / 插件 / 数据（Launcher）`，插件列表显示 `已启用/已禁用/清单错误/ID 重复/需 JIT/已崩溃`，清单错误仅影响该插件。

## 热键

- 呼出浮层：`Alt+Space` 按一次呼出、再按隐藏；`设置 → 热键` 可启用/禁用/捕获改绑（`overlayHotkey` + `overlayHotkeyEnabled` 落盘，
  经 `overlayHotkeySet` 一次性 IPC 实时生效，冲突时点名占用方并回滚旧键）。宿主未运行时仅保存，重启宿主后生效；
  禁用后仍可经托盘呼出浮层，不会锁死。
- 插件热键：由清单 `capabilities.hotkeys` 声明，经宿主 `IHotkeyService` 集中注册，触发后经 `hotkeyTriggered` 通知归属插件

## 浮层搜索

`Overlay` 由宿主聚合多源结果：`每源上限 = 全局上限(8) / 源数`，再全局截断，扁平混排 + 行尾来源标记，唯一源时与单源时代一致。`T2` 复用 `ISearchSource`，`T3` 经 `JSON-RPC over NamedPipe`（管道名 `mew-host-<username>`）返回 `SearchResultDto`。

## 测试

```bash
dotnet test tests/Mew.Host.Tests --filter "PluginDiscovery|PluginHostComposition|IpcSearchAggregation|HotkeyAndSettingsProxy|FaultIsolation|PluginDllLoader"
dotnet test Mew.slnx
```

## 常见问题

- **主界面未启动**：经宿主窗口“打开主界面”按钮或托盘右键“打开主界面”手动拉起；先 `dotnet build Mew.slnx` 构建整个方案（单跑宿主不会连带构建主界面），再确认 `Mew.PluginHost.exe` 与 `Mew.Host.exe` 同目录，或已生成 `.build` fallback；点按钮无反应时看弹窗提示，详查 `host.log`
- **DLL 插件置灰**：仅分发了 AOT 单文件，需补 `Mew.PluginHost.exe`（JIT）
- **构建输出直接当插件目录**：支持，`entry.path` 钉死入口 DLL，同目录其他 DLL 仅作依赖探测；入口内多个模块实现时按清单 `id` 精确匹配（无匹配/多匹配均在设置页报错）；`runtimes/<rid>/native` 下的 native 库自动探测；目录自带的 `Mew.Workbench.dll` / `Aprillz.MewUI.*` 副本恒被忽略（宿主契约走 Default 单例，否则跨边界类型双份导致加载失败）
- **清单标红**：检查 `id` 重复、`version` 非 semver、`entry.path` 与 `type` 不匹配、`protocolVersion != 1`
- **热键注册失败**：已被其他程序占用或与已注册热键冲突，设置页会点名占用方
