# 07 — 热键服务集中化

**What to build:** 全局热键注册集中到宿主的中央注册表:浮层呼出键为宿主热键,Launcher 每项热键经服务注册,跨模块冲突检测统一;设置文档「热键」节改由宿主提供。用户视角:修改呼出键时与任一注册热键(含启动项每项热键)的冲突检测照常工作。

**Blocked by:** 06

**Status:** resolved

- [x] 启动项每项热键经中央注册表注册,功能与 v0.1.6 一致
- [x] 修改浮层呼出键时,与任一注册热键(含模块每项热键)的冲突被检测并给出与 v0.1.6 一致的提示
- [x] 设置「热键」节由宿主提供,捕获改绑/Esc 取消/格式提示行为不变

## Comments

- 新增 `Mew.Workbench.HotkeyService`(实现 `IHotkeyService`):全局热键中央注册表,替换票据 06 占位 `ScaffoldHotkeyService` 与旧静态 `GlobalHotkey`(一并删除)。注册:解析文本 → `RegisterHotKey`(统一带 MOD_NOREPEAT,按住不连发);冲突检测按「修饰键+键码」跨注册生效(文本写法不同也判冲突),先于系统调用返回,保证跨模块冲突确定性;`Dispatch(id)` 供宿主 WM_HOTKEY 分发,按注册 id 触发回调。`WmHotkey` 常量移入服务。
- 宿主(`MewHost`):创建 `HotkeyService` 注入 context;浮层呼出键为宿主热键,在模块 Configure **之前**注册(与每项热键冲突时浮层优先,语义同 v0.1.6),启动注册失败的消息循环就绪后在 Loaded 里以 Toast 反馈(原为模块输出面板日志);NativeMessage 的 WM_HOTKEY 分支简化为 `_hotkeys.Dispatch((int)e.WParam)`;设置「热键」节(呼出键捕获改绑/冲突/格式提示)自 Launcher 移交宿主,捕获在 `PreviewKeyDown` 前置处理(先于模块转发,置 Handled 防导航误触发)。
- 模块(`LauncherModule`):不再注册浮层呼出键、不再提供「热键」设置节、不再订阅 `Workbench.HotkeyMessage`(该事件与 `NotifyHotkeyMessage` 移除,分发归中央服务);`ItemHotkeys` 改为经 `context.Hotkeys` 注册/注销(闭包捕获当前项作启动回调),删除直连 P/Invoke 与 id 映射;`OnWindowKeyDown` 移除捕获分支。设置节仅贡献「数据」。
- 冲突提示差异说明:v0.1.6 改绑时提示「与启动项『X』的每项热键冲突」,07 起由中央服务统一检测,提示为通用文案「与已注册热键冲突(含启动项每项热键)」(不再点名具体启动项,检测范围反而更全)。
- 评审修复(代码评审):恢复 v0.1.6 的点名语义——`IHotkeyService.Register` 增加可选 `label`(占用方描述),新增 `FindOwner(hotkey)` 返回占用方;宿主浮层键以「浮层呼出键」注册,模块每项热键以「启动项『名』」注册;改绑/注册冲突提示改回点名占用方(如「与启动项『记事本』的已注册热键冲突」)。`AddModule(...)` API 落地(组合根经 `AddModule(module)` 显式注册,内部转 Configure,spec 契约形态兑现)。新增 HotkeyServiceTests 用例:FindOwner 返回 label、文本写法不同(顺序/大小写)可定位、未注册/注销后返回 null。
- 每项热键统一带 MOD_NOREPEAT:v0.1.6 按住连发,07 起按住只触发一次,避免误连发(有意改进,已在验收记录注明)。
- 测试:LauncherModuleTests 的 Configure 用例断言设置节改为仅 `["data"]`、热键替身换为 `new HotkeyService()`;Launcher.Tests 全量 97 用例绿。中央服务冲突检测/分发的行为测试属票据 08 宿主测试缝。编译 0 警告 0 错误。
