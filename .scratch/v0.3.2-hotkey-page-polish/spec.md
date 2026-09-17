# v0.3.2 热键页打磨

设置→热键页的体验收尾：开关语义化、恢复默认、冲突预检、IPC 失败回滚。
背景与分析见 Mxd 会话（grill-with-docs：设置→热键页分析）。

## 代码内改动（本仓 src/Mew.PluginHost/PluginHostApp.cs，已完成待走查）

- 「禁用/启用」变文字按钮 → ToggleSwitch（MewUI 0.20.2 控件）+「启用」标签；禁用态联动禁用「更改」
- 新增「恢复默认」按钮（默认 Alt+Space，仅当前值 ≠ 默认时显示）
- 捕获解析通过后、应用前 FindOwner 冲突预检（宿主侧权威校验仍由 IPC ack 兜底）
- IPC ack 报错时回滚本地落盘的 OverlayHotkey/OverlayHotkeyEnabled

## Tickets

- 01 Launcher 启动项热键改按键捕获控件（Blocked by: none）
