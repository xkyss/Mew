# 04 — 键盘导航与选中模型

**What to build:** 启动项列表获得焦点后 ↑/↓ 移动选中（环绕）、Enter 启动选中项、「/」或 Ctrl+F 聚焦列表搜索框；选中模型卡片/列表共用，与呼出浮层的循环选择同构。

**Blocked by:** 01 — 列表单击启动与双击防重.

**Status:** resolved

- [x] ↑/↓ 移动选中并环绕，Enter 启动选中项。
- [x] 「/」或 Ctrl+F 聚焦列表搜索框。
- [x] 查询刷新后选中收敛到可见结果内不越界。
- [x] 选中移动/环绕/收敛抽为纯逻辑并在现有测试项目补用例（空列表、单元素、边界环绕、刷新收敛）。

## Comments

- 实现提交 `235b596` feat(launcher): 列表键盘导航与选中模型(↑/↓+Enter、/ 或 Ctrl+F 聚焦搜索)。
- 选中模型抽为 `SelectionModel`，与呼出浮层共用（OverlayWindow 内联循环逻辑已替换）；`SelectionModelTests` 8 例 + `ActiveDocumentId` 1 例全过，整体 54/54 通过。
- Workbench 新增 `ActiveDocumentId` 作为按键路由门控；键盘导航限定在启动项列表文档激活且焦点不在文本输入时生效。
- 框架约束/待冒烟：MewUI `Key` 枚举无标点键，「/」经平台虚拟键码 VK_OEM_2(0xBF) 识别（依赖 US 布局）；滚动跟随依赖布局时序；主题切换后选中高亮重涂顺序未实证。
