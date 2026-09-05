# MewUI 0.19.1 → 0.20.2:菜单/命令 API 迁移 + 运行验收

> **一句话定调：反射面与公共 API 在 0.20.2 全部存活（探测证实），编译断点只有菜单/命令系统 4 处；升级以 Windows 侧 AOT 发布 + 手工走查清单为硬门槛。**

ADR-000101-01 锁定 MewUI 0.19.1 的前提是「官方声明 API 不稳、升级须单独评审」。v0.2.5 的 MewDockShell 已把适配面收敛到单文件，本仓库随即做了探测性评审：临时升包编译 + 对 0.20.2 程序集元数据逐项核查。证据：①MewDockShell 全部反射触点存活——4 个 internal 类型名（FlexTabSetView/FlexTabButton/FlexSplitter/ExtendedBorderBar）、3 个私有字段（_model/_maximizeButton/_closeButton）、3 个模型属性（TabSetEnableMaximize/SplitterSize/IsColumnAxis，get/set 齐全）、MewUIDockString 字符串表；②WorkbenchView 依赖的全部公共 API 存活（DockingManager/WithContentFactory/LoadLayout/SaveLayout/AddToolPane/AddDocumentPane/TabMenuOpening/GroupMenuOpening）；③真实编译断点仅菜单/命令系统 4 处——`Menu.Item(文案, Action)` 改为要求 `MenuBar` + `Command`、`MenuItem.Click` 移除（0.20.0 三大支柱之一 commands 的直接冲击），集中在 `TitleBarBuilder` 与 `WorkbenchView`。锁 0.19.1 的机会成本持续累积：无窗口 `Application.Run`（宿主首启编排的官方解）、文本/绑定引擎、后续修复全部拿不到。

## Considered Options

- **继续锁 0.19.1**：官方「API 不稳」声明仍有效，但探测显示实际冲击可控且适配面已收敛；机会成本（无窗口 Run、修复、性能）持续增长。→ 未选（本次）。
- **分步升级 0.20.0 → 0.20.2**：0.20.0 本身就是最大断裂点，中间版各带破坏面，两轮评审无收益。→ 未选。
- **直升 0.20.2（选定）**：见下。

## 决策

1. **目标 0.20.2**（`Aprillz.MewUI` 与 `Aprillz.MewUI.MewDock` 两包同步），不取中间版；升后继续精确锁版——ADR-000101-01 的锁版机制不变，仅修订锁定版本号。
2. **菜单/命令 API 迁移**：标题栏菜单栏（TitleBarBuilder）、tab 右键菜单（WorkbenchView「在侧边栏定位」）按 0.20 新 Command API 改写；`GroupMenuOpening` 挂接点（MewDockShell.PruneGroupMenu 的入口）核对后接线。
3. **反射触点语义核对**：名字存活 ≠ 语义不变（原生弹窗重构后 `_maximizeButton` 是否照旧创建、`TabSetEnableMaximize=false` 是否仍阻止渲染、`Opacity` 首启编排是否如旧）——以手工走查清单逐项核对，失效项只动 MewDockShell 一个文件。
4. **验收硬门槛 = Windows 侧 AOT 发布 + 手工走查清单**（边框、最大化按钮、tab 关闭悬浮、分隔条粗细与光标、菜单去重、首启编排、托盘、浮层、设置页）。本机（WSL）仅有编译 + 测试能力，测试绿不免除走查。
5. **无窗口 `Application.Run` 评估**：可行则用官方路径替换宿主 0 透明度首启技巧——升级回报票，非门槛，验证依赖 Windows 环境。

## Consequences

- ADR-000101-01 修订：锁定版本 0.19.1 → 0.20.2；「升级是显式决策、须评审发布说明」的要求原样延续，本地参考 clone 同步 checkout v0.20.2。
- MewDockShell 使升级排查面保持单文件：走查失效项的修复只动这一个文件；本版探测证据（存活清单）作为后续升级评审的基线。
- 菜单/命令迁移后 TitleBarBuilder/WorkbenchView 的菜单代码为新形态，具体以 0.20 编译器与文档为准。
- AOT 与走查门槛未过前，升级不得宣称完成（即使编译与测试全绿）。

## Alternatives Rejected

- 见 Considered Options；继续锁版与分步升级均因机会成本或评审成本被放弃。
