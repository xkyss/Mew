# 01: 包升 0.21.1 + 废弃 API 迁移

**What to build:** 四个工程的两包引用直升 0.21.1 并保持精确锁版;按 0.21 废弃清单迁移 `LauncherModule` 的 5 处废弃 API——`ItemsSource` 赋值 ×3（分类树、类型过滤下拉等）改 `ItemsView.Create(...)`/`ItemsView` 重载,`ContextMenu.ShowAt` ×2 改 `Placement` + `Show(target)`（具体形态以编译器与 0.21 文档为准）;`TreeItemsView` 的选择 API（如 `SelectSingle`）保持不动,验证视图重建路径下选择行为如旧;`AppVersion` 提升为 `v0.3.4`。编译零错误 + 全量测试绿即验收（语义核对与运行验收归 02）。若出现预期外编译断点,逐条补记 ADR-000304-01 的 Consequences。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 四工程 `Aprillz.MewUI` / `Aprillz.MewUI.MewDock` 同步升 0.21.1,精确锁版
- [ ] 分类树 `ItemsSource` 赋值迁移到新 `ItemsView` 路径,刷新/过滤/默认选中「全部」行为等价
- [ ] 类型过滤下拉 `ItemsSource` 迁移,选项显示与选择行为等价
- [ ] 两处 `ContextMenu.ShowAt`（树右键、元素右键）迁移到 `Placement` + `Show(target)`,菜单项不变
- [ ] `AppVersion` = `v0.3.4`（单行）
- [ ] 全量测试绿（现有 Mew.Host.Tests 168 + Mew.Launcher.Tests 104;涉菜单/树用例如因构造方式调整,只动构造不动断言语义）
- [ ] 源码中废弃 API 清零（grep 无 `ItemsSource =` 赋值、无 `ShowAt`）
- [ ] 预期外断点（若有）已补记 ADR-000304-01

## Comments

-
