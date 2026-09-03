# 06 — Mew.Host 建立与 exe 角色移交

**What to build:** 新宿主 exe 成为唯一启动入口:平台注册、组合根显式注册模块、独占 Build()、窗口/托盘/标题栏/菜单/关于/主题循环/设置文档(外观节)全部归宿主;Launcher 工程由 WinExe 转为 Library,发布配置(应用图标/NativeAOT/Direct2D)移入宿主。产物变为 `Mew.Host.exe` + `Mew.Launcher.dll`。用户视角:启动入口换为宿主,其余行为与 v0.1.6 一致。

**Blocked by:** 05

**Status:** resolved

- [x] Mew.Host.exe 启动即 Launcher 应用:五区、托盘、全局热键、浮层、设置文档(外观节)行为与 v0.1.6 一致(运行期冒烟延至票据 09)
- [x] Launcher 转 Library 后,既有领域测试仍全绿
- [ ] NativeAOT 发布(win-x64)仍可行,产物为 Mew.Host.exe;窗口标题/托盘提示/关于保留产品名「Mew Launcher」(发布校验在票据 09)
- [ ] 设置文档由宿主提供,外观(主题)节归宿主;Launcher 仅贡献「数据」节(06 暂留「热键」节,票据 07 移交宿主)

## Comments

- 新增 `src/Mew.Host`(WinExe、`MewUIBackend=Direct2D`、`PublishAot`、应用图标沿用 mew-launcher-icon.ico):`MewHost.Run()` 即组合根——平台/后端注册、建 `NativeChromeWindow`、组装 Workbench/OverlayWindow/SettingsService/SettingsSectionRegistry/ScaffoldHotkeyService(占位,票据 07 换成宿主中央热键服务)、`new LauncherModule().Configure(context)`、宿主贡献设置上下文(活动栏/侧边栏/文档 + 外观节 + 模块节)、`TitleBarBuilder.BuildTitleBar`、`workbench.Build()` 后进入消息循环。`Program.cs` 仅 `Win32Platform.Register(); Direct2DBackend.Register(); new MewHost().Run();`。
- 宿主职责自 LauncherApp 移交:窗口/托盘/标题栏与 View 菜单/关于对话框/主题循环与持久化(LoadThemeMode/PersistThemeMode/SyncThemeRadios/UpdateThemeButton)/设置文档(外观节)与设置侧边栏/浮层生命周期 + WM_HOTKEY 浮层热键分发/ApplyWindowIcon/AppVersion 常量(v0.2.0)/MigrateLegacySettingsDocumentLayout。
- `LauncherModule`(原 LauncherApp,`git mv`)转为 Library 中的 public 模块类:仅保留启动项领域与五区贡献;`_window` 来自 context.Window(模块弹窗/Toast/焦点仍需 MewUI `Window` 对象,无 FromHandle 可用),全局呼出热键经 `context.WindowHandle` 注册,设置上下文贡献「热键」「数据」两节。原 Program.cs 删除(`git rm`)。
- 框架侧(`Mew.Workbench`):`ToolModuleContext` 新增 `Window?` 与 `SettingsSectionRegistry`(设置节注册表,宿主 + 模块贡献设置节);`Workbench` 新增 `HotkeyMessage`/`WindowKeyDown` 事件与 `NotifyHotkeyMessage`/`NotifyWindowKeyDown` 转发(宿主在 WM_HOTKEY/PreviewKeyDown 中转发,模块订阅);`TitleBarBuilder`(标题栏 + View 菜单,语义动作由宿主注入)与 `ScaffoldHotkeyService` 从 Launcher 移入 Workbench 公开。
- 模块可见性:`LauncherModule` 及其无参构造改为 public(组合根在不同程序集,模块即模块 dll 的公开入口);`Mew.slnx` 加入 Mew.Host 工程。
- 测试:LauncherModuleTests 的 Configure 用例改以宿主姿态补设置上下文后 Build/OpenDocument,断言模块节 id = [hotkey, data]、浮层搜索源 id = launcher;WorkbenchConfigurationTests 改用 `TitleBarBuilder.BuildViewMenu` 直调。编译 0 警告 0 错误,Launcher.Tests 全量 97 用例绿。
- 未勾选项说明:NativeAOT 发布与运行期冒烟属票据 09;「Launcher 仅贡献数据节」为 v0.2.0 终态,06 中热键设置节暂留模块(冲突检测依赖 `_items`,票据 07 集中化时移交宿主)。
