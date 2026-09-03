# 03 — IPC 契约与跨进程搜索聚合

**What to build:** 宿主与扩展主机/独立插件间通过命名管道 JSON-RPC 完成搜索注册与聚合，浮层一次输入跨所有已启用源混排。

**Blocked by:** 01 — 插件清单模型与发现（含校验与启用态）, 02 — 扩展主机进程与五区职责切分

**Status:** resolved

- [ ] 宿主为 NamedPipe server（管道名含用户隔离），消息含 `register{ id, capabilities, protocolVersion }` / `searchRequest{ query, maxResults, requestId }` / `searchResponse{ requestId, results }` / `activate{ resultId }` / `ping/pong/shutdown/log`，首包握手校验 `protocolVersion`（当前 1），不匹配拒绝并在设置页提示“协议版本不匹配”
- [ ] `SearchResult{ id, title, subtitle, icon?, payload }` 行渲染由宿主统一，来源标记显示 `displayName`
- [ ] 宿主为唯一聚合点，沿用每源上限 `全局上限 / 源数` 再全局截断、扁平混排的策略；唯一源时行数与样式与 v0.2.0 一致
- [ ] 多源（编译期源 + 环回 fake 独立插件源）一次输入可验证扁平混排、每源上限、全局上限、来源标记均符合预期
- [ ] 未声明 `search` 能力的插件其 `register` 搜索注册被拒绝
