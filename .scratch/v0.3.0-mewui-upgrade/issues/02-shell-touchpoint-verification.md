# 02: MewDockShell 触点语义核对 + 全量回归

**What to build:** 对 `MewDockShell` 的全部反射触点做**语义**核对（编译探针只能证明名字存活,证明不了行为不变）：以可无头验证的项为主——`TabSetEnableMaximize=false` 后最大化按钮是否仍被模型阻止、`SplitterSize=3` 是否仍生效并持久化、`StyleSheet.Define` 追加覆盖是否仍按从后往前匹配、`FlexTabButton._closeButton` 是否仍存在且接线路径同构、`MewUIDockString` 表是否仍被停靠菜单消费;不可无头验证的项（原生弹窗后的菜单行为、边框渲染）记入 03 的走查清单。核对失效项只动 `MewDockShell` 一个文件。

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] 反射触点逐项核对表落盘(触点 → 验证方式 → 结果)
- [ ] 可无头验证项全部通过（测试或探针断言）
- [ ] 不可无头项整理进 03 的手工走查清单
- [ ] 失效项(如有)修复仅限 `MewDockShell`
- [ ] 全量测试绿（137 + 103,允许等价改写）

## Comments

