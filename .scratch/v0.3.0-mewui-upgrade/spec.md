# v0.3.0 MewUI 0.20.2 升级规格

Status: ready-for-agent

> 版本: v0.3.0
> 对应 ADR: `docs/adr/000300-01-mewui-020-upgrade.md`（修订 ADR-000101-01 锁定版本号）

## Problem Statement

ADR-000101-01 锁定 MewUI 0.19.1 的机会成本持续累积：0.20.0 的三大支柱重构（文本引擎/无反射绑定/命令系统）、原生弹窗、**无窗口 `Application.Run`**（宿主 0 透明度首启技巧的官方替代）、后续修复与性能全部拿不到。v0.2.5 的 MewDockShell 已把适配面收敛到单文件，探测性评审给出可行性证据：反射触点与 WorkbenchView 公共 API 在 0.20.2 全部存活，真实编译断点仅菜单/命令系统 4 处（`TitleBarBuilder` 与 `WorkbenchView` 的 `Menu.Item`/`ContextMenu.Item`/`MenuItem.Click`）。升级的剩余成本在编译之外：反射触点的**语义**核对（原生弹窗重构后行为是否如旧）与 Windows 侧 AOT/走查验收。

## Solution

两包直升 0.20.2 并继续精确锁版（ADR-000101-01 机制不变、版本号修订）：按新 Command API 迁移 4 处菜单断点；全量测试回归；**Windows 侧 AOT 发布 + 手工走查清单**为验收硬门槛（测试绿不免除）；可选评估用无窗口 `Application.Run` 替换宿主首启技巧。

## User Stories

1. As 框架主人, I want MewUI 升至 0.20.2 且两包同步精确锁版, so that 拿到框架演进收益且升级仍是显式决策。
2. As 用户, I want 停靠界面观感与行为零变化（边框/最大化按钮/tab 关闭悬浮/分隔条/光标/菜单去重）, so that 升级无感。
3. As 用户, I want 标题栏菜单栏照常工作（File/Help、访问键）, so that 菜单 API 迁移无感。
4. As 用户, I want tab 右键「在侧边栏定位」照常可用, so that 反向导航不受升级影响。
5. As 用户, I want 底部分组菜单继续去重自动隐藏/关闭项, so that v0.2.1 的菜单收敛行为保留。
6. As 用户, I want 宿主首启无窗口闪烁、托盘/浮层/热键常驻如旧, so that 升级不引入常驻能力回归。
7. As 框架主人, I want MewDockShell 反射触点在真实运行中逐项核对（走查清单）, so that 静默失效被拦在验收前。
8. As 框架主人, I want 全量测试通过（现有 137 + 103）, so that 逻辑回归有底线。
9. As 框架主人, I want Windows 侧 AOT 发布成功, so that 秒开常驻不受影响。
10. As 开发者, I want 探测证据（反射面/公共 API 存活清单）随 ADR 归档, so that 后续升级评审有基线。
11. As 文档读者, I want ADR-000101-01 修订记录新锁版号, so that 锁版决策可追溯。
12. As 框架主人, I want 无窗口 `Application.Run` 的官方支持得到评估并在可行时替换 0 透明度技巧, so that 首启编排从 workaround 变官方路径（可选票）。

## Implementation Decisions

- **目标 0.20.2**：两包同步直升,不取 0.20.0/0.20.1 中间版;升后 csproj 精确锁版。
- **菜单/命令迁移**：按 0.20 新 Command API 改写标题栏菜单与 tab ContextMenu（具体形态以编译器与 0.20 文档为准）;`GroupMenuOpening`/`TabMenuOpening` 事件在 0.20.2 存在（add_/remove_ 已核实）,挂接点保留。
- **反射触点语义核对**：MewDockShell 的 4 类型名/3 私有字段/3 属性/字符串表逐一在真实运行核对;失效项只动 MewDockShell。
- **验收硬门槛**：Windows 侧 `dotnet publish -r win-x64`（AOT）+ 手工走查清单;本机（WSL）测试绿不免除。
- **无窗口 Run 评估**：可行则替换 `MewHost` 的 0 透明度 + `Hide()` 编排（行为契约:首启无可见帧、告警路径亮窗、托盘常驻）;不可行则维持现状并记录。
- **MewUI 版本探测结论不得作为完成依据**：编译零错误 ≠ 语义零漂移。

## Testing Decisions

- **全量测试**：Mew.Host.Tests 137 + Mew.Launcher.Tests 103 必须绿;`MewDockShellTests` 的真实 `ContextMenu` 用例按新菜单 API 调整构造方式。
- **手工走查清单（Windows,验收硬门槛）**：停靠区无边框、TabSet 最大化按钮不出现、tab 关闭按钮悬浮显示且 tab 宽度稳定、分隔条最细 + 缩放光标、底部分组菜单无自动隐藏/关闭项、菜单文案中文、窗口拖动/布局持久化、宿主首启无闪烁、托盘菜单与浮层、设置→外观/热键/插件、AOT 发布产物可运行。
- **先前技艺**：无头控件树测试（组合测试、PluginAdminPanelTests、MewDockShellTests）在 WSL 可跑;视觉/编排项必须真实窗口。

## Out of Scope

- **不做 0.20 新特性重构**：绑定引擎、文本引擎、Command 系统全面接入、GridView 等——除无窗口 Run 评估外,只做「编译通过 + 行为等价」所需的最小迁移。
- **不做架构走查其余候选**（插件目录配方、settings 单写者、LauncherModule 拆分）。
- **不做 MewUI 本地参考 clone 的管理自动化**（人工 checkout v0.20.2 即可）。

## Further Notes

- 版本号常量 `AppVersion` 随升级票提升为 `v0.3.0`（底层框架大版本迁移,配得上 minor——grilling 已确认口径）。
- 探测证据（2026-09-05）:0.20.2 程序集元数据核查反射触点全存活;探测性编译断点 4 处全在菜单/命令系统;探测后 csproj 已还原 0.19.1。
- `docs/adr/000101-01` 已加修订注记;本地参考 clone 需人工 checkout `v0.20.2`。
- 票据 03（AOT + 走查）为 `ready-for-human`——只有 Windows 环境能执行;票据 04（无窗口 Run 评估）为可选。
