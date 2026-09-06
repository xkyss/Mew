# v0.3.3 插件管理收敛规格

> 版本: v0.3.3
> 对应 ADR: `docs/adr/000303-01-plugin-admin-merge.md`

## Problem Statement

托盘菜单的「插件管理」打开的是宿主（Mew.Host）自己的占位窗口：里面是版本信息、「打开/重启主界面」两个与托盘菜单重复的按钮、以及 T3 独立插件行区。由于仓库内没有任何 `entry.type=exe` 插件，T3 行区恒为空态——用户点开它只能看到一个近乎空白的裸窗口，而真正有信息量的插件管理（目录、DLL 插件开关）全在主界面的设置→插件页里。插件管理入口分裂在两处，宿主窗口为「T3 管控不依赖扩展主机活着」付出的常驻成本买不到对应价值。

## Solution

T3 行并入扩展主机设置→插件页：新增「独立插件（T3，宿主托管）」子节，经一次性 IPC（`pluginRowsRequest`）向宿主现拉行快照、动作经 `pluginAdminAction` 转发宿主即时执行；行渲染仍走共享 `PluginAdminPanel`，宿主侧新增代理 adapter（第三个 adapter）。宿主占位窗退役——主窗口降级为托盘/热键/告警的隐藏基础设施，托盘「插件管理」菜单项删除。宿主不可达时 T3 子节诚实降级（警示行，非空态误导）。

## User Stories

1. As 用户, I want 在设置→插件同一页看到并操作 DLL 插件与独立插件, so that 管理插件只认一个入口。
2. As 用户, I want T3 行的启用/禁用/重启点击后立即生效并如实提示, so that 不需要理解「重启后生效」与「即时生效」的差异被撒谎掩盖。
3. As 用户, I want 宿主不可达时 T3 子节显示明确警示而非空列表, so that 不会被误导为「没有独立插件」。
4. As 用户, I want 托盘右键菜单只留 打开主界面/重启主界面/退出, so that 不再出现点了没内容的「插件管理」项。
5. As 框架主人, I want 行渲染与动作路由继续只有一份实现（ADR-000204 契约延续）, so that 三个 adapter 不产生文案/语义漂移。
6. As 框架主人, I want T3 行权威只在宿主（引擎实态 + plugins.json）, so that 扩展主机不缓存跨会话插件状态、不出现第二写者。
7. As 框架主人, I want 宿主占位窗代码路径（构建/行刷新/自窗口消息）删除, so that 宿主不再养一套插件行 UI。
8. As 开发者, I want 新 IPC 入口在无宿主处理器时明确降级且可无头测试, so that 降级语义有锁定、不靠约定。

## Implementation Decisions

- IPC：`pluginRowsRequest`/`pluginRowsAck`、`pluginAdminAction`/`pluginAdminActionAck` 一次性消息，行 DTO `PluginAdminRowDto`（描述符复用 `PluginSnapshotEntry`，状态/动作为枚举名）；全部注册 `IpcJsonContext`（宿主 AOT）。
- `IpcServer` 增加两个 one-shot 分支与内存直连入口（`TryFetchPluginRows`/`TryPluginAction`），无处理器时明确拒绝。
- `HostPluginAdminIpc`（扩展主机侧传输，2s 连接+读超时）与 `HostProxyPluginAdminService`（行缓存 + 动作转发）。
- `PluginHostApp.RefreshPluginPanel` 追加 T3 子节：拉取成功渲染共享面板（footer 说明宿主即时执行语义），失败渲染警示行；通知条文案区分 T2（重启扩展主机生效）与 T3（宿主即时执行）。
- 宿主：`IpcServer` 构造接入 `FetchPluginRows`/`OnPluginAdminAction`（走既有 adapter 加锁路径）；删 `BuildHostPlaceholder`/`ShowHostPluginManager`/`WmRefreshPluginRows`；托盘删 `MenuPluginManager`。
- 版本号提升至 v0.3.3（`MewHost.cs` 单行常量）。

## Testing Decisions

- 主缝仍为 `tests/Mew.Host.Tests`（先例：v0.2.1–v0.2.4）。新增 `HostPluginAdminIpcTests`：IpcServer 行拉取/动作入口的无处理器降级与处理器透传、行 DTO 对象与 JSON 双往返保真、动作名解析门、代理 adapter 行缓存与不可达三态。
- `TrayIconMenuTests` 收敛为新菜单三项；删除「插件管理」分发用例。
- 无 UI 像素断言：设置页组装走既有惰性构建路径，行渲染契约已由 `PluginAdminPanelTests` 锁定。

## Out of Scope

- 设置页打开期间 T3 引擎状态变化的自动刷新（与 T2 现状一致，重进页面即刷新）。
- PluginHost 崩溃窗口期的 T3 兜底管控 UI（用户已确认接受：重启主界面后管控完整恢复；引擎防循环锁兜底失控上限）。
- ADR-000204 遗留的 IPC 传输层问题（字符串嗅探分发、长连接双读者竞争）。
