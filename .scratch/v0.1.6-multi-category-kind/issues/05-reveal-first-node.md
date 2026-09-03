# 05 — 在侧边栏定位首个节点

**What to build:** 「在侧边栏定位当前启动项」在多归属下的语义:按树序取该启动项第一个所属分类节点并切到该节点、选中它;无归属的启动项定位到「未分类」。行为确定、可重复。

**Blocked by:** 01 — 多分类数据模型与迁移

**Status:** resolved

- [x] 对挂多个分类的启动项触发「在侧边栏定位」,侧边栏切到树序第一个所属分类并选中该节点
- [x] 同一启动项重复触发定位,结果一致(可预测)
- [x] 无归属的启动项定位到「未分类」

**Comments**

- 实现:新增 `LauncherData.FirstCategoryIdInTreeOrder`(纯函数,深度优先先序取首个所属分类 id,无有效归属返回 null);`EditItem` 的定位回调改为 `RevealInSidebar`——目标导航 = 树序首个归属 ?? 「未分类」,`ShowNav` 切换列表并 `SelectNavNode` 在树中选中该节点。
- 与 ticket 01 旧行为(直接取 `CategoryIds[0]`)的区别:按树序而非数组序,多归属下定位结果与侧边栏展示顺序一致。
- code-review 修正:定位前先 `ClearCategorySearch()` 清空侧边栏搜索过滤,保证目标节点不被过滤隐藏、定位后必然可见并选中(满足验收「切到并选中该节点」);重复触发结果一致。
- 测试:LauncherDataTests 新增 2 例(数组序 vs 树序取首个 / 无有效归属返回 null),31 例通过。
