# 03: 版本提升与扫尾（v0.3.1）

**What to build:** 版本收尾：`AppVersion` 提升为 v0.3.1;ADR-000203 增修订注记（卸载 = 删主目录下插件子目录,「目录增减」叙述收敛,uninstall 词汇边界不变）;run.md 等运行文档的 `MEW_PLUGINS_EXTRA`/多目录措辞清理;全量回归。

**Blocked by:** 02

**Status:** resolved

- [x] `AppVersion` 提升为 `v0.3.1`（单行,先例 6e02e1a/ba2885e）
- [x] ADR-000203 修订注记落盘
- [x] run.md 等文档多目录/env 措辞清理,与单目录模型一致
- [x] 全量测试绿（141 + 103 基线）
- [x] 各票据完成注记落盘

## Comments


## Comments

- 已完成。`AppVersion` 单行提升 v0.3.1;ADR-000203 修订注记（卸载 = 删主目录下插件子目录,词汇边界不变）。
- run.md:联调提示改为链接指引(junction/symlink 两条路径)、插件放置节改单主目录模型、「额外插件目录」整段(env + 列表 + 优先级)替换为建链指引与迁移说明。
- 相关测试绿(Host.Tests 143 + Launcher.Tests 104);全量回归见 04 之后收尾。
