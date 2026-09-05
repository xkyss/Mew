# 02: 版本提升与扫尾（v0.2.5）

**What to build:** 版本收尾：运行时版本号提升为 v0.2.5（宿主内单行改动）；触及代码文档与运行文档措辞扫尾；全量回归。

**Blocked by:** 01

**Status:** resolved

- [x] `AppVersion` 提升为 `v0.2.5`（单行，先例 6e02e1a/ba2885e）
- [x] docs 运行文档如有 MewDock 适配措辞，与 ADR-000205 一致
- [x] 宿主/扩展主机测试工程全量通过
- [x] 各票据完成注记落盘（先例：v0.2.3/v0.2.4 票据 Comments）

## Comments

- 已完成。`AppVersion` 单行提升 v0.2.5;run.md 无 MewDock 适配措辞需对齐;触及代码无「T1/T2」类过时注释残留（LocalizeDockStrings 调用点已改为指向 ADR-000205 的指针注释）。
- 全量：Mew.Host.Tests 137 例 + Mew.Launcher.Tests 103 例,Release 构建通过。
- AOT 发布仍留 Windows 侧（同 v0.2.4:本环境不支持 Cross-OS native compilation;MewDockShell 无新增反射类型——复用既有反射点,裁剪敏感面不变）。
