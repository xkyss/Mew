# 02 — 设置服务分节与旧结构迁移

**What to build:** 设置持久化升级为「根节 + 模块节」结构:宿主级设置(主题模式、浮层呼出键)在根节,工具模块设置按模块 Id 分节(如 launcher 的列表形态);旧扁平 settings.json 首次启动自动迁移到新结构,原值不丢。用户无感知,但升级后设置文件形态变化且迁移无损。

**Blocked by:** 01

**Status:** resolved

- [x] 设置服务支持根节 + 模块节分节读写
- [x] 旧扁平结构 settings.json 首次启动自动迁移为分节结构,主题模式/浮层呼出键/列表形态原值保留
- [x] Launcher 的列表形态经设置服务读写其模块节,行为与 v0.1.6 一致
- [x] 迁移失败(文件损坏/无权限)时静默回退默认值,不阻塞启动

## Comments

- `SettingsStore`(+`AppSettings`)删除,由 `Mew.Workbench.SettingsService` 取代:根节(主题模式/浮层呼出键)状态化读写,模块节经 `ReadSection<T>/WriteSection<T>` 走调用方源生成类型(JSON DOM 无反射,AOT/Trim 兼容);`Load()`/`Save()` 沿用静默容错(IO/权限/JSON 损坏回退默认)。
- 旧扁平结构迁移:`Load()` 后若根节存在 `itemsViewMode`,移入 `launcher` 模块节并回写;主题模式/浮层呼出键键名不变,原值无损。
- Launcher 侧:新增 `LauncherSettings`(+`LauncherSettingsJsonContext`,camelCase 命名策略与旧文件一致),`LauncherApp` 由一次性 `Load()` 改状态化 `_settings.Load()` 后读写 `_settings.ThemeMode`/`_settings.OverlayHotkey`/`launcher` 节,`ToggleViewMode` 经 `WriteSection` 落盘。
- 测试:`SettingsStoreTests` 删除,新增 `SettingsServiceTests` 5 用例(根节与模块节往返、无文件缺省、旧扁平迁移、手写 JSON 未知字段忽略、损坏 JSON 回退)。调试中发现测试用源生成上下文缺 camelCase 命名策略导致迁移值读空,补 `JsonKnownNamingPolicy.CamelCase` 后 5/5 全绿。
- 评审修复(代码评审):「迁移失败静默回退」补全类型不符路径——`GetString` 捕获根值类型不符(如 `"overlayHotkey": 123`)的 `InvalidOperationException` 静默返回 null,`ReadSection` 捕获模块节字段类型不符的 `JsonException` 静默返回 null;新增 2 用例(根值类型不符、模块节类型不符均不抛且回退)。`SettingsServiceTests` 7/7 全绿。
- 编译 0 警告 0 错误;相关用例全绿。
