# 03 — 详情页工具行、分组与动态标签

**What to build:** 详情页第一行改为「启动」「删除启动项」工具行；字段按「基本」（名称/命令/参数/工作目录/分类）与「高级」（图标/每项热键）分组；移除表单内名称大标题与 id 展示；详情文档标签动态显示当前项名（Workbench 提供改文档标题能力）。

**Blocked by:** None — can start immediately.

**Status:** resolved

- [x] 详情页第一行为「启动」「删除启动项」工具行，行为与现有一致。
- [x] 字段按「基本」「高级」两组分区显示，表单内不再显示名称大标题与 id。
- [x] 详情标签动态显示当前项名，切换项时标签跟随。
- [x] Workbench 改文档标题能力对外可用，不影响布局持久化与恢复。
- [x] 手工冒烟覆盖：编辑时测试启动、删除确认、标签跟随、「在侧边栏定位」不回归。

## Comments

- 实现提交 `7f6fed9` feat(launcher): 详情页工具行/分组与标签随项名动态。
- Workbench 新增 `SetDocumentTitle`（写 `DockPane.Title`，不触发布局持久化）；`WorkbenchConfigurationTests` 增 2 例（更新标题/未知文档拒绝），整体 45/45 通过。
- 顺带：`ShowEmptyDetail` 时标签回落「启动项详情」；`DetailDocumentId` 常量消除三处魔法字符串。
- 待手工冒烟：编辑/切换/删除项时标签跟随、工具行启动与删除、Ctrl+Alt+R 定位、重启恢复后标题。
