# 04 — 模块契约定义

**What to build:** 主框架定义工具模块契约:`IMewToolModule`(Id/DisplayName/Configure)与 `ToolModuleContext`(工作台、窗口句柄、热键/设置/浮层/主题四个服务接口);「宿主独占 Build()」的边界随契约固定。纯增量:类型定义落地,尚无消费者,应用行为不变。

**Blocked by:** 02,03

**Status:** resolved

- [x] 契约类型定义完整:模块接口、上下文、四个服务接口与既有服务实现(设置分节、搜索源注册、主题上下文)衔接
- [x] 仓库编译通过,应用行为不变(纯增量)
- [x] 契约不含托盘菜单贡献与自定义浮层行渲染(按规格暂缓项)

## Comments

- 契约落地(`Mew.Workbench`):`IMewToolModule { Id, DisplayName, Configure(ToolModuleContext) }`;`ToolModuleContext` 表面 = 裸 Workbench + 窗口句柄 + 四个服务接口,文档注明 Build() 归宿主独占(ADR-000101-03/ADR-000200 边界)。
- 四个服务接口(`ModuleServices.cs`):`IHotkeyService`(注册/注销/冲突检测,实现接线在票据 07)、`ISettingsService`(模块分节)、`IOverlayService`(AddSearchSource)、`IThemeService`(模式读写 + ThemeModeChanged)。
- 与既有实现衔接:`SettingsService : ISettingsService`、`OverlayWindow : IOverlayService`(AddSource 更名 AddSearchSource,LauncherApp 同步)、`WorkbenchThemeContext : IThemeService`(新增 ThemeModeChanged 事件,SetMode 同模式幂等不触发;显式接口实现保留 Fluent 链)。
- 测试:`ThemeServiceTests` 3 用例(变更触发事件且 Mode 反映、同模式不触发、模式往返);编译 0 警告 0 错误,Launcher.Tests 全量 96 用例绿。
