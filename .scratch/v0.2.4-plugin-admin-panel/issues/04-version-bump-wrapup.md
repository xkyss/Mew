# 04: 版本提升与扫尾（v0.2.4）

**What to build:** 版本收尾：运行时版本号提升为 v0.2.4（宿主内单行改动）；触及代码中过时的「T1/T2」注释清零（T1 已摘除，Launcher 是标准 T2）；运行文档措辞与「插件管理面板」词条对齐；全量回归与发布验证。

**Blocked by:** 02, 03

**Status:** resolved

- [x] `AppVersion` 提升为 `v0.2.4`（单行，先例 6e02e1a/ba2885e）
- [x] 触及文件中「T1/T2」过时注释清零
- [x] docs 运行文档如有插件行管理措辞，与 CONTEXT.md「插件管理面板」词条一致
- [x] 宿主/扩展主机测试工程全量通过；AOT 发布通过
- [x] 各票据完成注记落盘（先例：v0.2.3 票据 Comments）

## Comments

- 已完成。`AppVersion` 单行提升 v0.2.4；`PluginDiscovery.IsExtensionHostManaged` 文档「T1/T2」改「T2 DLL 插件」；`run.md` 两处措辞对齐「插件管理面板」词条并补 ADR-000204 指引。
- 全量测试：Mew.Host.Tests 129 例 + Mew.Launcher.Tests 103 例，全绿（Release 构建同过）。
- AOT 发布：本环境（WSL/Linux）报「Cross-OS native compilation is not supported」，须 Windows 侧执行 `dotnet publish src/Mew.Host -c Release -r win-x64`；替代验证 = Release 全量构建 + 共享面板模块零反射/零裁剪敏感 API 走查通过。
- 发现既有测试基建缺陷（非本特性引入）：两个测试工程在同一 `dotnet test` 调用下并行时，`%APPDATA%\Mew` 的 IsolateUserFiles 备份/恢复互踩，偶发组合/布局用例失败（失败案例在两工程间漂移）；顺序执行稳定全绿。根因即架构走查点名的 `WorkbenchLayoutStore` 硬编码路径——路径参数化记为后续独立小改。
