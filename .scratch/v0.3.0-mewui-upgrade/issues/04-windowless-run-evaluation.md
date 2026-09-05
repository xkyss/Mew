# 04: 无窗口 Application.Run 评估（可选,升级回报票）

**What to build:** 0.20 官方支持无窗口 `Application.Run`(纯托盘/热键常驻场景)。评估能否用官方路径替换 `MewHost` 的 0 透明度首启技巧(`Opacity = 0` → Loaded 内 `Hide()`)。行为契约不变:首启全程无可见帧、告警路径(热键被占/主界面缺失)亮窗提示、托盘左键浮层右键菜单、`ShowHostPluginManager` 亮窗。可行则替换并保持契约;不可行则维持现状并在本票记录原因。**运行验证依赖 Windows 环境**(与 03 同机执行)。

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] 0.20 无窗口 Run 的官方形态确认(文档/探针)
- [ ] 可行:替换宿主首启编排,行为契约逐项保持
- [ ] 不可行:维持现状,原因记录于 Comments
- [ ] Windows 侧走查确认首启无可见帧(可与 03 合并执行)

## Comments

