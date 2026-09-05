# MewDockShell：MewDock 适配析出为幂等 adapter

> **一句话定调：凡触碰 MewDock internal/私有成员或字符串表的进 adapter，凡协调 Workbench 模型的留视图；Tune() 幂等可反复调用，菜单去重以字符串表当前值为准。**

`WorkbenchView`（781 行）三类职责混居：五区模型协调（布局恢复、区域显隐、工具窗格同步——本职）+ **MewDock 行为调参**（7 个方法、4 个样式工厂、3 处私有字段反射 `_model`/`_maximizeButton`/`_closeButton`、按类型名反射 internal 控件、tab 关闭接线去重缓存）+ 菜单去重。调参方法在每次 `docking.Changed` 全量重跑（事实幂等但无契约）；菜单去重硬编码匹配「自动隐藏/Auto Hide/关闭/Close」双语字符串——43af42c 本地化提交改的正是这批文案，去重靠两处手工同步才没踩断；`MewUIDockString` 字符串表设置则住在扩展主机 exe（`PluginHostApp.LocalizeDockStrings`），依附在错误的一层。MewUI 0.19.1 之后已发布 0.20.0–0.20.2，官方声明次版本间可能破坏性变更（ADR-000101-01 锁版）——当前升级的反射排查面散在视图各处，没有单一入口。

## Considered Options

- **维持现状**：升级 0.20.x 时逐个反射点试错，菜单文案仍靠双处同步。→ 未选，风险无落点。
- **三模块拆分**（视图 / 适配 / 样式各一）：样式工厂本身也是 MewDock 适配的一部分（针对 internal 类型按名反射注册），拆三份只增文件不增边界。→ 未选。
- **MewDockShell 单 adapter（选定）**：见下。

## 决策

1. **模块落位与形状**：`MewDockShell` 落 `Mew.Workbench`（与 DockingManager 同层），实例类构造入 `DockingManager`。
2. **接口收敛**：`Tune()`——实例方法，涵盖全部反射触点（停靠区禁边框样式注册、TabSet 最大化禁用、细分隔条尺寸、分隔条光标接线、最大化按钮视图层隐藏、tab 关闭按钮悬浮接线）；静态 `Localize()`——`MewUIDockString` 字符串表设置；静态 `PruneGroupMenu(menu)`——分组菜单去重，提取为纯函数。
3. **订阅留在视图**：`docking.Changed` 订阅不动（还要保存布局、回写区域显隐），处理器内改调 `shell.Tune()`；调用节奏不变（每次 Changed 全量重跑），「Tune() 幂等、可安全反复调用」从事实升格为契约并以测试锁定。
4. **Localize 迁移**：`LocalizeDockStrings` 从扩展主机迁入 adapter，`PluginHostApp.Run` 开头改调，调用时机不变（任何 DockingManager 构造之前），零行为差异。
5. **菜单文案以表为准**：去重匹配从硬编码改为 `MewUIDockString.MenuAutoHide.Value / MenuClose.Value` 当前值（单一事实来源）；行为等价——本地化写的正是这张表。
6. **边界判定线**：凡触碰 MewDock internal/私有成员或其字符串表的，进 adapter；凡协调 Workbench 模型的（布局恢复/默认窗格/未知窗格关闭/区域显隐/`_paneContents` 内容实例缓存与 SyncContent workaround），留 `WorkbenchView`——内容实例缓存是 Workbench 自身的内容解析语义，不是 MewDock 内部触碰。
7. **零回归**：停靠界面观感与行为逐项不变（边框、最大化按钮、tab 关闭悬浮、分隔条粗细与光标、菜单去重结果）。

## Consequences

- `WorkbenchView` 回归五区模型协调；MewUI 0.20.x 升级评审的排查面收缩为一个文件——「我们碰了它的哪些内部」一目了然。
- 菜单文案与去重不再两处同步；字符串表设置住在正确的层。
- 测试新增：菜单修剪纯函数（fake 菜单项 + 字符串表当前值）、`Tune()` 幂等（真实 `DockingManager` 无窗口连调两次）。
- 本 ADR 是 ADR-000101-02（MewDock 引擎）与 ADR-000101-01（锁版）的**配套**：为未来 MewUI 升级铺路，不改变锁版决策——0.20.x 升级仍是后续独立版本，须单独评审发布说明。
- 词汇：不新增 CONTEXT.md 词条——`MewDockShell` 是实现层 adapter 名，非领域概念。

## Alternatives Rejected

- 见 Considered Options；现状与三模块拆分均因排查面无单一入口或边界冗余被放弃。
