# 01: 包升 0.20.2 + 菜单/命令 API 迁移

**What to build:** 四个工程的两包引用直升 0.20.2 并保持精确锁版;按 0.20 新 Command API 迁移探测出的 4 处编译断点——标题栏菜单栏（`TitleBarBuilder` 的 `Menu.Item`/`MenuItem.Click`）与 tab 右键菜单（`WorkbenchView` 的 `ContextMenu.Item`「在侧边栏定位」）;`GroupMenuOpening`/`TabMenuOpening` 挂接点核对后保留;`AppVersion` 提升为 `v0.3.0`。编译零错误 + 全量测试绿即验收（语义核对归 02,运行验收归 03）。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 四工程 `Aprillz.MewUI` / `Aprillz.MewUI.MewDock` 同步升 0.20.2,精确锁版
- [ ] `TitleBarBuilder` 菜单栏按新 Command API 迁移,菜单项/访问键行为等价
- [ ] `WorkbenchView` tab 菜单「在侧边栏定位」按新 API 迁移
- [ ] `GroupMenuOpening` 挂接点（→ `MewDockShell.PruneGroupMenu`）在新版接线成功
- [ ] `MewDockShellTests` 真实 `ContextMenu` 用例按新菜单 API 调整并通过
- [ ] `AppVersion` = `v0.3.0`（单行）
- [ ] 全量测试绿（现有 137 + 103,允许按新 API 调整的用例等价改写）
- [ ] 本地参考 clone checkout v0.20.2(如可用)

## Comments

