# 04 — DLL 运行时加载（ALC 隔离）

**What to build:** 扩展主机按目录隔离动态加载 DLL 运行时插件，复用既有富插件契约，越权与热更新行为可预期。

**Blocked by:** 01 — 插件清单模型与发现（含校验与启用态）, 02 — 扩展主机进程与五区职责切分, 03 — IPC 契约与跨进程搜索聚合

**Status:** resolved

- [ ] 扩展主机按插件目录以 `AssemblyLoadContext` 隔离加载 `entry.type=dll` 插件，扫描与加载失败不影响其他插件
- [ ] DLL 插件复用 `IMewToolModule / ISearchSource` 既有契约写法，经扩展主机代理向宿主注册搜索源，搜索结果可经 03 的 IPC 聚合进宿主浮层
- [ ] `capabilities` 未声明的能力（如未声明 `search` 却尝试注册搜索源）被拒绝
- [ ] 禁用 DLL 插件即卸载对应 ALC 并从宿主侧移除其搜索源与设置节
- [ ] 热更新场景在设置页提示“需重启扩展主机”并提供一键重启，重启后新 DLL 生效
