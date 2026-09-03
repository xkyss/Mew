# 自绘标题栏走 DWM 原生帧扩展(NativeChromeWindow)

v0.1.1 使用系统标题栏;v0.1.2 起 Workbench 提供自绘标题栏的窗口壳「NativeChromeWindow」:采用官方样例 `NativeCustomWindow` 的 DWM 原生帧扩展(客户端内容上扩进原生标题栏区域)而非全自绘窗口,并决定 Win11-only、不做 Win10 圆角兼容。选择原生帧路线,因为它在 Direct2D + NativeAOT 目标下开销更低(无逐帧 alpha 合成),圆角/阴影/缩放/贴边由 OS 提供,与「个人自用、开发与使用环境均为 Win11」的定位匹配。

## Considered Options

- **DWM 原生帧扩展(NativeCustomWindow 路线,选定)**:`ExtendClientAreaTitleBarHeight` 把内容扩展进标题栏区域,OS 保留圆角/阴影/缩放;Win32 后端把扩展区变 HTCAPTION 获得拖拽移动;窗口按钮原生优先(`HasNativeChromeButtons` 时隐藏自绘按钮)、缺失时自绘兜底。官方注释明确建议 Win11+/macOS 优先此路。
- **全自绘窗口(CustomWindow 路线)**:`AllowsTransparency` + 自绘圆角/边框/阴影,为 Win10 及更早提供圆角观感;代价是逐帧 alpha 合成的 CPU/GPU 开销,且最大化时要手动清零圆角/边框/阴影。仅当必须兼容 Win10 时才值得。未选。

## Consequences

- 目标运行环境限定 Win11;Win10 及更早自然降级为方角,不修。
- 框架职责:Workbench 提供 `NativeChromeWindow`(标题栏左区/居中标题/右区可注入,含窗口按钮),Launcher 创建窗口并配置;关闭→托盘、`NativeMessage`、`Loaded` 等窗口级钩子留在应用侧,框架只做外壳。
- 标题栏是外壳 chrome,不属五区色板;术语见 `CONTEXT.md`「标题栏」「菜单栏」。
- 自绘关闭按钮调 `Close()` 仍走 `Closing` 事件,托盘常驻行为不变。

## Known Issues(上游框架缺陷)

**运行时切换主题导致局部区域渲染损坏**(MewUI 0.19.1):应用运行中通过状态栏按钮在 跟随系统/亮/暗 间切换主题后,部分区域不随主题重绘或损坏(实测:侧边栏变纯黑、编辑器区仍亮,呈稳定混合态;无自绘 chrome 的普通窗口同样复现,与 `NativeChromeWindow` 无关)。

- **根因**:MewUI 0.19.1 主题切换的传播/重绘不完整;`Window.OnThemeChanged`/`Window.ThemeChanged` 不随应用主题切换触发(0.19.1 行为,样例 main 分支不同),`Application.ThemeChanged` 虽触发但各区域重绘不一致。
- **影响**:v0.1.1 的状态栏主题切换即有此问题(冒烟时未深查);v0.1.2 的标题栏配色切换同样受影响。
- **缓解**:切换后重启应用可恢复完整主题;`NativeChromeWindow` 已订阅 `Application.ThemeChanged` 并强制整窗失效,标题栏自身(图标/菜单/标题)在切换后能重绘,但内容区损坏仍受上游缺陷影响。
- **修复路径**:升级 MewUI/MewDock(受 ADR 000101-01 版本锁约束)或上游修复后跟随发布;已可向 https://github.com/aprillz/MewUI 提交 issue。
