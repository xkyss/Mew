# MewUI 0.20.2 → 0.21.1:废弃 API 迁移 + 走查验收

> **一句话定调:0.21.1 破坏面经逐项核实为极小（2 处破坏均不波及源码），实际工作是 5 处废弃 API 迁移（单文件 LauncherModule）+ 回归验收；评审方式较 0.20.2 那轮简化——不做独立探测编译，直接升包。**

ADR-000101-01 锁定 MewUI 0.20.2 的锁版机制不变，本轮为下一次显式升级评审。评审依据 = v0.21.1 发布说明全文（0.21.0/0.21.1 合并描述，2026-09-07 发布）+ 本仓库源码逐项比对。证据：①破坏性变更仅 2 处——`ToolTipProperty`/`ContextMenuProperty` 声明从 `Control` 移至 `FrameworkElement`（源码兼容，本仓库未直接引用字段）、`TreeView.ItemTemplate` 返回值语义变化（本仓库未读取）；②本仓库在用的废弃 API 共 5 处、全部集中在 `LauncherModule.cs`——`ItemsSource` ×3（分类树 `:307`、类型过滤下拉 `:754`，已核实前者每次刷新整体重建视图重新赋值、后者为静态列表，行为暂不受影响）、`ContextMenu.ShowAt` ×2（`:349`、`:691`）；③机会收益显著：#175（`Application.Run` 前建窗主题错误，正中 `NativeChromeWindow.CurrentTheme()` 手写回退场景）、#243/#236 弹层焦点与误关、X11 剪贴板/按键、Linux DPI、渲染资源内存池（常驻托盘进程直接受益）。

## Considered Options

- **继续锁 0.20.2**:破坏面虽小，但 0.21 修复直接命中本仓库已知痛点（首启主题、弹层焦点、X11），且废弃 API 的行为差异会随下次升级累积。→ 未选。
- **直升 0.21.1（选定）**:跳过 0.21.0 中间版，延续 ADR-000300-01 先例。
- **顺带采用 0.21 新特性**（命令参数、`RenderResourceMetrics`、行容器钩子等）:扩大验收面，违背"升级票小、可验收"原则。→ 未选，留后续票。

## 决策

1. **目标 0.21.1**（`Aprillz.MewUI` 与 `Aprillz.MewUI.MewDock` 两包同步），升后继续精确锁版；ADR-000101-01 锁版机制不变，仅修订版本号。
2. **废弃 API 全部迁移**（`ItemsSource` → `ItemsView.Create(...)`/`ItemsView` 重载；`ShowAt` → `Placement` + `Show(target)`），保持升级排查面最小；具体形态以编译器与 0.21 文档为准。
3. **评审流程简化**:破坏面已逐项核实为极小，不再做独立探测性编译——直接升包编译，断点（若有）记录于本 ADR 的 Consequences。
4. **不采用新特性**;#175 只在走查中验证首启/切主题行为，`NativeChromeWindow` 回退逻辑简化留后续票。
5. **本地参考 clone 出清**:先例要求的"参考 clone 同步 checkout"已无法执行（clone 不存在），本次起评审依据 = NuGet 包元数据 + 发布说明，clone 按需再建，不作为流程环节。
6. **验收硬门槛 = Windows 侧 AOT 发布 + 手工走查清单**（在现有走查清单上追加 0.21 专项:树右键菜单 Placement、类型过滤下拉、切主题/首启主题、弹层焦点、滚动误关、托盘/热键回归）。WSL 测试绿不免除走查。

## Consequences

- ADR-000101-01 修订:锁定版本 0.20.2 → 0.21.1;「升级是显式决策、须评审发布说明」的要求原样延续。
- `LauncherModule.cs` 废弃 API 清零后，下次升级的排查面继续收敛在 MewDockShell + 本轮记录的基线。
- 若编译断点出现（预期外），逐条补记于此:（无——0.21.1 编译零错误零警告;实施时口径修正:0.21 正式废弃的是 `ItemsSource` 包装类型/`ItemsView.From`/流式 `.ItemsSource(ItemsSource)` 扩展三件套,本仓库均未使用,`ItemsSource` 属性赋值为现行类型化路径无需迁移,实际断点仅 `ContextMenu.ShowAt` ×2 的 CS0618,已按 `Placement = MenuPlacement.Pointer` + `Show(target)` 迁移）。
- 本仓库版本迭代为 v0.3.4（`.scratch/v0.3.4-mewui-upgrade/spec.md`），`AppVersion` 随实施提升。

## Alternatives Rejected

- 见 Considered Options；继续锁版因修复收益与排查面累积被放弃，特性接入因验收面扩大被推迟。
