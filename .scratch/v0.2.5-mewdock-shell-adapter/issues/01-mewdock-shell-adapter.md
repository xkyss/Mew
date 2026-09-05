# 01: MewDockShell 析出（反射触点集中 + Localize 迁移 + 菜单去重以表为准）

**What to build:** `Mew.Workbench` 长出 `MewDockShell` adapter（构造入 `DockingManager`）：`Tune()` 收拢 WorkbenchView 里全部 MewDock 反射触点（禁边框样式注册、TabSet 最大化禁用、细分隔条、分隔条光标、最大化按钮隐藏、tab 关闭悬浮接线）并以幂等为显式契约；静态 `Localize()` 收拢 `MewUIDockString` 字符串表设置（自扩展主机 `PluginHostApp` 迁入，调用时机不变）；静态 `PruneGroupMenu(menu)` 提取为纯函数，冗余匹配从硬编码中英文案改为字符串表当前值。`docking.Changed` 订阅留在 WorkbenchView（处理器改调 `shell.Tune()`），WorkbenchView 回归五区模型协调；停靠观感与行为逐项零变化。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] `Tune()` 覆盖全部 7 处反射调参方法与 4 个样式工厂；adapter 外无任何 MewDock internal/私有成员触碰
- [ ] `Changed` 订阅留 WorkbenchView，处理器改调 `shell.Tune()`；重跑节奏与调参内容不变
- [ ] `Localize()` 迁入 adapter；`PluginHostApp.Run` 开头改调，时机在 DockingManager 构造前不变
- [ ] 菜单去重改取 `MewUIDockString.MenuAutoHide.Value / MenuClose.Value`；修剪提取为纯函数
- [ ] `_paneContents`/SyncContent workaround 留在 WorkbenchView（边界判定线：模型协调不进 adapter）
- [ ] 测试：菜单修剪纯函数（冗余项摘除、其余保留）、`Tune()` 幂等（真实 DockingManager 无头连调两次无异常、接线去重不重复）
- [ ] 停靠观感与行为零回归：边框/最大化按钮/tab 关闭悬浮/分隔条/光标/菜单去重结果逐项对照 v0.2.4
- [ ] 全量测试通过（现有 131 + 103 不回归）

## Comments

