# 运行与发布

> 对应版本：`v0.2.1` 三层插件宿主（`docs/adr/000201-01-three-layer-plugin-host.md`）

## 先决条件

- Windows 10/11（`Direct2D` + `Win32` 热键/托盘/浮层）
- .NET SDK `10.0.400+`（`dotnet --version`）
- WSL/Linux 仅可跑 `tests`，不可跑 UI

## 项目结构

```
src/Mew.Host/        — 宿主常驻（AOT，托盘/热键/浮层框架/插件发现/IPC 路由）
src/Mew.PluginHost/  — 扩展主机（JIT，Workbench 五区 + T1/T2 工具模块）
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

`Mew.Host` 启动后自动按需拉起 `Mew.PluginHost`：

- 首启即拉起（保证 Launcher 可见）
- 开发期 fallback 路径：`Host` 会到 `.build/Mew.PluginHost/bin/Debug/net10.0-windows/Mew.PluginHost.exe`
- 托盘常驻：关闭主窗口仅隐藏，托盘 `显示` / `退出`，浮层 `Ctrl+Alt+Space`

单独调试扩展主机：

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

单 AOT 回退：仅分发 `Mew.Host.exe` 时，`T2`（`type=dll`）插件在 `设置 → 插件` 置灰并提示“需 JIT 扩展主机”，`T3`（`type=exe`）仍可用。

## 插件放置

扫描目录（递归一层）：

```
%APPDATA%\Mew\Plugins\<id>\plugin.json   # 用户目录
<exe-dir>\Plugins\<id>\plugin.json       # 安装目录
```

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

启用态：

```
%APPDATA%\Mew\plugins.json   # { "clipboard-history": true, "todo-dll": false }，默认启用
```

`T2` 启用/禁用需 `设置 → 插件 → 重启扩展主机` 后生效（ALC 卸载限制）；`T3` 无需重启宿主，重启插件进程即可。

## 设置与日志

```
%APPDATA%\Mew\settings.json   # 根节 themeMode/overlayHotkey + 模块节（按插件 id 分）
%APPDATA%\Mew\layout.json     # Workbench 布局（含 settings 文档迁移）
%APPDATA%\Mew\host.log        # 扩展主机拉起/退出、插件崩溃/断开、ping/pong 可观测
```

设置入口：`设置 → 外观 / 热键 / 插件 / 数据（Launcher）`，插件列表显示 `已启用/已禁用/清单错误/ID 重复/需 JIT/已崩溃`，清单错误仅影响该插件。

## 热键

- 呼出浮层：`Ctrl+Alt+Space`（可在 `设置 → 热键` 捕获改绑，冲突时点名占用方）
- 插件热键：由清单 `capabilities.hotkeys` 声明，经宿主 `IHotkeyService` 集中注册，触发后经 `hotkeyTriggered` 通知归属插件

## 浮层搜索

`Overlay` 由宿主聚合多源结果：`每源上限 = 全局上限(8) / 源数`，再全局截断，扁平混排 + 行尾来源标记，唯一源时与单源时代一致。`T2` 复用 `ISearchSource`，`T3` 经 `JSON-RPC over NamedPipe`（管道名 `mew-host-<username>`）返回 `SearchResultDto`。

## 测试

```bash
dotnet test tests/Mew.Host.Tests --filter "PluginDiscovery|PluginHostComposition|IpcSearchAggregation|HotkeyAndSettingsProxy|FaultIsolation|PluginDllLoader"
dotnet test Mew.slnx
```

## 常见问题

- **扩展主机未拉起**：确认 `Mew.PluginHost.exe` 与 `Mew.Host.exe` 同目录，或已 `dotnet build` 生成 `.build` fallback；查看 `host.log`
- **DLL 插件置灰**：仅分发了 AOT 单文件，需补 `Mew.PluginHost.exe`（JIT）
- **清单标红**：检查 `id` 重复、`version` 非 semver、`entry.path` 与 `type` 不匹配、`protocolVersion != 1`
- **热键注册失败**：已被其他程序占用或与已注册热键冲突，设置页会点名占用方
