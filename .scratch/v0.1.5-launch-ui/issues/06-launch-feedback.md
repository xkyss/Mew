# 06 — 启动反馈：成功静默、失败醒目

**What to build:** 启动成功静默（状态栏轻文字或完全静默）；启动失败弹出 toast 提示并在状态栏以红色显示，输出面板照记日志。

**Blocked by:** 01 — 列表单击启动与双击防重.

**Status:** resolved

- [x] 启动成功不弹打扰性提示（状态栏轻文字或静默）。
- [x] 启动失败弹出 toast，状态栏红色显示失败信息，输出面板照记日志。
- [x] 反馈逻辑挂在统一启动路径上（主窗口列表与每项热键共用）。

## Comments

- 实现提交 `8f567fc` feat(launcher): 启动失败 toast 与状态栏红字反馈(成功静默)。
- 反馈收敛在 `LaunchItem`（主窗口列表与每项热键的共同路径）：成功 = 状态栏轻文字并复位颜色；失败 = toast（`ShowToast`）+ 状态栏红字（`HotkeyWarning` 警示红）+ 输出面板照记。
- Workbench 新增 `SetStatusTextColor(id, Color?)`（null 复位区前景）；`WorkbenchConfigurationTests` 增 1 例（变红 + 置 null 恢复），整体 61/61 通过。
- 待手工冒烟：启动不存在路径/无效命令 → 失败 toast + 状态栏红字 + 输出面板日志；成功启动不弹提示；再成功一次后状态栏颜色恢复区前景；主窗口隐藏时热键启动失败的 toast 可见性（边缘）。
