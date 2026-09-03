# 08 — 宿主测试缝

**What to build:** 新增单一测试缝覆盖架构特性:真实 Launcher 模块 + 测试内假第二模块的宿主组装与 Build 校验(活动栏↔侧边栏配对、ID 全局唯一)、设置分节与旧结构迁移、浮层聚合(混排顺序/每源上限/全局上限/来源标记)、热键跨模块冲突判定。开发者:架构特性拥有自动化回归网,不依赖手工冒烟。

**Blocked by:** 06

**Status:** resolved

- [x] 组装用例:Launcher 模块 + 假模块注册后 Build() 成功、五区包含各模块贡献;配对违规与 ID 冲突被 Build 期校验拒绝
- [x] 设置用例:根节 + 模块节分节存取;旧扁平结构迁移结果正确(既有 SettingsServiceTests 覆盖,未触碰)
- [x] 浮层聚合用例:多源扁平混排顺序、每源上限、全局上限、来源标记(既有 OverlaySearchAggregatorTests 覆盖,未触碰)
- [x] 热键用例:注册/注销与跨模块冲突判定
- [x] 既有 Launcher 领域测试不被触碰(引用对象变化除外)

## Comments

- 新增 `tests/Mew.Host.Tests`(xunit,引用 Mew.Launcher,经传递引用获得 Mew.Workbench;不直接引用 Mew.Host——`MewHost.Run()` 需真实窗口/消息循环,本缝复刻其组装路径,USER STORY 15/16)。`Mew.slnx` 的 /tests/ 加入该工程。
- `HostCompositionTests`(组装用例,`[Collection("HostComposition")]` 集合内串行 + 临时移走 launcher.json/layout.json/presentation.json,同 LauncherModuleTests 惯例):真实 LauncherModule + 测试内假模块 FakeModule(独立活动栏/侧边栏/文档/面板/状态栏/设置节/浮层搜索源)经 ToolModuleContext 贡献,宿主补设置上下文后 Build 通过——断言侧边栏按活动上下文切换各出现、底部面板两模块视图齐备、文档运行时可打开、假模块状态栏项可见、设置节 = [data, todo-options]、浮层源 = [launcher, todo];另两条验证 Build 期拒绝:活动栏配对违规(无对应侧边栏)与跨模块停靠 ID 冲突(假模块复用 Launcher 的「items」文档 id)。
- `HotkeyServiceTests`(热键用例):注册/注销/IsRegistered、跨模块同组合冲突(修饰键顺序不同的文本写法也判冲突)、WM_HOTKEY 分发按 id 路由(未知 id 与注销后不触发)。以 `IntPtr.Zero` 句柄注册 = 线程级热键,不产生窗口消息;各用例使用不同组合键,避免并行执行时线程级热键系统级互抢(修复过一次:全量跑时两个用例共用 Ctrl+Alt+F9 偶发失败)。
- 设置分节/迁移与浮层聚合用例按验收清单第 5 项留在既有缝:`SettingsServiceTests`(根节 + 模块节往返、旧扁平迁移)与 `OverlaySearchAggregatorTests`(混排顺序/每源上限/全局上限/来源标记)已完整覆盖,本次未触碰。
- 测试:新增 8 用例全绿;Launcher.Tests 全量 97 用例不变全绿。编译 0 警告 0 错误。
