# 01: MewDockShell 析出（反射触点集中 + Localize 迁移 + 菜单去重以表为准）

**What to build:** `Mew.Workbench` 长出 `MewDockShell` adapter（构造入 `DockingManager`）：`Tune()` 收拢 WorkbenchView 里全部 MewDock 反射触点（禁边框样式注册、TabSet 最大化禁用、细分隔条、分隔条光标、最大化按钮隐藏、tab 关闭悬浮接线）并以幂等为显式契约；静态 `Localize()` 收拢 `MewUIDockString` 字符串表设置（自扩展主机 `PluginHostApp` 迁入，调用时机不变）；静态 `PruneGroupMenu(menu)` 提取为纯函数，冗余匹配从硬编码中英文案改为字符串表当前值。`docking.Changed` 订阅留在 WorkbenchView（处理器改调 `shell.Tune()`），WorkbenchView 回归五区模型协调；停靠观感与行为逐项零变化。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] `Tune()` 覆盖全部 7 处反射调参方法与 4 个样式工厂；adapter 外无任何 MewDock internal/私有成员触碰
- [x] `Changed` 订阅留 WorkbenchView，处理器改调 `shell.Tune()`；重跑节奏与调参内容不变
- [x] `Localize()` 迁入 adapter；`PluginHostApp.Run` 开头改调，时机在 DockingManager 构造前不变
- [x] 菜单去重改取 `MewUIDockString.MenuAutoHide.Value / MenuClose.Value`；修剪提取为纯函数
- [x] `_paneContents`/SyncContent workaround 留在 WorkbenchView（边界判定线：模型协调不进 adapter）
- [x] 测试：菜单修剪纯函数（冗余项摘除、其余保留）、`Tune()` 幂等（真实 DockingManager 无头连调两次无异常、接线去重不重复）
- [x] 停靠观感与行为零回归：边框/最大化按钮/tab 关闭悬浮/分隔条/光标/菜单去重结果逐项对照 v0.2.4
- [x] 全量测试通过（现有 131 + 103 不回归）

## Comments

- 已完成。`MewDockShell`（Mew.Workbench，public）：实例 `Tune()`（5 个反射调参方法 + 边框覆盖样式注册）、静态 `Localize()`（字符串表全表迁移）、静态 `PruneGroupMenu` + 纯函数 `PruneGroupMenuTexts` + `RedundantGroupMenuTexts`（取表当前值）。WorkbenchView 781 → 440 行，仅剩五区模型协调。
- 实现要点：`DisableDockZoneBorders` 经 `StyleSheet.Define` **追加** rule（靠 GetByType 从后往前匹配生效），进 Tune 后若每次 Changed 重跑会无限追加——adapter 内加 `_zoneStylesApplied` 一次性守卫,首调注册、后续跳过（首次布局后子树未就绪时,首个 Changed 补注册,较旧行为更稳）。
- Tune 调用点三处等价替换：Build 中段（原禁最大化+细分隔条,须先于首次布局）、Build 尾段（原四连调）、Changed 处理器。幂等以 `ConfiguredTabCloseCount`（internal 测试缝）锁定:真实 DockingManager + 文档 tab,连调两次接线数不增。
- 测试 6 例:字符串表迁移生效、冗余集合取表值、修剪纯函数、真实 ContextMenu 修剪、Tune 幂等（接线数 1 稳定）、空停靠连调不抛。全量 137 + 103 绿。
