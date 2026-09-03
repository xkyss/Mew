# 06 — 启动类型维度(推导 + 徽标 + 过滤)

**What to build:** 新增「启动类型」维度:由启动命令推导「URL / 程序」两档(脚本 .bat/.cmd 并入程序),不落盘存储;卡片/列表显示类型徽标,列表工具行提供类型过滤下拉(全部 / URL / 程序)。类型与命令恒一致,命令是唯一事实来源。

**Blocked by:** None — can start immediately(与分类链正交;前提是 v0.1.5 的卡片/列表形态已落地)

**Status:** resolved

- [x] 卡片/列表按命令区分显示类型徽标(URL / 程序)
- [x] 列表工具行类型过滤下拉:全部 / URL / 程序,切换即时生效
- [x] 脚本(.bat/.cmd)命令显示为「程序」
- [x] 类型由命令推导、不落盘;修改命令后徽标与过滤结果跟随变化

**Comments**

- 展示形式(用户反馈后三轮调整,最终形态):卡片——标签置于名称正下方(10px、普通文字色);列表——标签紧跟名称右侧(10px、与名称同色),命令行独立一行。`#` 前缀自带「标签」语义,与过滤下拉共用 `ItemKindLabel` 文案映射;`ItemKindTag` 单一样式,卡片与列表共用。
- 顺带统一:原 `LauncherRunner` / `IconResolver` 各自的私有 `IsUrl`(语义完全相同)改为委托 `LauncherData.KindOf`,URL 判定单一来源,与 ADR 000106-02「同一推导函数」一致。
- 空态:列表空态判定引入 `hasFilter`,`EmptyState.ForList` 增加可选参数;聚合有内容但被搜索或类型过滤排除 → 「无匹配启动项」(过滤导致时按钮为「全部类型」清除过滤);聚合为空 → 「暂无启动项」引导新增。code-review 修正了「空库+过滤被误判为无匹配」的边界。
- 测试:LauncherDataTests 新增 `KindOf` 推导 Theory(含大小写、.bat/.cmd 脚本并入程序、空命令),EmptyStateTests 新增过滤空态用例,连同既有 44 例通过。
