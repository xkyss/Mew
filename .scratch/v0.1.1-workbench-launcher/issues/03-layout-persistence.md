# 03 — 布局保存/恢复

**What to build:** 用户调整五区布局后,Workbench 将布局序列化为 JSON 自动保存;重启应用时恢复上次布局,首次启动则使用默认五区布局。

**Blocked by:** 01 — Workbench 五区外壳与 Fluent API

**Status:** ready-for-agent

- [x] 调整分区(大小、停靠、显隐)后重启应用,布局与上次一致
- [x] 保存/恢复由 Workbench 管理(布局变更后/退出前),对使用者透明
- [x] 首次启动无布局记录时,呈现默认五区布局
