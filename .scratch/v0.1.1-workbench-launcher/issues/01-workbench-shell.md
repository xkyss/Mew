# 01 — Workbench 五区外壳与 Fluent API

**What to build:** Launcher 启动后呈现完整的 VSCode 式工作台外壳——活动栏、侧边栏、编辑器区、底部面板、状态栏五个区域全部可见、可停靠,且布局由 Workbench 的类型安全 Fluent API 在编译期声明,不写 XAML、不读布局配置文件。

**Blocked by:** None — can start immediately

**Status:** ready-for-agent

- [ ] Launcher 启动后五区(活动栏、侧边栏、编辑器区、底部面板、状态栏)全部可见
- [ ] 五区布局通过 Workbench 的类型安全 Fluent API 声明,编译期组合(无 DI、无插件加载)
- [ ] 区域命名与 `CONTEXT.md` 术语一致
