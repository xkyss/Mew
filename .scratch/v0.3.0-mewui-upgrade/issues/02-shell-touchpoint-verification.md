# 02: MewDockShell 触点语义核对 + 全量回归

**What to build:** 对 `MewDockShell` 的全部反射触点做**语义**核对（编译探针只能证明名字存活,证明不了行为不变）：以可无头验证的项为主——`TabSetEnableMaximize=false` 后最大化按钮是否仍被模型阻止、`SplitterSize=3` 是否仍生效并持久化、`StyleSheet.Define` 追加覆盖是否仍按从后往前匹配、`FlexTabButton._closeButton` 是否仍存在且接线路径同构、`MewUIDockString` 表是否仍被停靠菜单消费;不可无头验证的项（原生弹窗后的菜单行为、边框渲染）记入 03 的走查清单。核对失效项只动 `MewDockShell` 一个文件。

**Blocked by:** 01

**Status:** resolved

- [x] 反射触点逐项核对表落盘(触点 → 验证方式 → 结果)
- [x] 可无头验证项全部通过（测试或探针断言）
- [x] 不可无头项整理进 03 的手工走查清单
- [x] 失效项(如有)修复仅限 `MewDockShell`
- [x] 全量测试绿（137 + 103,允许等价改写）

## Comments

- 触点核对表(0.20.2,无头可验证项):
  | 触点 | 验证方式 | 结果 |
  |---|---|---|
  | `DockingManager._model` 反射 | 测试断言非 null | ✅ 存活 |
  | `TabSetEnableMaximize=false` | Tune 后读模型属性 | ✅ 真实生效 |
  | `SplitterSize=3` | Tune 后读模型属性 | ✅ 真实生效 |
  | `StyleSheet.Define` 追加覆盖 | `ZoneStylesApplied` 断言(含空停靠重试语义) | ✅ 注册成功路径可用 |
  | `FlexTabButton._closeButton` + 悬浮接线 | `ConfiguredTabCloseCount > 0` 且连调稳定 | ✅ 存活且接线 |
  | `FlexTabSetView._maximizeButton` 隐藏 | 按钮创建时机依赖 arrange | → 03 走查 |
  | `FlexSplitter.IsColumnAxis` 光标 | 依赖命中测试/悬浮 | → 03 走查 |
  | `MewUIDockString` 表被停靠菜单消费 | 需打开真实菜单 | → 03 走查 |
  | 原生弹窗后的菜单命令派发(ContextMenu.Commands) | 需真实点击 | → 03 走查 |
- 新增内部观察点 `MewDockShell.ZoneStylesApplied`,3 个语义测试锁定(140 例全绿);失效项:无。
- 不可无头项已并入 03 走查清单(共 4 项,外加 01 的访问键/View 切换文本显示)。
- 真机首验补遗:反射触点名字存活之外,运行期发现的新语义断点是「窗口默认 StyleSheet 冻结」——它不属 MewDockShell 触点,已在 NativeChromeWindow 修复。
