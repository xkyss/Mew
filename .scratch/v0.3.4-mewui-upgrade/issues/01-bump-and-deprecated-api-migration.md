# 01: 包升 0.21.1 + 废弃 API 迁移

**What to build:** 四个工程的两包引用直升 0.21.1 并保持精确锁版;按 0.21 废弃清单迁移 `LauncherModule` 的废弃 API——`ContextMenu.ShowAt` ×2 改 `Placement` + `Show(target)`;`ItemsSource` 连接按 0.21.1 编译器口径核实后处理(见 Comments:属性赋值即现行类型化路径,需清零的是废弃三件套);`TreeItemsView` 的选择 API(如 `SelectSingle`)保持不动;`AppVersion` 提升为 `v0.3.4`。编译零警告 + 全量测试绿即验收(语义核对与运行验收归 02)。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 四工程 `Aprillz.MewUI` / `Aprillz.MewUI.MewDock` 同步升 0.21.1,精确锁版
- [x] 分类树 `ItemsSource` 连接经 0.21.1 编译器核实:属性赋值为现行类型化路径(无 CS0618),保持不变
- [x] 类型过滤下拉 `ItemsSource` 同上,选项显示与选择行为等价(全量测试绿)
- [x] 两处 `ContextMenu.ShowAt`(树右键、元素右键)迁移到 `Placement = MenuPlacement.Pointer` + `Show(target)`
- [x] `AppVersion` = `v0.3.4`(提交 c04d2e8,先行完成)
- [x] 全量测试绿(Mew.Host.Tests 168 + Mew.Launcher.Tests 104,零改写)
- [x] 源码中废弃 API 清零(grep 无 `ShowAt` / `new ItemsSource` / `ItemsView.From` / 流式 `.ItemsSource(`;编译零警告)
- [x] 无预期外断点(0.21.1 编译零错误零警告),ADR-000304-01 无需补记断点

## Comments

- 2026-09-25(WSL 侧完成):两包 0.20.2 → 0.21.1,四工程同步;编译零错误零警告(仅有的 2 个 CS0618 来自 `ShowAt`,迁移后清零)。
- **`ItemsSource` 口径修正**(票面假设与 0.21.1 实际不符,按 spec「以编译器与 0.21 文档为准」执行):0.21 正式废弃的三件套 = `ItemsSource` 包装类型(reflection 证实 `[Obsolete]`)、`ItemsView.From(ItemsSource)`、ListBox/ItemsControl/ComboBox 的流式 `.ItemsSource(ItemsSource)` 扩展——本仓库三者均未使用;`ComboBox.ItemsSource`/`TreeView.ItemsSource` 属性赋值 `ItemsView<T>`/`TreeItemsView<T>` 实例是 0.21.1 现行类型化路径,且无替代重载(反射核实两类型无其他接受 ItemsView 的成员),故保持不变。
- `ShowAt` 迁移形态:两处右键菜单均改 `menu.Placement = MenuPlacement.Pointer; menu.Show(target);`,语义等价(指针位置锚定)。`Show(target, positionInWindow)` 备选未用。
- 全量:Mew.Host.Tests 168 + Mew.Launcher.Tests 104 全绿。运行时语义(菜单弹出位置、下拉行为)待 02 Windows 走查。
