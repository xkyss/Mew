# 02: 宿主切换共享面板（进程内 adapter + 锁收敛 + IPC 无锁写修复）

**What to build:** 宿主占位窗的 T3 行区块换用共享插件管理面板：进程内 adapter 包生命周期引擎 + `plugins.json`——engine 锁与启用锁全部收进 adapter 内部（engine 锁 → 启用锁单向取锁升格为不变量），unknown-id / 保留 id 拒绝门收敛于此；宿主的 IPC one-shot 启用/禁用处理器改走 adapter 同一条加锁路径，线程池无锁写 `plugins.json` 的并发交错点就此消除。用户可见行为与 v0.2.3 完全一致。

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] 宿主面板行由共享面板渲染：标题/配色/单动作/校验文案/空态提示与 v0.2.3 一致
- [ ] adapter 之外无任何调用者接触启用锁或引擎内部锁；锁顺序以 fake 断言（engine 锁内取启用锁、单向）
- [ ] IPC 启用/禁用请求经 adapter：锁内落盘，T3 立即生效（启用拉起/禁用杀进程），ack 语义与文案不变
- [ ] unknown-id / 保留 id 请求拒绝，文案与 v0.2.3 一致
- [ ] 测试：unknown-id 门 + IPC 路径落盘走锁的断言
- [ ] 宿主 AOT 发布成功；托盘「插件管理」→ 面板行为走查通过
- [ ] 面板级内容（标题/版本/打开/重启主界面按钮/崩溃说明页脚）原样保留
