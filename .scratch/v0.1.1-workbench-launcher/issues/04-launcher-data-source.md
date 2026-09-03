# 04 — 启动项模型与 JSON 数据源 + 侧边栏列表

**What to build:** Launcher 的启动项模型与单一 JSON 数据源打通:启动时加载、变更后保存,首次运行生成默认示例;侧边栏按分类展示启动项,活动栏可切换分类。数据读写使用源生成 JSON,无运行时反射。

**Blocked by:** 01 — Workbench 五区外壳与 Fluent API

**Status:** ready-for-agent

- [x] 启动项数据来自单一 JSON 配置(可手写、可备份、可 diff),首次无文件时生成默认示例
- [x] 启动项模型含:名称、命令(程序/脚本/URL)、可选参数与工作目录、分类、可选图标、可选每项热键
- [x] 侧边栏按分类展示启动项,活动栏可切换分类
- [x] JSON 读写用 System.Text.Json 源生成,避免运行时反射
