# 03: 启用/禁用写权力归一宿主

**What to build:** `plugins.json` 的唯一写者收敛为宿主：扩展主机面板的启用/禁用操作不再本地 Save，改经 IPC 请求宿主落盘，宿主持久化成功后回推刷新两侧；双端不再并发写同一文件，兑现 ADR-000202 的单一事实来源。

**Blocked by:** 02

**Status:** resolved

- [x] 扩展主机开关操作触发「请求宿主落盘」而非本地 Save
- [x] 宿主收到请求后持久化启用态并回推确认，两侧视图一致
- [x] 宿主仍是唯一写者；扩展主机只读启用态
- [x] 测试断言：扩展主机侧开关不产生本地写、仅发起宿主请求

## Comments

- 已完成。`PluginHostApp.TogglePluginEnabled` 不再本地 `Save()`，改经 `EnablePluginIpc.TrySet` 发 `pluginEnableSet` 一次性 IPC；宿主侧 `MewHost.OnPluginEnableSet` 校验 id → `PluginEnableStore.SetEnabled + Save()` 落盘 → 回推 ack；宿主仍是唯一写者（扩展主机侧 `Save()` 调用已清零）。
- T3（exe）由宿主引擎托管立即生效（启用即拉起、禁用即杀）；T2/T1 只落意图、扩展主机下次装载——与 ADR-000203 生效时机分型一致。
- 宿主 `OnPluginEnableSet`/`LaunchPluginProcess`/进程句柄桥接需窗口装配，走代码走查验证（与托盘菜单先例一致）；共享缝（`IpcServer` 一次性消息处理）以 `PluginEnableIpcTests` 3 个新用例锁定（无处理器明确拒绝/有处理器透传/JSON 往返保型）。
- 相关测试：宿主测试工程全量通过（102 → 105 用例）。
