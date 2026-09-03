# MewDock 作为停靠引擎

Workbench 需要 VSCode 式停靠布局(标签页、拆分、自动隐藏、拖拽重组)。决定:使用官方扩展 **MewDock**(`DockingManager` + 类型化/JSON 布局模型)作为停靠引擎,v1 不自研;Workbench 在其上封装类型化五区布局 API。

## Considered Options

- **自研停靠引擎**:完全可控,但开发与调优成本高,难以在 v1 达到 MewDock 的成熟度(拖拽、自动隐藏、弹出等)。
- **MewDock(选定)**:与 MewUI 同源、AOT/Trim 友好、提供 JSON 序列化模型,契合「布局保存/恢复」需求。

## Consequences

- 停靠能力边界受 MewDock 约束;若后续遇到其无法满足的场景,再评估替换。

## Known Issues(上游框架缺陷)

**窗口拖拽路由器竞态崩溃**(MewUI 0.19.1 / MewDock 0.19.1):快速连续鼠标操作停靠元素(tab、窗格)时,
`WindowDragDropRouter.TryPromoteCandidate` 会对已 detach 的 `CanDrag` 元素调用 `TranslatePoint`,抛
`InvalidOperationException: The specified element is not in the same visual tree`,UI 循环中止、应用退出。

- **根因**:MewDock 将 tab/tabset/标题栏设为 `CanDrag=true`;视图重建(`FlexTabSetView.BuildTabs`)延迟到
  layout pass,会把 tab 按钮整体 detach。若 mouse-down 记录的拖拽候选恰在此后被 detach,下一次 >4DIP 的
  鼠标移动即触发未守卫的 `TranslatePoint`。
- **影响**:非本仓库代码可修复(上游 `WindowDragDropRouter` 缺陷),所有 MewDock 使用者都会遇到;崩溃率为
  低概率间歇性(快速点击 tab 时相对高发)。已用差异测试确认与仓库代码无关(仅含五区外壳的提交同样可复现)。
- **缓解**:避免超快速连续点击 dock tab;崩溃后重启布局自动恢复。
- **修复路径**:升级 MewUI/MewDock(受 ADR 000101-01 版本锁约束,升级需评审)或上游修复后跟随发布;已可向
  https://github.com/aprillz/MewUI 提交 issue。
