# v0.2.4 插件管理面板规格

Status: ready-for-agent

> 版本: v0.2.4
> 对应 ADR: `docs/adr/000204-01-plugin-admin-panel.md`

## Problem Statement

v0.2.3 把行标签契约钉在 `PluginRowState.Derive` 纯逻辑上，但它的两个调用方——宿主占位窗的 T3 行与扩展主机设置→插件的 T2 行——各养一套近乎克隆的渲染与路由：行标题格式、健康态配色、动作按钮、校验文案两侧各写一份，字节级相同却无人守护；两份内存态 `plugins.json` 副本各随其主；engine 锁 → 启用锁的取锁顺序只活在注释里。更实质的裂缝在调用方式上：IPC 启用/禁用请求（线程池线程）写 `plugins.json` 完全无锁，与宿主 UI 动作路径（持锁）并发，是真实无守卫交错点；扩展主机开关路径的管道读无超时，宿主挂起即冻结扩展主界面 UI。纯函数已经很深，真 bug 藏在「怎么调」里——正是纯函数抽取救不了的那一类。

## Solution

收敛为共享的**插件管理面板**（CONTEXT.md 词条已落）：interface 只有两个动作——`Snapshot()` 返回行视图模型、`Apply(id, action)` 返回结果；行渲染与动作路由只此一份、完全进程无关。两个真实 adapter 坐实 seam：宿主进程内 adapter（生命周期引擎 + `plugins.json`，锁与 unknown-id 门全收内部），扩展主机 IPC adapter（发现结果 + 本地只读缓存 + 实载集合，写走一次性 IPC 并加 2s 读超时）。宿主的 IPC one-shot 处理器改走 adapter 同一条加锁路径，顺手消除无锁写并发点。行序统一按 id（唯一 UX 豁免），其余严格零回归。

## User Stories

1. As 用户, I want 宿主侧 T3 行与扩展主机侧 T2 行呈现完全一致的标题格式、健康态配色、动作按钮与校验文案, so that 无论在哪一侧管理插件都不需要重新学习。
2. As 用户, I want 插件行状态与动作继续遵循行标签契约（策略单标签 + 健康态覆盖，崩溃行换「重启」）, so that 行为与 v0.2.3 完全一致、无回归。
3. As 用户, I want 两侧插件行统一按 id 排序, so that 行序稳定可预期且两面板彼此一致（对原扩展主机「发现顺序」的唯一可见微变，已确认豁免）。
4. As 用户, I want T2 行「已禁用，重启扩展主机后生效」副提示照常出现, so that 诚实提示保留（宿主侧因不传「本会话仍加载」输入而天然不触发，行为不变）。
5. As 用户, I want 扩展主机侧开关操作有 2 秒读超时, so that 宿主异常挂起时扩展主界面不再整个冻死。
6. As 用户, I want 宿主不可达时开关操作提示「未生效」且不改变本地状态, so that 操作结果诚实（现状语义保留）。
7. As 用户, I want 宿主占位窗、托盘「插件管理」入口与扩展主机设置→插件面板的窗口形态不变, so that 使用习惯完全不变。
8. As 框架主人, I want 插件行渲染与动作路由只有一份实现, so that 改文案/配色/语义不再两处同步、不再漂移。
9. As 框架主人, I want 宿主侧 engine 锁与启用锁全部收进宿主 adapter 内部, so that 锁顺序从注释升格为构造上不可能违反的不变量。
10. As 框架主人, I want IPC 启用/禁用请求与宿主 UI 动作走同一条加锁路径, so that 线程池无锁写 `plugins.json` 的并发交错点被消除。
11. As 框架主人, I want unknown-id / 保留 id 拒绝门收敛在宿主 adapter 一处, so that IPC 与 UI 动作不再各写一遍校验。
12. As 框架主人, I want 扩展主机本地启用缓存保持只读（启动供 DLL 装载跳过禁用项、ack 成功后内存同步、永不本地落盘）, so that 「宿主唯一写者」语义原样兑现。
13. As 框架主人, I want 面板级按钮与页脚（打开/重启主界面、退出主界面进程）不进共享模块, so that 进程特有 UI 不被共享层绑架。
14. As 框架主人, I want ack 报文映射留在 IPC 消息层, so that adapter 不感知 IPC 报文形状。
15. As 框架主人, I want 分两步落地（先共享模块 + 宿主切换，再扩展主机切换）, so that 每步可独立验收回退、AOT 发布风险先行验证。
16. As 开发者, I want 渲染可无头断言（给定行视图模型断言标签/按钮/警示色）, so that 行为不随重构漂移。
17. As 开发者, I want 动作路由可无头断言（禁用行不可路由、崩溃行路由到重启）, so that 路由语义有锁定。
18. As 开发者, I want 宿主 adapter 的锁顺序与 unknown-id 门可用 fake 断言, so that 并发契约可测而非只靠注释。
19. As 开发者, I want IPC adapter 的成功/宿主拒绝/传输失败三路径可测, so that 开关结果语义有锁定。
20. As 文档读者, I want 「插件管理面板」词条与既有插件词汇衔接, so that 「插件页/插件列表/T3 UI」式散称退出文档。
21. As 开发者, I want 触及代码中过时的「T1/T2」注释顺手清理（T1 已摘除，Launcher 是标准 T2）, so that 注释不再误导后续读者。

## Implementation Decisions

- **模块落位**：共享模块进 `Mew.Workbench`（宿主与扩展主机的公共层，已承载五区/浮层 UI 代码），不新建工程。
- **interface**：`Snapshot()` → 行视图模型集合（描述符 + `PluginRowState` + 校验错误）；`Apply(id, action)` → 结果（成功/错误）。渲染器只吃行视图模型，不感知进程。
- **两个 adapter**：宿主进程内 adapter 包生命周期引擎 + 启用存储——双锁收内部，engine 锁 → 启用锁单向取锁为不变量，unknown-id / 保留 id 门在此；扩展主机 adapter 包发现结果 + 本地只读启用缓存 + DLL 装载实载集合，写走既有一次性 IPC。
- **IPC one-shot 处理器改走 adapter**：不再自行读写 `plugins.json`；ack 映射留 IPC 消息层。
- **本地缓存角色钉死**：启动读盘 + ack 后内存同步，永不本地 `Save()`。
- **读超时**：2 秒，与连接超时对称，超时按宿主不可达处理。
- **零 UX 回归**：行文案、标签契约、按钮语义、警示配色保持 v0.2.3；唯一豁免 = 行序统一 id 排序；面板级内容与进程分工（宿主 T3 / 扩展主机 DLL 行）不动。
- **分两步**：步骤一共享模块 + 宿主切换（验证 AOT 发布无坑），步骤二扩展主机切换。

## Testing Decisions

- **好测试的标准**：只测外部行为——渲染结果（标签/按钮/警示色）、动作路由语义、锁与校验门契约、开关三结果；不测窗口像素、管道帧格式与引擎内部。
- **单一高位缝**：宿主测试工程为唯一主缝（插件测试簇所在，沿用 v0.2.1–v0.2.3 纪律），复用「无窗口/消息循环、零句柄组装真实控件树 + 树遍历断言」先例：断言 Label 文本与 Foreground 颜色；按钮断言只断内容文本、不模拟点击——点击路由经 adapter fake 覆盖。
- **四组用例**：① 渲染断言（健康/禁用/崩溃/清单错误/需 JIT 行 + Hint 条件）② 动作路由（动作 None 不路由、崩溃行路由重启）③ 宿主 adapter（fake 断言锁顺序单向 + unknown-id 门）④ IPC adapter（成功/拒绝/传输失败）。
- **先前技艺**：行状态纯推导用例、组合测试的无头组装与树遍历、一次性 IPC 服务端用例，均为本规格测试直接先前技艺。

## Out of Scope

- **不动既有 ADR 决策**：三层结构、宿主独立启动、生命周期词汇、`plugins.json` 宿主唯一写者全部原样（本版是 ADR-000203 的实现收敛补丁）。
- **不修 IPC 传输层其余问题**：字符串嗅探分发、长连接双读者竞争、T3 搜索断头路——归架构走查候选 2 另案。
- **不做其余走查候选**：MewDock 适配析出（候选 3）、插件目录配方收敛（候选 4）、settings.json 单写者（候选 5）、LauncherModule 拆分（候选 6）。
- **不做分工扩张**：宿主面板不展示 T1/T2 行；不新增安装/卸载 UI；不改窗口形态与入口。

## Further Notes

- 版本号常量 `AppVersion` 在实现开始时提升为 `v0.2.4`（宿主内单行改动，先例 `6e02e1a`/`ba2885e`）。
- CONTEXT.md「插件管理面板」词条已随 grilling 落盘；本规格与 ADR-000204-01 同步归档。
- 实现前按 issue tracker 约定将本规格拆为 `.scratch/v0.2.4-plugin-admin-panel/issues/NN-*.md`，`Status: ready-for-agent`。
- 本规格源自架构走查（improve-codebase-architecture）候选 1，grilling 两轮共 12 项决策全部经用户确认；测试缝（宿主测试工程单一高位缝 + 无头控件树 + adapter fake）在 grilling 中确认，未另行复议。
