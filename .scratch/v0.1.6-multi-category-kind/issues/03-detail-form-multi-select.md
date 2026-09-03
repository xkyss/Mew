# 03 — 详情表单分类平铺多选

**What to build:** 详情表单的分类字段从树形单选改为平铺多选:所有分类以 checkbox 平铺展示(「未分类」置顶、带「父/子」路径前缀),可勾选多个分类;「未分类」不再是可勾选项,而是「全不勾选」的隐式状态;勾选父分类不自动勾选子分类。保存后启动项在这些分类下都可见。

**Blocked by:** 01 — 多分类数据模型与迁移

**Status:** resolved

- [x] 表单分类字段以 checkbox 平铺展示全部分类,「未分类」置顶、子分类带「父/子」路径前缀
- [x] 可同时勾选多个分类并保存;保存后启动项在每个勾选分类下都可见
- [x] 全不勾选 = 未分类(不存在可勾选的「未分类」选项)
- [x] 勾选父分类不自动勾选子分类,归属保持显式
- [x] 打开已有多归属启动项时,勾选状态与已存归属一致

**Comments**

- 实现:删除 `BuildDetailForm` 中的单选 ComboBox(含两处重复的 SelectionChanged 处理),新增 `BuildCategoryChecks`:未分类置顶为不可勾选的提示行,分类按 FlattenCategoryOptions 的「父 / 子」路径逐项渲染 checkbox;每个 checkbox 独立开关,只增删自身 id,不联动父子;勾选集合来自 `item.CategoryIds`。
- 数据语义(ticket 01 已覆盖):多归属可见性由 `AggregateSubtree` 的 `Overlaps` 保证,全不勾选归未分类由 `Uncategorized` 保证,路径前缀由 `FlattenCategoryOptions` 保证。
- 测试:UI 无单测缝,跑 LauncherDataTests 29 例确认数据契约无回归。
