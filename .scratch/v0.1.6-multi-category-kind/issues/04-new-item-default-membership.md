# 04 — 新建启动项默认归属

**What to build:** 新建启动项的默认归属决策改为数组语义:在分类节点下新建 → 新项初始归属含该分类;在「全部」/「未分类」下新建 → 新项初始为空归属(未分类)。新项在详情表单中以默认归属初始化,保存后正确落盘。

**Blocked by:** 03 — 详情表单分类平铺多选

**Status:** resolved

- [x] 在某个分类节点下新建启动项,新项默认归属该分类,表单中可见且可调整
- [x] 在「全部」或「未分类」下新建启动项,新项默认未分类
- [x] 新建保存后,归属以数组形式正确落盘,并在对应分类下可见

**Comments**

- 实现依托前序 ticket:默认归属由 ticket 01 的 `LauncherData.CategoryIdsForNewItem(_navId)`(分类节点 → `[id]`,固定节点 → `[]`)+ `CreateItem` 注入;表单默认勾选由 ticket 03 的平铺多选以 `item.CategoryIds` 初始化;落盘与可见性由 ticket 01 的 `ToDto`(categoryIds 数组)/ `AggregateSubtree`(任一归属命中)保证。本次为纯验证,无源码改动。
- 测试:LauncherStoreTests 新增 `Save_新建项默认归属_落盘并在分类下可见`(保存→读回默认归属、文件含 `categoryIds` 非 `categoryId`、新项在对应分类聚合中可见),连同 LauncherDataTests 共 36 例通过。
