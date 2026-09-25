# v0.3.4 MewUI 0.21.1 升级规格

Status: ready-for-agent

> 版本: v0.3.4
> 对应 ADR: `docs/adr/000304-01-mewui-021-upgrade.md`（修订 ADR-000101-01 锁定版本号）
> 前置: develop 上 v0.3.2 热键页工作收尾提交后再开工本票

## Problem Statement

MewUI 0.20.2 之后发布 0.21.0/0.21.1（2026-09-07,两包同步）。发布说明评审结论（grilling 已确认）:

- **破坏性变更仅 2 处,均不波及本仓库源码**:`ToolTipProperty`/`ContextMenuProperty` 声明位置从 `Control` 移至 `FrameworkElement`（源码兼容,本仓库未直接引用这两个字段）;`TreeView.ItemTemplate` 返回值语义变化（本仓库未读取该属性）。
- **本仓库用到 2 个废弃 API**,全部集中在 `src/Mew.Launcher/LauncherModule.cs` 单文件:`ItemsSource` 赋值 ×3（分类树 `:307`、类型过滤下拉 `:754`）、`ContextMenu.ShowAt` ×2（`:349`、`:691`）。已核实行为暂不受影响（分类树每次刷新整体重建视图重新赋值;下拉数据为静态列表）,迁移属卫生性质。
- **拿不到的修复/收益**:#175（`Application.Run` 前建窗主题错误——正中 `NativeChromeWindow.CurrentTheme()` 手写回退场景）、#243 弹层关闭后焦点不返回、#236 滚动误关弹层、X11 剪贴板/按键修复、Linux DPI、渲染资源内存池（常驻托盘进程直接受益）。

锁版机制（ADR-000101-01）下升级是显式决策,本票即本轮评审与执行。

## Solution

两包直升 0.21.1 并继续精确锁版（ADR-000101-01 机制不变、版本号修订）:迁移 5 处废弃 API（单文件）;全量测试回归;**Windows 侧 AOT 发布 + 手工走查清单**为验收硬门槛（测试绿不免除）;不采用 0.21 新特性。

## User Stories

1. As 框架主人, I want MewUI 升至 0.21.1 且两包同步精确锁版, so that 拿到内存池与修复收益且升级仍是显式决策。
2. As 用户, I want 界面观感与行为零变化（标题栏/托盘/浮层/停靠/设置页）, so that 升级无感。
3. As 用户, I want 分类树右键菜单与类型过滤下拉照常工作, so that 废弃 API 迁移无感。
4. As 框架主人, I want 全量测试通过（现有 Mew.Host.Tests 168 + Mew.Launcher.Tests 104）, so that 逻辑回归有底线。
5. As 框架主人, I want Windows 侧 AOT 发布成功且走查清单全绿, so that 静默失效（反射触点语义漂移）被拦在验收前。
6. As 框架主人, I want 废弃 API 清零, so that 下次升级排查面不累积。
7. As 文档读者, I want ADR-000101-01 修订记录新锁版号, so that 锁版决策可追溯。

## Implementation Decisions

- **目标 0.21.1**:两包同步直升,不取 0.21.0 中间版;升后 csproj 精确锁版。
- **废弃 API 迁移**（仅 `LauncherModule.cs`）:`ItemsSource` 赋值改 `ItemsView.Create(...)` / `ItemsView` 重载（具体形态以编译器与 0.21 文档为准;`TreeItemsView` 的选择 API 如 `SelectSingle` 保持不动,先验证视图复用后选择行为如旧）;`ShowAt` 改 `Placement` + `Show(target)`。
- **不采用新特性**:命令参数（`Register<TArg>`/`CommandData`）、`RenderResourceMetrics`、行容器钩子、WASM 等一律不接入;#175 相关只验证行为（首启/切主题路径）,`NativeChromeWindow` 回退逻辑的简化留后续票。
- **评审方式简化**:0.21 破坏面已逐项核实,不做独立探测性编译——直接升包编译,断点（若有）记录进 ADR。
- **本地参考 clone**:已不存在（`~/code` 下无 MewUI）,本次评审依据 = NuGet 包元数据 + 发布说明;clone 按需再建,不作为流程环节。

## Testing Decisions

- **全量测试**:Mew.Host.Tests 168 + Mew.Launcher.Tests 104 必须绿;涉菜单/树用例如因 0.21 构造方式调整,只动构造不动断言语义。
- **手工走查清单（Windows,验收硬门槛）**:在现有 `.scratch/windows-walkthrough-checklist.md` 基础上追加 0.21 专项——分类树右键菜单（Placement 迁移后弹出位置正确）、类型过滤下拉、切主题（含启动前建窗主题正确性,#175 验证）、弹层关闭后焦点返回主窗（#243 验证）、滚动不误关弹层（#236）、托盘/热键/首启编排回归。
- **先前技艺**:WSL 只有编译 + 测试能力;视觉/编排项必须真实 Windows 窗口,测试绿不免除走查。

## Out of Scope

- **不做 0.21 新特性接入**（见 Implementation Decisions）。
- **不做 `NativeChromeWindow` 主题回退逻辑的简化重构**（#175 简化票,后续单独立票）。
- **不做 MewUI 本地参考 clone 的重建与管理**。

## Further Notes

- 版本号常量 `AppVersion` 随本票实施提升为 `v0.3.4`（单文件单行,见 AGENTS.md 约定）。
- ADR-000101-01 顶部加修订注记（锁版 0.20.2 → 0.21.1）,机制原样延续。
- 评审证据（2026-09-25）:v0.21.1 发布说明全文通读;破坏/废弃 API 与本仓库源码逐项比对（结论见 Problem Statement）。
