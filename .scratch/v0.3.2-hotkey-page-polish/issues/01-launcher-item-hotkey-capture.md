# 01: Launcher 启动项热键改为按键捕获控件

**What to build:** 启动项编辑表单的每项热键输入从 `TextField` 手输字符串（`LauncherModule.cs` 约 L1194，占位「可选每项热键,如 Ctrl+Shift+1」）改为与设置→热键页一致的按键捕获控件：点击进入捕获态（提示「按组合键…（Esc 取消）」），全局拦截 `KeyEventArgs` 收集修饰键+主键，经 `HotkeyParser.TryParse` 校验，冲突用 `HotkeyService.FindOwner` 预检点名；清空入口（删除已设热键）。即时校验与红色无效提示随之退役。用户可见行为：编辑启动项时按一次组合键即完成设置，格式/冲突错误即时可见。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 启动项表单热键字段 = 捕获按钮（复用/抽取 PluginHostApp `OnPluginHostPreviewKeyDown` 捕获逻辑为可共享组件）
- [ ] Esc 取消、清空已设热键入口
- [ ] 冲突预检（FindOwner 点名）+ `HotkeyParser` 校验，错误即时提示
- [ ] 保存链路不变（每项热键仍经宿主中央热键服务注册）
- [ ] 无头测试锁定捕获逻辑；全量测试绿

## Comments

