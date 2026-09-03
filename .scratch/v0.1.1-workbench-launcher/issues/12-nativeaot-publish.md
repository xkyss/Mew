# 12 — NativeAOT 单文件发布(win-x64)

**What to build:** 整个 Launcher 可发布为 NativeAOT 单文件 exe(win-x64、Direct2D 后端、完整裁剪),启动接近瞬时;对发布产物跑通规格中的手工冒烟清单。

**Blocked by:** 07 — 启动执行 + 日志 + 状态栏、09 — 全局热键呼出浮层、11 — 托盘常驻

**Status:** ready-for-agent

- [x] 可按 NativeAOT 发布 win-x64 单文件 exe,完整裁剪无反射
- [x] 发布产物启动接近瞬时,浮层呼出流畅
- [x] 对发布产物跑通 spec 手工冒烟清单(五区、主题、布局恢复、增删改查、搜索、浮层、每项热键、托盘)
