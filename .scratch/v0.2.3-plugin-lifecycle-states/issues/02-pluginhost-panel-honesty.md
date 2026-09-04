# 02: 扩展主机面板诚实化

**What to build:** 扩展主机（管 T1/T2 富插件）的设置→插件列表改用 01 的行状态推导：不再只凭 `plugins.json` 推「已启用/已禁用」而忽略运行时实态；过滤掉 exe 行（T3 独立插件不在扩展主机列表出现）；本会话仍加载的已禁用 T2 如实显示并带「重启扩展主机后生效」副提示。行为不变：扩展主机不在列表、不允许禁用。

**Blocked by:** 01

**Status:** resolved

- [x] 扩展主机插件列表只列 T1/T2（过滤 exe 行），特殊容器（扩展主机自身）仍不出现
- [x] 行状态词由 01 推导得出，不再只按 enabled 直推
- [x] T2 禁用且本次会话仍加载 → 显示「已禁用」+「重启扩展主机后生效」副提示，不谎称已停
- [x] 清单错误 / ID 重复行标红且不提供误导性开关；需 JIT 置灰语义保留

## Comments

- 已完成。扩展主机设置→插件列表经 `PluginDiscovery.IsExtensionHostManaged` 过滤（T1/T2 非 exe 行），行标签改用 `PluginRowState.Derive`，本会话仍加载的已禁用 T2 如实带「重启扩展主机后生效」副提示。
- 共享过滤判定 `IsExtensionHostManaged` 落 `PluginDiscovery` 并补测试（`PluginDiscoveryTests`），宿主/扩展主机两侧语义同一来源。
- 相关测试：宿主测试工程全量通过。
