# 宿主 + 编译期工具模块:主框架与工具分离

Launcher 长期是 Workbench 的唯一消费者,`LauncherApp.cs`(1954 行)一人身兼四职——应用引导、五区组装、Launcher 专属 UI、领域操作——托盘/全局热键/设置/浮层等外壳服务混在工具代码里。写第二个工具时要么复制要么重写,埋下「主框架分叉」的种子(主体相同又略有差别的多份框架)。决定:v0.2.0 起采用**宿主 + 编译期工具模块**架构——新建薄宿主 `Mew.Host`(exe),Workbench 吸收共享外壳服务成为「主框架」,Launcher 从 WinExe 变为第一个工具模块(Library);工具通过显式契约 `IMewToolModule` 编译期注册进宿主,共享同一份主框架源码。目标:主框架稳定且可持续演进,工具可替换、单独演进,彼此不产生分叉。

## Considered Options

- **运行时插件**:字面意义的插件——启动时目录扫描 + `Assembly.Load`。扩展性最强,但与本仓库硬冲突:NativeAOT(ADR-000101-04)不支持运行时加载托管程序集,宿主须放弃 AOT;并推翻 ADR-000101-03 的「无插件加载」;还须承担契约/版本锁定(MewUI 0.19.1 对齐)、加载失败降级、进程内无故障隔离等成本。个人自用、唯一消费者,收益存疑。→ **机制后置**:按它的契约设计,机制先用编译期;「发现器」留作可替换实现,等第二个工具出现且确需免重编译增删时再切(届时宿主转 JIT)。
- **每工具独立 exe**:各自进程、隔离最好,但托盘/热键/浮层的接线代码每个工具各写一份,「插件」形态感弱,与「一个托盘、一套热键」的体验目标不符。→ 未选。
- **纯工程拆分、不建宿主**:只拆 `LauncherApp.cs`,不解决「多工具共享外壳服务」这个分叉根源。→ 未选。
- **宿主 + 编译期工具模块(选定)**:见下。

## 契约(记录)

```
IMewToolModule { Id, DisplayName, Configure(ToolModuleContext) }
ToolModuleContext: Workbench(裸对象,宿主独占 Build())/ WindowHandle / Window(宿主主窗口,模态/Toast/焦点)
                  / IHotkeyService / ISettingsService(模块分节)/ IOverlayService(搜索源)/ IThemeService
                  / SettingsSectionRegistry(设置节注册表)
ISearchSource { Id, DisplayName, Search(query, maxResults) → SearchResult(Title, Subtitle, Icon, Activate) }
```

- 浮层机制(全局热键 → 快捷搜索)归框架;跨源结果**扁平混排 + 来源标记**,行渲染框架统一(图标 + 主行 + 副行);唯一搜索源时行为与今日一致,零 UX 回归。
- 设置文档收归宿主(外观/热键两节归宿主),工具模块以「设置节」贡献自己的内容(Launcher 贡献「数据」等节)。
- 托盘菜单贡献暂缓,等真有工具需要时再扩契约。

## Consequences

- `Mew.Launcher` 由 WinExe 变 Library:ApplicationIcon / PublishAot / MewUIBackend 移入 `Mew.Host`;AppVersion 常量移入宿主;exe 输出 `Mew.Host.exe`(模块为 `Mew.Launcher.dll`,避免重名),产品名「Mew Launcher」暂作宿主常量保留。
- **ADR-000101-03(编译期组合)与 ADR-000101-04(NativeAOT)保持有效**:宿主 AOT 发布,模块编译期并入,零反射零动态加载;本 ADR 是 000101-03 后果「后续扩展以编译期注册方式接入」在工具层级的落地。
- settings.json 由扁平结构(themeMode / overlayHotkey / itemsViewMode)迁移为「根节 + 模块节」(宿主:themeMode、overlayHotkey;模块 launcher:itemsViewMode 等),需一次性迁移逻辑(仓库已有 `MigrateLegacySettingsDocumentLayout` 先例)。
- 防分叉规则:框架只吸收 ≥2 个工具都需要的功能;工具只通过契约贡献,不绕过 Workbench 直连 MewUI(视图构建除外——工具照常使用 MewUI 控件拼 UI)。
- 术语:CONTEXT.md 新增「工具模块」,「呼出浮层」改为框架级机制(Launcher 是搜索源之一)。
