# 04: 宿主 T3 生命周期引擎

**What to build:** 宿主获得对独立插件（T3 exe）的运行期托管：按启用态拉起进程、禁用即杀、按清单入口重启；区分「主动停」与「意外退出/IPC 断连」，只有意外才标已崩溃；崩溃态只在运行期推导、不落盘；带防循环计数（启动后短时崩 N 次 → 本次运行不再自动拉起）。**T3 意外崩溃后宿主本次运行不自动重拉，仅标记 + 手动重启**（与扩展主机不自愈一致，防循环兜底启动期重试）。宿主重启后按启用态重新拉起。

**Blocked by:** None (can start immediately).

**Status:** resolved

- [x] 宿主能拉起/杀掉/重启 enabled 的 exe 插件（复用扩展主机进程管理先例），按清单 `entry.path` 启动
- [x] 禁用 T3 → 杀进程，来源从宿主侧消失（兑现 v0.2.2 故事 20）
- [x] 主动停不标已崩溃；意外退出 / IPC 断连 → 标该插件已崩溃
- [x] 崩溃态不落盘（plugins.json 仍只存 enabled）；宿主重启后按启用态重拉
- [x] 防循环：启动后短时崩 N 次 → 本次运行不再自动拉起，标已崩溃待手动重启
- [x] 行为测试用假进程/假管道缝驱动，无真实子进程

## Comments

- 已完成。`Mew.Workbench/Plugins/PluginLifecycleEngine.cs` 为共享引擎：`Register/ReconcileStartup/Start/Restart/Stop/MarkUnexpectedExit/Remove` + 运行态快照；进程生死经 `IPluginProcessHandle` launcher 注入，宿主以真实 `Process` 桥接，引擎逻辑无进程可单测。
- 崩溃归因：仅意外退出（进程 Exited / IPC 断连且句柄仍托管中）标已崩溃并累计防循环计数；主动停先摘句柄再杀，回调不误标。
- 防循环：`CrashWindow` 内连续崩溃达 `CrashLoopThreshold`（3 次）→ 本次运行不再自动拉起（仅挡 auto 路径，手动重启总是放行）。
- 单测 `PluginLifecycleEngineTests` 用假进程句柄驱动：拉起/幂等/禁用即杀不标崩/意外退出标崩/防循环达阈值停自动重拉/窗口过期计数复位/未托管 id 忽略。
- 宿主接线（`MewHost`：注册 exe 项、按启用态 `ReconcileStartup`、IPC 断连归因）在票据 03/05 中完成。
