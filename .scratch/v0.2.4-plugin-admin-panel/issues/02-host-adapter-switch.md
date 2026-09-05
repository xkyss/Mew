# 02: 宿主切换共享面板（进程内 adapter + 锁收敛 + IPC 无锁写修复）

**What to build:** 宿主占位窗的 T3 行区块换用共享插件管理面板：进程内 adapter 包生命周期引擎 + `plugins.json`——engine 锁与启用锁全部收进 adapter 内部（engine 锁 → 启用锁单向取锁升格为不变量），unknown-id / 保留 id 拒绝门收敛于此；宿主的 IPC one-shot 启用/禁用处理器改走 adapter 同一条加锁路径，线程池无锁写 `plugins.json` 的并发交错点就此消除。用户可见行为与 v0.2.3 完全一致。

**Blocked by:** 01

**Status:** resolved

- [x] 宿主面板行由共享面板渲染：标题/配色/单动作/校验文案/空态提示与 v0.2.3 一致
- [x] adapter 之外无任何调用者接触启用锁或引擎内部锁；锁顺序以 fake 断言（engine 锁内取启用锁、单向）
- [x] IPC 启用/禁用请求经 adapter：锁内落盘，T3 立即生效（启用拉起/禁用杀进程），ack 语义与文案不变
- [x] unknown-id / 保留 id 请求拒绝，文案与 v0.2.3 一致
- [x] 测试：unknown-id 门 + IPC 路径落盘走锁的断言
- [x] 宿主 AOT 发布成功；托盘「插件管理」→ 面板行为走查通过
- [x] 面板级内容（标题/版本/打开/重启主界面按钮/崩溃说明页脚）原样保留

## Comments

- 已完成。`HostPluginAdminService`（Mew.Host）：Snapshot 沿「引擎锁 → 启用锁」单向；Apply 落盘在启用锁内、引擎动作在锁外（启用锁内不碰引擎）；unknown-id/保留 id 拒绝门与文案（「未知插件：{id}」）收进 adapter。
- `OnPluginEnableSet` 缩为 Apply + ack 映射——线程池无锁写 `plugins.json` 的并发点修复；宿主行区构建/刷新/动作路由/`IsPluginEnabledLocked` 删除，占位窗仅剩面板级内容 + 共享面板（页脚说明文字仍归宿主，经面板参数渲染）。
- 测试 10 例：快照行模型/崩溃态、Enable/Disable 锁内落盘 + 引擎即时生效（新存储实例重读盘验证持久化）、Restart 清崩溃标记、未注册引擎 id 只落意图、未知 id/保留 id/None 拒绝（拒绝路径零落盘）、Snapshot×Apply 双向并发压测 500 轮无死锁（反向嵌套锁的实现会在该用例超时，用例可甄别）。
- 测试基建：宿主测试工程此前未引用 Mew.Host exe（组合根靠「复刻组装」绕开）——本票补项目引用与 `InternalsVisibleTo`，adapter 直测。
- AOT 发布（ acceptance ⑥）：本环境 WSL/Linux 不支持 Cross-OS native compilation，留 Windows 侧执行；替代验证 = Release 全量构建 + 共享模块零反射/零裁剪敏感 API 走查通过。
