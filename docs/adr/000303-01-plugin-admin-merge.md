# 插件管理收敛设置→插件：宿主占位窗退役，T3 行经 IPC 现拉

> **一句话定调：T3 行并入扩展主机设置→插件页（宿主代理 adapter + 一次性 IPC），宿主主窗口退役为托盘/热键/告警的隐藏基础设施。**

ADR-000204 让宿主占位窗与扩展主机共用同一份 `PluginAdminPanel`，但两侧行集合互斥（宿主只列 T3 exe 行、扩展主机只列 T2 DLL 行）。现实是：仓库内没有任何 exe 类型插件，宿主占位窗的 T3 行区恒为空态，窗口实际只剩与托盘菜单重复的「打开/重启主界面」按钮；而用户要找插件管理时，入口却藏在托盘三级菜单「插件管理」→ 一个无五区外观的裸窗口。T3 的管控实权（生命周期引擎 + plugins.json）在宿主进程，UI 却可能出现在两个地方，认知与实现都是双份的。

## Considered Options

- **维持现状**：宿主占位窗 + 托盘「插件管理」项原样保留。空窗口与重复按钮继续存在，插件管理入口分裂。→ 未选。
- **托盘项改为跳转主界面设置→插件节**：宿主经注册窗口消息通知扩展主机 `ShowSettingsNav("plugins")`。保留快捷入口，但为一条仅服务跳转的新通路；主界面本就距托盘一步。→ 未选。
- **T3 行并入设置→插件 + 宿主窗口退役（选定）**：见下。

## 决策

1. **设置→插件页统一呈现 T2 与 T3 行**：T2 行区（扩展主机 adapter）之下新增「独立插件（T3，宿主托管）」子节，仍渲染共享 `PluginAdminPanel`（ADR-000204 行契约原样）。行权威在宿主：每次页面重建经一次性 IPC（`pluginRowsRequest`）现拉快照，不缓存跨会话状态。
2. **宿主代理 adapter（`HostProxyPluginAdminService`）坐实第三个 adapter**：`Snapshot()` = 最近一次拉取的行缓存（重绘不重拉，避免 UI 线程反复打管道），`Apply(id, action)` = `pluginAdminAction` 一次性 IPC 转发。动作语义（启用即拉起、禁用即停止、崩溃行重启）全部由宿主侧既有 adapter 执行，扩展主机不感知。
3. **IPC one-shot 家族再添两员**：`pluginRowsRequest`/`pluginAdminAction` 沿用「首行嗅探类型、应答即关、2s 连接与读超时」的既有模式（ADR-000204 决策 5）；行 DTO（`PluginAdminRowDto`）复用宿主快照条目承载描述符，行状态以枚举名传输，双向映射只存在于 IPC 消息层；新类型全部注册 `IpcJsonContext`（宿主 AOT source-gen）。
4. **宿主不可达的降级语义诚实**：拉取失败时 T3 子节渲染警示行「宿主不可达，独立插件暂不可管理（原因）」而非空态误导；动作三态（Ok/Rejected/Unreachable）在通知条如实呈现。
5. **宿主主窗口退役**：删除 `BuildHostPlaceholder`、托盘「插件管理」菜单项及引擎状态变化的窗口消息刷新链；窗口本体保留（0 透明度隐藏），继续承载托盘回调、全局热键、浮层归属与告警 toast，内容换为最小告警说明。宿主保留：插件发现/快照写入、生命周期引擎、T3 拉起、IPC 服务。
6. **PluginHost 崩溃窗口期的管控真空明确接受**：扩展主机崩溃期间 T3 无 UI 可管（引擎防循环锁兜底失控上限），用户以托盘「重启主界面」恢复后管控完整恢复（新实例经 IPC 重新接管，宿主侧 Apply 与进程归属无关）。兜底按钮/托盘项不做——为不存在的 T3 插件预付常驻 UI 成本不值。

## Consequences

- 删除：宿主占位窗行区、`ShowHostPluginManager`、`WmRefreshPluginRows` 自窗口消息、托盘 `MenuPluginManager` 项及其分发分支。
- 新增：`pluginRowsRequest`/`pluginAdminAction` 报文与 `IpcServer` 分发入口（内存直连可测）、`HostPluginAdminIpc`（扩展主机侧传输）、`HostProxyPluginAdminService`（第三个 adapter）。
- 设置页打开期间 T3 引擎状态变化不自动刷新（与 T2 现状一致，重进页面即刷新）——可接受，未引入反向推送。
- `PluginAdminPanel` 从「两侧各一份面板」变为「一份面板、三个 adapter」；宿主 adapter 从「UI + IPC 双消费」退为纯 IPC 服务。
- 本 ADR 是 ADR-000204 的**管理面收敛补丁**：行标签契约（000203 第 6 条）、宿主唯一写者（000203 第 7 条）、锁纪律（000204 决策 2）全部原样；ADR-000204 决策 6 中「宿主占位窗形态不变」的承诺由本 ADR 取代。

## Alternatives Rejected

- 见 Considered Options；跳转案因「为一条跳转消息引入宿主→扩展主机通知通路」与现状收益不成比例被放弃。
