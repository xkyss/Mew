# v0.2.0 宿主与工具模块架构规格

Status: ready-for-agent

> 版本:v0.2.0
> 对应 ADR:`docs/adr/000200-01-host-tool-modules.md`

## Problem Statement

Launcher 是 Workbench 的唯一消费者,`LauncherApp.cs`(1954 行)一人身兼四职——应用引导、五区组装、Launcher 专属 UI、领域操作——托盘/全局热键/设置/浮层等外壳服务混在工具代码里。想写第二个自用工具时,要么复制要么重写这些外壳服务,产生「主框架分叉」(主体相同又略有差别的多份框架)。同时多工具的愿景(每个工具一个工程、可替换、单独演进)在现行结构下没有落点:工具与框架没有清晰的贡献契约,外壳能力无法复用。

## Solution

v0.2.0 起改为**宿主 + 编译期工具模块**架构:

- 新建薄宿主 `Mew.Host`(exe):平台注册、窗口与工作台组装、模块显式注册、`Build()` 独占、托盘/全局热键/浮层生命周期、设置文档(外观/热键节)、标题栏/菜单/关于。
- `Mew.Workbench` 成为「主框架」:五区框架 + 吸收共享外壳服务(全局热键、热键解析、托盘、设置服务、选择模型、悬浮引用计数、空态、图标解析、防抖、快捷搜索浮层、主题循环与持久化)。
- `Mew.Launcher` 由 WinExe 变为**工具模块**(Library):保留启动项领域与专属视图,通过契约贡献到宿主。
- 契约:`IMewToolModule { Id, DisplayName, Configure(ToolModuleContext) }`,宿主组合根显式 `AddModule(...)`,全部贡献完后统一 `Build()`。
- 浮层机制归框架:工具模块贡献「搜索源」;跨源结果扁平混排 + 来源标记;唯一搜索源时行为与今日一致。

## User Stories

1. As 框架主人, I want 主框架是单一工程、所有工具共享同一份源码, so that 不存在「每个工具各带一份框架」的物理分叉。
2. As 工具作者, I want 新工具是独立工程、只实现一个 `IMewToolModule` 契约, so that 我不需要重写主框架。
3. As 工具作者, I want 在宿主组合根加一行 `AddModule(...)` 即可接入工具, so that 接入成本最低且一目了然。
4. As Launcher 用户, I want 重构后 Launcher 的一切行为与 v0.1.6 一致, so that 架构变更零 UX 回归。
5. As 用户, I want 托盘/全局热键/设置/浮层由宿主统一提供, so that 每个工具不重复实现外壳能力。
6. As 用户, I want 一个全局热键呼出浮层并跨工具搜索(带来源标记), so that 任意工具的内容一次命中。
7. As 工具作者, I want 通过 `ctx.Overlay.AddSearchSource(...)` 注册搜索源, so that 我的工具出现在快捷浮层中。
8. As 工具作者, I want 设置按模块 Id 分节存储, so that 多工具设置互不踩键。
9. As 用户, I want 旧版扁平 settings.json 自动迁移到「根节 + 模块节」, so that 升级不丢设置。
10. As 工具作者, I want 通过 `ctx.Hotkeys` 注册全局热键, so that 宿主集中做跨模块冲突检测。
11. As Launcher 用户, I want 每项启动项热键照常工作, so that 高频启动项仍一键直达。
12. As 工具作者, I want 设置文档由宿主统一提供、工具以「设置节」贡献内容, so that 多工具不会在活动栏里各开一个设置页。
13. As 用户, I want 外观(主题)与热键(浮层呼出键)设置归宿主, so that 这些设置对所有工具生效。
14. As Launcher 用户, I want 「数据」设置节(launcher.json 路径)保留, so that 我仍能定位数据文件。
15. As 开发者, I want 宿主的「模块注册 + Build()」与「Application.Run(窗口)」分离, so that 组装路径无需窗口即可测试。
16. As 开发者, I want 宿主组装与外壳服务有一个测试缝(单一新测试项目), so that 架构特性被黑盒覆盖而不散落各处。
17. As 开发者, I want Launcher 领域测试保持原位、只改引用对象, so that 重构不churn既有覆盖。
18. As 开发者, I want 宿主仍以 NativeAOT 发布, so that 「热键一按、浮层秒开」的启动体验不回归(ADR-000101-04 保持有效)。
19. As 开发者, I want 输出命名清晰(`Mew.Host.exe` 宿主 / `Mew.Launcher.dll` 模块), so that 产物不混淆。
20. As 工具作者, I want 工具模块照常直接使用 MewUI 控件构建视图, so that 拼 UI 不受框架约束(「不直连 MewUI」只约束框架机制性 API)。
21. As 工具作者, I want 我的工具工程单独演进(独立提交、独立测试), so that 工具变更不扰动主框架稳定性。
22. As 框架主人, I want 主框架只吸收 ≥2 个工具都需要的功能, so that 框架不膨胀成各工具需求的并集。
23. As 用户, I want 所有工具运行在单一进程、单一托盘、一套全局热键体系下, so that 工具间体验统一。
24. As 开发者, I want 运行时插件加载保持「机制后置」状态(发现器留作可替换实现), so that 需要时再切、不提前放弃 AOT。
25. As 开发者, I want 术语与文档同步(CONTEXT.md「工具模块」、ADR-000200), so that 后续读者理解架构选择。

## Implementation Decisions

- **工程结构**:`Mew.Workbench`(主框架,吸收外壳服务)、`Mew.Host`(新薄宿主 exe)、`Mew.Launcher`(WinExe → Library,工具模块)。`Mew.Host` 引用 Workbench 与 Launcher 模块;Launcher 模块引用 Workbench。
- **模块契约**:`IMewToolModule { Id, DisplayName, Configure(ToolModuleContext) }`;宿主组合根显式列出模块,全部贡献完后统一 `Build()`(宿主独占 Build,与 ADR-000101-03 编译期组合一致)。
- **ToolModuleContext 表面**:裸 `Workbench`(五区贡献 + 运行时方法)、`WindowHandle`、`Window`(宿主主窗口对象,模块模态/Toast/焦点用)、`IHotkeyService`(注册/注销/冲突检测)、`ISettingsService`(模块分节)、`IOverlayService`(AddSearchSource)、`IThemeService`(读取/切换主题模式 + ThemeModeChanged)、`SettingsSectionRegistry`(设置节注册表,宿主 + 模块贡献设置节)。托盘菜单贡献暂缓,不进契约。
- **浮层契约**:`ISearchSource { Id, DisplayName, Search(query, maxResults) → SearchResult(Title, Subtitle, Icon, Activate) }`;行渲染框架统一(图标 + 主行 + 副行,沿用现浮层样式);跨源结果**扁平混排 + 来源标记**;每源限 `maxResults`、框架全局设上限;唯一搜索源时行为与今日一致。
- **设置**:settings.json 结构改为「根节 + 模块节」——根节 `themeMode` / `overlayHotkey`(宿主),模块节按 Id 分(如 `launcher`: `itemsViewMode`);提供一次性旧扁平结构迁移(先例:`MigrateLegacySettingsDocumentLayout`)。设置文档由宿主提供(外观/热键节),模块贡献设置节(Launcher 贡献「数据」节)。
- **热键**:`IHotkeyService` 为中央注册表,浮层呼出键归宿主;Launcher 每项热键经 `ctx.Hotkeys` 注册,冲突检测集中。
- **标题栏/托盘/关于**:归宿主;产品名「Mew Launcher」暂作宿主常量,`AppVersion` 常量移入宿主。
- **移入 Workbench 的外壳服务**:GlobalHotkey、HotkeyParser、TrayIcon、SettingsStore → 设置服务、SelectionModel、HoverRefCount、EmptyState、IconResolver、LaunchDebouncer、浮层机制(OverlayWindow + 搜索源)、标题栏构建、主题循环与持久化。
- **留在 Launcher 模块**:LauncherStore、LauncherData、LauncherItem、LauncherCategory、LauncherRunner、LauncherSearch、ItemHotkeys(领域);分类树、列表/卡片、详情表单、设置「数据」节(专属视图);注册浮层搜索源。
- **宿主组装可测性**:`MewHost` 的「AddModule + Build() 产出 Workbench」与窗口运行(平台注册、`Application.Run`)分离,组装路径不碰窗口。
- **发布**:AOT/Trim/图标/Direct2D 相关 csproj 配置随 exe 移到 `Mew.Host`;Launcher 库保持 AOT/Trim 兼容(沿用源生成 JSON,零反射)。`InternalsVisibleTo` 视需要为测试项目开放。

## Testing Decisions

- **好测试的标准**:只测外部行为——组装结果(Build 校验的成败、五区包含的贡献)、设置迁移的最终数据、浮层聚合的输出(顺序/上限/来源标记)、热键冲突判定——不测实现细节;纯逻辑优先。
- **单一新缝**:`tests/Mew.Host.Tests`(xunit,先例:`tests/Mew.Launcher.Tests` 纯逻辑风格 + `WorkbenchConfigurationTests` 的 Fluent 配置校验风格)。
  - 宿主组装:真实 Launcher 模块 + 测试内定义的假第二模块 → `Build()` 成功且五区含各模块贡献;活动栏↔侧边栏配对违规、ID 冲突被 Build 期校验拒绝。
  - 设置服务:根节 + 模块节分节存取;旧扁平结构 → 新结构迁移。
  - 浮层聚合:多搜索源 → 扁平混排、每源上限、全局上限、来源标记。
  - 热键服务:注册/注销、跨模块冲突检测。
- **既有缝不变**:`tests/Mew.Launcher.Tests` 继续测 Launcher 领域(引用对象由 exe 变为 Library,用例内容不变);Workbench 配置校验用例随外壳服务迁移调整归属。

## Out of Scope

- **运行时插件加载**:机制后置;发现器留作可替换实现,待出现第二个真实工具且确需免重编译增删时再评估(届时宿主转 JIT,另立 ADR)。
- **第二个真实工具**:测试用假模块验证多模块场景,不建真实工具工程。
- **托盘菜单贡献**:暂缓,等真有工具需要(如剪贴板历史)再扩契约。
- **自定义浮层行渲染**:框架统一渲染,契约不放行渲染扩展。
- **产品名抽象**:「Mew Launcher」产品名与窗口标题保持宿主常量,不抽象「应用身份」,待第二个工具出现再说。
- **浮层分组呈现**:保持扁平混排 + 来源标记,不引入按源分组。
- **放弃 NativeAOT**:不评估 JIT 切换(ADR-000101-04 保持有效)。

## Further Notes

- 版本号常量 `AppVersion` 在实现开始时提升为 `v0.2.0`(宿主内;先例:单文件单行提交,如 `ba2885e`)。
- 实现票据按 issue-tracker 约定在实现启动时拆分为 `.scratch/v0.2.0-host-tool-modules/issues/NN-*.md`。
- CONTEXT.md 已同步:新增「工具模块」术语,「呼出浮层」改为框架级机制(Launcher 是搜索源之一)。
- 防分叉两条规则写进 ADR-000200:框架只吸收 ≥2 个工具都需要的功能;工具只通过契约贡献、不绕过 Workbench 直连 MewUI 机制性 API(视图构建除外)。
