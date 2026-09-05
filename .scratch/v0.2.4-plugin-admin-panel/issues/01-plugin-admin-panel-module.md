# 01: 共享插件管理面板模块（渲染 + 路由 + 无头测试）

**What to build:** `Mew.Workbench` 长出共享的插件管理面板模块：行视图模型 + `Snapshot()/Apply(id, action)` interface + 进程无关渲染器（行标题格式、警示配色、单动作按钮、校验文案、Hint 非空才渲染、行序按 id 排序）。本票不接线任何 exe，但渲染与路由带完整无头测试——测试跑绿即可独立验收；这是后续两票共用的地基。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 行视图模型 + `Snapshot()/Apply` interface 定义完成；渲染器只吃行视图模型，无进程感知分支
- [x] 行标题格式、警示配色、单动作按钮（动作 None 显「—」）、校验文案与 v0.2.3 两侧现状逐项一致；Hint 非空才渲染
- [x] 行序按 id 排序（不区分大小写）——对 v0.2.3 的唯一确认豁免
- [x] 无头渲染断言：健康启用/禁用（含「重启扩展主机后生效」副提示）/已崩溃/清单错误/ID 重复/需 JIT 行的标签、颜色、按钮文本
- [x] fake adapter 路由断言：动作 None 不路由、崩溃行路由「重启」、启用/禁用路由正确
- [x] 测试工程全量通过；共享模块不引入 AOT 不兼容面（无反射/无裁剪敏感 API）

## Comments

- 已完成。`PluginAdminPanel`（Mew.Workbench）：`PluginAdminRow`（描述符+行状态，标题格式渲染器统一）、`PluginAdminOutcome/PluginAdminResult`（Ok/Rejected/Unreachable 三态）、`IPluginAdminService { Snapshot, Apply }`、进程无关渲染器（含空态提示与「仅行存在时渲染」的页脚文字，文案归所在 exe）。
- 无头测试 9 例（复用零句柄组装 + 树遍历先例）：文案与配色、健康态覆盖标红、行序大小写不敏感、空态无页脚、页脚条件渲染、重复刷新不残留、Activate 路由（None 不路由/未知 id 忽略）、回调与重绘。
- 实现 note：`StackPanel.Spacing` 为实例属性（fluent 扩展在类内部被属性遮蔽），ctor 内改属性赋值。
