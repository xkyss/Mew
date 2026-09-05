# v0.2.5 MewDockShell 适配析出规格

Status: ready-for-agent

> 版本: v0.2.5
> 对应 ADR: `docs/adr/000205-01-mewdock-shell-adapter.md`

## Problem Statement

`WorkbenchView` 781 行三类职责混居：五区模型协调（本职）+ MewDock 行为调参（7 个方法、4 个样式工厂、3 处私有字段反射、internal 控件按类型名反射、tab 关闭接线去重缓存）+ 菜单去重。调参在每次 `docking.Changed` 全量重跑，幂等只是事实没有契约；菜单去重硬编码匹配中英双语字符串，与 `LocalizeDockStrings` 两处手工同步，上次本地化提交就差点踩断；`MewUIDockString` 字符串表设置住在扩展主机 exe，依附在错误的一层。MewUI 0.20.0–0.20.2 已发布而锁版未动（ADR-000101-01），升级的反射排查面没有单一入口——凡升版必碎，且碎得无声。

## Solution

析出 `MewDockShell` adapter（落 `Mew.Workbench`，构造入 `DockingManager`）：`Tune()` 收拢全部反射触点并以「幂等、可安全反复调用」为显式契约；`Localize()` 收拢字符串表设置（自扩展主机迁入）；`PruneGroupMenu(menu)` 收拢分组菜单去重并提取为纯函数，匹配来源改为 `MewUIDockString` 当前值。`docking.Changed` 订阅留在 WorkbenchView（处理器改调 `shell.Tune()`），调用节奏不变。WorkbenchView 回归五区模型协调。停靠界面观感与行为逐项零变化。

## User Stories

1. As 框架主人, I want 全部 MewDock 反射触点集中在一个 adapter, so that MewUI 升级评审有单一排查入口、升级风险有落点。
2. As 框架主人, I want WorkbenchView 只做五区模型协调, so that 视图变更与库适配变更互不干扰。
3. As 框架主人, I want 「Tune() 幂等、可安全反复调用」成为显式契约并有测试锁定, so that 反射接线去重与调参安全不随重构漂移。
4. As 用户, I want 停靠菜单去重继续只摘与标题栏独立按钮同义的项, so that 底部分组菜单行为与 v0.2.4 完全一致。
5. As 框架主人, I want 菜单去重匹配字符串表当前值而非硬编码文案, so that 改本地化文案不再两处同步、不再踩断去重。
6. As 框架主人, I want `MewUIDockString` 字符串表设置迁出扩展主机 exe、住进 adapter, so that MewDock 适配住在正确的层、不再跨进程漂移。
7. As 用户, I want 停靠界面观感与行为零变化（边框、最大化按钮、tab 关闭悬浮、分隔条、光标）, so that 重构完全无感。
8. As 开发者, I want 菜单修剪为纯函数并可无头测试, so that 文案匹配逻辑有锁定。
9. As 开发者, I want `Tune()` 幂等有真实 DockingManager 的无头测试, so that 反射调参在无窗口环境可验证。
10. As 开发者, I want adapter 内反射触点清单一目了然（类型名/私有成员名集中可数）, so that 升级 0.20.x 时逐项核对。
11. As 文档读者, I want ADR 记录「进 adapter / 留视图」的边界判定线, so that 未来新增 workaround 知道放哪。
12. As 框架主人, I want 分两步落地（提取+测试 → 版本扫尾）, so that 每步可独立验收。

## Implementation Decisions

- **模块落位**：`MewDockShell` 落 `Mew.Workbench`，实例类构造入 `DockingManager`；不新建工程。
- **接口**：实例方法 `Tune()`（禁边框样式注册、TabSet 最大化禁用、细分隔条尺寸、分隔条光标、最大化按钮视图层隐藏、tab 关闭悬浮接线）；静态 `Localize()`（字符串表）；静态 `PruneGroupMenu(menu)`（纯函数）。
- **订阅留视图**：`docking.Changed` 订阅仍在 WorkbenchView（另存布局、回写区域显隐），处理器改调 `shell.Tune()`；每次 Changed 全量重跑的节奏不变，幂等升格为契约。
- **Localize 迁移**：自 `PluginHostApp.Run` 开头改调 `MewDockShell.Localize()`，时机不变（任何 DockingManager 构造之前）。
- **菜单文案以表为准**：冗余集合取 `MewUIDockString.MenuAutoHide.Value / MenuClose.Value`，不再硬编码；修剪逻辑为纯函数（入参：菜单项文本序列 + 冗余集合）。
- **边界判定线**：触碰 MewDock internal/私有成员或字符串表的进 adapter；协调 Workbench 模型的留视图——`_paneContents` 实例缓存与 SyncContent workaround 属后者，留在 WorkbenchView。
- **零回归**：样式值、行为、文案逐项不变；调用时机不变。

## Testing Decisions

- **好测试的标准**：只测外部行为——修剪结果、幂等重入安全；不测反射内部细节与窗口像素。
- **单一高位缝**：宿主测试工程（沿用 v0.2.4 纪律）：① 菜单修剪纯函数（fake 菜单项 + 字符串表当前值，断言冗余项摘除、其余保留）；② `Tune()` 幂等（真实 `DockingManager` 无窗口构建——组合测试已证明可行——连调两次断言无异常、tab 关闭接线去重不重复）；③ 全量回归（现有 131 + 103）。
- **先前技艺**：`PluginAdminPanelTests` 的零句柄控件树断言、组合测试的无窗口 `Workbench.Build()`。

## Out of Scope

- **不升 MewUI 0.20.x**：ADR-000101-01 锁版维持，0.20.2 升级为后续独立版本（评审发布说明 + 新 ADR），本版只为其铺路。
- **不动五区模型协调**：布局恢复、默认/未知窗格、区域显隐、`_paneContents` 语义原样。
- **不改停靠观感**：样式工厂的值逐项照搬，无视觉变化。
- **不做架构走查其余候选**（插件目录配方、settings 单写者、LauncherModule 拆分等）。

## Further Notes

- 版本号常量 `AppVersion` 在实现开始时提升为 `v0.2.5`（单行，先例 `6e02e1a`/`ba2885e`）。
- 不新增 CONTEXT.md 词条：`MewDockShell` 是实现层 adapter 名，非领域概念（grilling 确认）。
- 本规格源自架构走查候选 3，grilling 六项决策全部经用户确认；MewUI 0.20.x 发布说明摘要（无窗口 Application.Run、原生弹窗重构、ToolBar/CanClick 破坏性变更）已录于对话，升级决策待本版落地后另立版本评审。
