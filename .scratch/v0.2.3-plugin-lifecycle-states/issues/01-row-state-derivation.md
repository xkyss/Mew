# 01: 插件行状态推导纯逻辑

**What to build:** 把「参与策略（enabled）× 插件健康态 × 宿主崩溃集合 × 本会话是否仍加载」推导为插件行要展示的（状态词、动作、副提示），做成无 UI 依赖的共享纯逻辑与行模型，供宿主与扩展主机两侧面板复用；T2 禁用但本次会话仍加载时，状态词下须带「重启扩展主机后生效」副提示，不谎称已停。

**Blocked by:** None (can start immediately).

**Status:** resolved

- [x] 提供纯逻辑推导函数：输入 enabled（策略）、PluginHealth（含清单错误/ID 重复/需 JIT）、是否已崩溃、本会话是否仍加载，输出（状态词、动作、副提示）
- [x] 健康且启用 → 「已启用」，健康且禁用 → 「已禁用」；清单错误 / ID 重复 / 需 JIT / 已崩溃以健康态覆盖策略词
- [x] 已崩溃（仅 T3 语义下出现）→ 动作变「重启」而非「启用/禁用」
- [x] T2 禁用且本会话仍加载 → 状态词「已禁用」+ 副提示「重启扩展主机后生效」
- [x] 单测覆盖四维组合，纯逻辑无窗口依赖

## Comments

- 已完成。`Mew.Workbench/Plugins/PluginRowState.cs` 新增共享纯逻辑（`Derive(enabled, health, crashed, stillLoaded)` → 状态词/动作/副提示三元组），无 UI 依赖。
- 单测 `PluginRowStateTests` 覆盖健康×启用/禁用、清单错误/ID 重复/需 JIT 覆盖、已崩溃→重启动作、T2 禁用仍加载→副提示等四维组合。
- 相关测试：宿主测试工程全量通过。
