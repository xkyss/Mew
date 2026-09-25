# 03: MewUI #253 tooltip 绕过——上游修复后恢复按钮提示

**What to build:** 无实现工作;本票是**升级备忘**。v0.3.4 因上游 [aprillz/MewUI#253](https://github.com/aprillz/MewUI/issues/253)「Buttons with ToolTip needs to be double-clicked for firing Click event」(open,0.21.1 未修)摘除了可点击按钮上的 ToolTip。**下次升级 MewUI(≥ 修复 #253 的版本)时:恢复下列摘除点并删除对应注释**,再把本票关闭。

**Blocked by:** 上游 aprillz/MewUI#253 修复发布(升级锁版时核对 release notes)

**Status:** blocked (upstream)

- [ ] 升级评审时核对 #253 是否已修复(release notes 或 issue 状态)
- [ ] 恢复 `WorkbenchView.BuildItemButton` 的 `.ToolTip(item.Title)`(活动栏按钮)
- [ ] 恢复 `TitleBarBuilder` 主题按钮的 `.ToolTip("切换主题")` 初始值
- [ ] 恢复 `PluginHostApp.UpdateThemeButton` 的 `b.ToolTip(entry.ToolTip)` 运行时刷新
- [ ] 删除三处「MewUI #253」注释;真机验证悬停出提示后单击仍一次生效

## Comments

- 2026-09-26:症状 = 悬停出 tooltip 后首次点击只关闭 tooltip 不触发 Click,单击呈"两次才生效";日志表现为孤儿 `WINDOW-MOUSEUP`(按下不产生 Click)。上游 #253(2026-09-07,v0.21.1 发布当天)有人以 MewVG 后端复现,本仓库 Direct2D 同样命中;0.20.2 → 0.21.1 的 `NativePopupHost`/`PressCaptureHelper` 捕获生命周期改动为嫌疑面。本仓库绕过 = 摘除三处 ToolTip 赋值(见上),未提交前真机验证通过后与代码同批入库。
