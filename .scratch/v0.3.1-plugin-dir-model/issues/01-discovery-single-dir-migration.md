# 01: 发现单值化与 settings 迁移（链接扫描测试锁定）

**What to build:** 插件扫描根收敛为唯一主目录：`ResolvePluginRoots`/`GetExtraPluginDirs`/安装目录根退役，宿主与扩展主机两处发现接线改为单值 `pluginDir`（缺省用户目录）；settings 一次性迁移（`pluginDirs` 列表 → 单值，取首项、缺省回退、损坏回退，被移除的附加目录由宿主日志提示人工建链接）；**链接子目录扫描以测试锁定**——真实目录、链接目录、重复 id 先到优先三个用例（无头 symlink 即可）。主界面对用户可见的行为：原有插件（含链接挂入的开发插件）照常被发现、加载、进插件管理面板。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 扫描根单值化：`ResolvePluginRoots`/`GetExtraPluginDirs`/安装目录根及其调用点（宿主 + 扩展主机）移除
- [x] settings 迁移：`pluginDirs` → `pluginDir`（首项优先、缺省回退、损坏回退），沿用一次性迁移先例
- [x] 被移除的附加目录写宿主日志提示「人工以链接挂入主目录」
- [x] 测试：单根发现 + 链接子目录枚举/穿透 + 重复 id 先到优先；迁移往返三态
- [x] 既有插件发现测试按单根模型等价改写,全量测试绿
- [x] 快照写入/推送流程不变（宿主唯一发现来源,ADR-000202 不动）

## Comments


## Comments

- 已完成。`PluginDiscovery`：`Discover(string root)` 单根(容器根与「根本身即插件目录」两种形态都保留),新增 `DefaultUserPluginsDir`;`ResolvePluginRoots`/`GetExtraPluginDirs`/`ExtraDirsEnvVar`/两参重载退役。`SettingsService`:`PluginDirs` → `PluginDir` 单值,`MigrateLegacyPluginDirs` 一次性迁移(首项进 pluginDir,余项进 `MigratedOutPluginDirs` 供宿主日志提示,元素类型不符整列放弃)。
- 两处接线(宿主/扩展主机)改为单值 + 缺省回退;宿主把 MigratedOutPluginDirs 逐条写日志提示人工建链。
- 测试:发现测试按单根模型改写并新增 **链接子目录枚举/穿透**(无头 symlink,无特权环境自动降级为仅真实目录断言)、`DefaultUserPluginsDir`;settings 迁移三态(列表→单值+余项记录/类型不符整列放弃/往返)。141 + 104 绿。
