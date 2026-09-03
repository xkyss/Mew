# 01 — 外壳服务迁入 Workbench

**What to build:** 九个通用外壳类——全局热键、热键解析、托盘、设置存储、选择模型、悬浮引用计数、空态视图、图标解析、防抖——从 Launcher 工程机械迁入 Workbench 主框架,Launcher 改为消费主框架提供的能力;被迁类的既有测试用例继续在既有测试缝运行(经 InternalsVisibleTo 调整归置)。对用户不可见:纯架构性搬迁。

**Blocked by:** 无

**Status:** resolved

- [x] 仓库编译通过(Workbench + Launcher + 既有测试工程)
- [x] 既有测试全绿:被迁类的用例(选择模型/悬浮引用计数/空态/防抖/设置存储)经测试归置调整后仍运行
- [ ] Launcher 启动、托盘、全局热键、浮层、设置读写行为与 v0.1.6 一致(手工冒烟,延至票据 09 全量冒烟)
- [x] 迁移为机械搬动:不改变类行为、不改公共语义(命名空间/可见性随工程调整除外)

## Comments

- 9 个外壳服务类迁入 `Mew.Workbench` 并转 public:GlobalHotkey、HotkeyParser、TrayIcon、SettingsStore(+AppSettings/AppSettingsJsonContext)、SelectionModel、HoverRefCount、EmptyState(+EmptyStateKind)、IconResolver、LaunchDebouncer。
- `IconResolver` 泛化:签名由 `Resolve(LauncherItem)` 改为 `Resolve(iconPath, command, isUrlCommand)`,去掉对启动项领域的依赖(避免 Workbench → Launcher 循环引用);URL 判定由调用方传入,Launcher 侧新增 `IconResolverExtensions.Resolve(LauncherItem)` 扩展方法,单一事实来源仍在 `LauncherData.KindOf`。
- `Mew.Workbench.csproj` 补 `System.Drawing.Common` 引用(TrayIcon/IconResolver 依赖)。
- 被迁类测试的 using 由 `Mew.Launcher` 调整为 `Mew.Workbench`,用例内容不变。
- 编译 0 警告 0 错误;相关 28 个用例(SelectionModel/HoverRefCount/LaunchDebouncer/SettingsStore/EmptyState)全绿。
