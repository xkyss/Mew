# 01: 包升 0.20.2 + 菜单/命令 API 迁移

**What to build:** 四个工程的两包引用直升 0.20.2 并保持精确锁版;按 0.20 新 Command API 迁移探测出的 4 处编译断点——标题栏菜单栏（`TitleBarBuilder` 的 `Menu.Item`/`MenuItem.Click`）与 tab 右键菜单（`WorkbenchView` 的 `ContextMenu.Item`「在侧边栏定位」）;`GroupMenuOpening`/`TabMenuOpening` 挂接点核对后保留;`AppVersion` 提升为 `v0.3.0`。编译零错误 + 全量测试绿即验收（语义核对归 02,运行验收归 03）。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 四工程 `Aprillz.MewUI` / `Aprillz.MewUI.MewDock` 同步升 0.20.2,精确锁版
- [x] `TitleBarBuilder` 菜单栏按新 Command API 迁移,菜单项/访问键行为等价
- [x] `WorkbenchView` tab 菜单「在侧边栏定位」按新 API 迁移
- [x] `GroupMenuOpening` 挂接点（→ `MewDockShell.PruneGroupMenu`）在新版接线成功
- [x] `MewDockShellTests` 真实 `ContextMenu` 用例按新菜单 API 调整并通过
- [x] `AppVersion` = `v0.3.0`（单行）
- [x] 全量测试绿（现有 137 + 103,允许按新 API 调整的用例等价改写）
- [ ] 本地参考 clone checkout v0.20.2(如可用)——本机无 clone,留人工确认

## Comments

- 已完成。两包 0.20.2;`AppVersion` = v0.3.0。
- 菜单/命令迁移点:`TitleBarBuilder`(MenuBar 命令注册进 `menuBar.Commands`,View 切换项 = `MenuItem(command)` + `Text` 随显隐反转)、`WorkbenchView`(TabMenuOpening 命令注册进事件自带 `args.Commands`)、`LauncherModule` 两处右键菜单(注册进 `ContextMenu.Commands`,替代 `ContextMenu.Item(text, Action)`)。`Application.Quit` → `Application.Shutdown()`(2 处)。
- 新增共享助手 `MewCommands.Register(scope, id, text, execute)`(三处命令注册样板)。
- **0.20 运行时发现**:视觉树构建带全局静态状态(`BumpContextVersionDeep`/`VisualTree.Visit` 共享集合),无头**并行**建树不再线程安全(单跑通过、并行 NRE/OOR)——两个测试工程加 `xunit.runner.json` 关闭集合并行;此为测试基建约束,产品代码不受影响。
- 访问键(`_File` 下划线)与 View 切换项文本反转的**显示**行为无头不可验证,已进 03 走查清单。
- 全量:Host.Tests 137 + Launcher.Tests 103 全绿。
- 真机首验补遗:NativeChromeWindow 的 StyleSheet 冻结崩溃与 mxd 半装载崩 Build 已修(见 03 Comments);`MewCommands.Register` 已覆盖全部命令注册点(含 ViewToggleItem 与 TabMenuOpening)。
