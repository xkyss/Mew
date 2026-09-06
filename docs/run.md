# 运行与发布

> 对应版本：`v0.2.3` 插件生命周期状态（`docs/adr/000203-01-plugin-lifecycle-states.md`）；宿主独立启动见 `docs/adr/000202-01-host-independent-startup.md`

## 先决条件

- Windows 10/11（`Direct2D` + `Win32` 热键/托盘/浮层）
- .NET SDK `10.0.400+`（`dotnet --version`）
- WSL/Linux 仅可跑 `tests`，不可跑 UI；Linux 产物进 `.build-linux/`，与 Windows 的 `.build/` 隔离，互不干扰

## 项目结构

```
src/Mew.Host/        — 宿主常驻（AOT，托盘/热键/浮层框架/插件发现/IPC 路由）
src/Mew.PluginHost/  — 主界面（JIT，Workbench 五区 + T2 运行期 DLL 插件，不再编译进任何工具模块）
src/Mew.Workbench/   — 主框架（五区、主题、布局、Overlay 聚合、IPC 契约）
src/Mew.Launcher/    — 示例工具模块（标准 T2 插件，自带 plugin.json）
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
- 托盘左键呼出搜索浮层，右键菜单可打开/重启主界面/进插件管理
- 开发期 fallback 路径：`Host` 会到 `.build/Mew.PluginHost/bin/Debug/net10.0-windows/Mew.PluginHost.exe`
- 托盘常驻：首启即隐藏，左键呼出/隐藏浮层，右键 `打开主界面` / `重启主界面` / `插件管理` / `退出`，浮层 `Alt+Space` 按一次呼出、再按隐藏；失败告警（热键被占、主界面缺失）会亮出主窗口
- `插件管理`（或宿主窗口）以**插件管理面板**列出独立插件（T3 exe）行：`已启用/已禁用/已崩溃` + 单动作（启用/禁用/重启），不依赖主界面活着即可管理；主界面设置→插件的 DLL 行共用同一面板（渲染与动作路由只此一份，ADR-000204）
- 退出宿主不再终止主界面进程；运行中主界面退出后仅标记，需手动重启（自动拉起只发生在宿主启动时）

单独调试主界面：

```bash
dotnet run --project src/Mew.PluginHost/Mew.PluginHost.csproj
```

> 主界面不再内置任何工具模块：本地联调把构建输出以**链接**挂进插件主目录
> （`mklink /J %APPDATA%\Mew\Plugins\launcher <构建输出目录>`，指 WSL 路径用 `mklink /D` 并开启开发者模式），
> 否则打开是只有设置的空壳。

## 发布（双 exe）

默认分发为双 exe 同目录：

```bash
dotnet publish src/Mew.Host -c Release -r win-x64 /p:PublishAot=true -o publish
dotnet publish src/Mew.PluginHost -c Release -r win-x64 -o publish
# 产物：
# publish/Mew.Host.exe       — AOT，秒开常驻
# publish/Mew.PluginHost.exe — JIT，可加载 DLL 插件
# 另需 Launcher（标准 T2，随包分发）：
dotnet publish src/Mew.Launcher -c Release -r win-x64 -o publish/Plugins/launcher
# publish/Plugins/launcher/Mew.Launcher.dll + plugin.json
```

单 AOT 回退：仅分发 `Mew.Host.exe` 时，`T2`（`type=dll`）插件在 `设置 → 插件` 置灰并提示“需 JIT 主界面”，`T3`（`type=exe`）仍可用。

## 插件放置

**单一主插件目录**（ADR-000301，默认用户目录，可在 `设置 → 插件` 修改）：

```
<主目录>\<id>\plugin.json   # 每个子目录 = 一个插件；链接子目录同样被扫描（开发联调通道）
```

修改主目录：`设置 → 插件 → 插件目录 → 修改`（文件夹选择器），重启宿主后生效。
卸载 = 删除主目录下对应插件子目录。

开发期免复制联调（ADR-000301）：把构建输出以**链接**挂进主目录，改代码后只需
`dotnet build` + 重启主界面（T2 的 ALC 限制），无需复制、无需重启宿主：

```powershell
# 本机卷路径：junction 免特权
mklink /J "%APPDATA%\Mew\Plugins\mxd" "D:\code\Mxd\.build\Mxd.UI\bin\Debug\net10.0-windows"
# WSL/UNC 目标：目录符号链接，需开发者模式（或管理员）
mklink /D "%APPDATA%\Mew\Plugins\mxd" "\\wsl.localhost\Ubuntu-24.04\home\xkyii\code\Mxd\.build\Mxd.UI\bin\Debug\net10.0-windows"
```

旧 `settings.json` 的 `pluginDirs` 列表会在启动时自动迁移为单值 `pluginDir`（取首项），
其余条目由 `host.log` 提示人工以链接挂入。

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

生命周期词汇见 ADR-000203：**卸载仅指 uninstall**（把插件目录移出受管范围），运行态释放写作**回收/释放**，不再用「卸载 ALC」式措辞。行为分型：

- `T2`（DLL）：启用/禁用经宿主落盘（`pluginEnableSet` 一次性 IPC，宿主为 `plugins.json` 唯一写者）。禁用即意图落盘；本会话仍加载时行上带「重启扩展主机后生效」副提示，下次主界面启动不再装载（不做运行时 ALC 释放）。
- `T3`（独立 exe）：由宿主窗口/托盘「插件管理」入口管理。禁用即杀进程回收（来源立即消失）；进程意外退出标「已崩溃」，行内动作变「重启」由宿主重新拉起；崩溃标记不落盘、宿主重启后按启用态重拉，防循环（启动后短时崩 3 次，本次运行不再自动拉起，仅手动重启）。
- 主界面不在插件列表中，不提供禁用。

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

设置入口：`设置 → 外观 / 热键 / 插件 / 数据（Launcher）`，插件管理面板显示 `已启用/已禁用/清单错误/ID 重复/需 JIT/已崩溃`，清单错误仅影响该插件。

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
