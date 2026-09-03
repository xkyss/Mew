# 01 — NativeChromeWindow 骨架与 Launcher 切换

**What to build:** Workbench 提供自绘标题栏的窗口壳 NativeChromeWindow(DWM 原生帧扩展):标题栏左/中/右三区注入点、窗口按钮原生优先(`HasNativeChromeButtons` 时隐藏自绘)、缺失时自绘兜底、拖拽移动、双击最大化/还原、激活/非激活边框色。Launcher 主窗口改用此窗口,居中标题显示「Mew Launcher — v0.1.2」(左右区暂空),并完成既有能力回归验证。

**Blocked by:** None — can start immediately

**Status:** ready-for-agent

- [ ] 主窗口无系统标题栏,自绘标题栏显示居中标题「Mew Launcher — v0.1.2」
- [ ] 拖拽移动、双击最大化/还原、贴边缩放可用;最大化时内容不错位
- [ ] 五区布局在自绘标题栏下方正常渲染
- [ ] 亮/暗主题切换时标题栏背景与激活/非激活边框色即时联动
- [ ] 关闭按钮→托盘常驻、全局热键呼出浮层、每项热键均不回归
- [ ] 布局持久化:窗口尺寸/位置重启后恢复
