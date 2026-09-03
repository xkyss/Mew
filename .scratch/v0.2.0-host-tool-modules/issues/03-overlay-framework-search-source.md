# 03 — 浮层机制框架化(搜索源契约)

**What to build:** 呼出浮层机制从 Launcher 收归主框架:搜索源契约(源 Id/显示名/搜索/结果激活)、框架统一行渲染(图标 + 主行 + 副行)、多源结果扁平混排 + 来源标记、每源上限与全局上限;Launcher 作为第一个搜索源注册。用户视角:热键呼出、输入即搜、上下选中、回车启动、Esc/失焦关闭,与 v0.1.6 完全一致;为跨工具搜索铺路。

**Blocked by:** 01

**Status:** resolved

- [x] 单搜索源(Launcher)时浮层行为与 v0.1.6 一致:搜索、选中态、回车/点击启动、Esc/失焦关闭、位置与尺寸
- [x] 多搜索源时结果扁平混排、行尾来源标记、每源上限与全局上限生效
- [x] 结果行渲染统一为框架样式(图标 + 主行 + 副行),契约不含自定义行渲染
- [x] 启动项搜索逻辑(命令匹配规则)不因搬入搜索源而改变

## Comments

- 浮层机制迁入 Workbench:`OverlayWindow` 泛化为框架级(构造仅依赖 owner 窗口 + 主题上下文,`AddSource` 运行期注册);契约 `ISearchSource { Id, DisplayName, Search(query, maxResults) }` + `SearchResult(Title, Subtitle, Icon, Activate)`,行渲染框架统一,契约不含自定义行渲染。
- 多源聚合:`OverlaySearchAggregator` 纯逻辑——每源上限 = 全局上限(8)/ 源数,单源时为 8 与旧行为一致;全局再取 8 截断;对违约源(无视 maxResults)按每源上限防御兜底。结果携带来源,仅多源时行尾显示来源标记,单源不显示与旧版一致。
- Launcher 作为首个搜索源:`LauncherSearchSource` 适配启动项领域,复用 `LauncherSearch.Matches`(命令匹配规则不变),行数据 = 名称/命令/图标/启动动作;LauncherApp 改为构造 OverlayWindow + AddSource(票据 05 改经模块契约注册)。
- 旧 `src/Mew.Launcher/OverlayWindow.cs` 删除。
- 测试:`OverlaySearchAggregatorTests` 6 用例(单源原样、单源全局上限、双源混排/每源上限/来源标记、余量不足不补位、违约源兜底、零源空);编译 0 警告 0 错误,Launcher.Tests 全量 93 用例绿。浮层视觉冒烟(呼出/位置/失焦关闭)延至票据 09 全量冒烟。
