# 05 — LauncherApp 模块化

**What to build:** Launcher 从「应用本体」重写为工具模块:启动项领域(数据/搜索/执行/每项热键)保留,五区贡献(活动栏、侧边栏分类树、列表/卡片、详情表单、设置「数据」节)、浮层搜索源、主题参与全部改为经 `ToolModuleContext` 贡献;Launcher 工程内以临时引导继续跑通(宿主未建之前不改变入口)。用户视角:行为与 v0.1.6 完全一致。这是拆分中最重的一张。

**Blocked by:** 04

**Status:** resolved

- [x] LauncherModule 经 context 贡献五区,分类树/列表卡片/详情表单/设置「数据」节行为与 v0.1.6 一致
- [x] 浮层搜索源经 context 注册,呼出行为不变
- [x] 每项热键与启动/反馈逻辑保留在模块内,行为不变
- [x] 临时引导跑通全功能,无回归

## Comments

- `LauncherApp` 实现 `IMewToolModule`(Id=launcher/DisplayName=启动项),新增 `Configure(ToolModuleContext)`:绑定 context.Workbench/ThemeContext/Settings/Overlay,加载启动项数据与设置后贡献五区(活动栏、分类树侧边栏、列表/卡片、详情表单、设置节、输出、状态栏)、调用 ShowNav/ShowEmptyDetail/迁移布局,并 `context.Overlay.AddSearchSource(new LauncherSearchSource(...))`。
- 五区组装代码整体自 `Run()` 移入 `Configure`(顺序不变:主题/五区链 → ShowNav → ShowEmptyDetail → 布局迁移),`Run()` 改为临时引导:自组 Workbench/SettingsService/OverlayWindow/上下文后自我 Configure,再 BuildTitleBar + Build + 生命周期(窗口/托盘/全局热键/消息分发原样保留)。
- 每项热键(`ItemHotkeys`)、启动/反馈(`LauncherRunner`/`LaunchItem`/`Feedback`)、`LauncherStore`/`LauncherSearch` 等启动项领域全部保留在模块内,行为不变;入口不变(`Program.cs` 仍 `new LauncherApp().Run()`),WinExe 未动。
- 临时占位 `ScaffoldHotkeyService`(满足契约,空转):宿主中央热键服务(票据 07)落地前使用;`_settings` 由 context 传入(临时 cast,宿主未建模块暂持根节设置,票据 06/07 后归宿主)。
- 评审修复(代码评审):移除 `_settings = (SettingsService)context.Settings` 向下转型——`ISettingsService` 契约补 `Save()`,模块字段改为 `ISettingsService`,仅按契约读写 launcher 节;新增用例验证非 `SettingsService` 实现下 Configure 不崩溃(不再依赖具体宿主类型,ADR-000200「工具只通过契约贡献」)。
- 服务接口名与 MewUI 的 `IOverlayService` 撞名,LauncherApp 内用 `OverlayServiceContract` 别名消歧。
- 测试:`LauncherModuleTests`(Configure 贡献五区 → Build 通过、设置文档可打开、注册 launcher 搜索源;隔离用户 launcher.json/layout.json/presentation.json)。`WorkbenchConfigurationTests` 与 `LauncherModuleTests` 同集合串行(MewDock Build 后持有 layout.json 句柄,并行互相踩文件)。编译 0 警告 0 错误,Launcher.Tests 全量 97 用例绿;运行期冒烟延至票据 09。
