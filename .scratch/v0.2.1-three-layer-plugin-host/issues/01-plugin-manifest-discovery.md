# 01 — 插件清单模型与发现（含校验与启用态）

**What to build:** 端到端可验证的插件发现——扫描用户目录与安装目录下的插件清单，校验后在设置中列出并支持启用开关；清单错误不拖垮宿主，单 AOT 下对 DLL 类插件置灰提示。

**Blocked by:** 无，可立即开始

**Status:** resolved

- [x] 扫描 `%APPDATA%/Mew/Plugins/<id>/plugin.json` 与安装目录 `Plugins/`（递归一层）可发现插件，合法清单（`id` 全局唯一 kebab-case、`displayName`、`version` semver、`entry{type:dll|exe,path,args}`、`capabilities{search,settingsSection,hotkeys}`、`permissions`、`protocolVersion`）被列入设置→插件列表
- [ ] 清单经 JSON Schema 校验 + `id` 唯一性 + semver 校验，`id` 重复/schema 非法/版本非法的项在列表中标红且不加载，后发现的重复 `id` 被拒绝
- [ ] `capabilities` 未声明的能力在后续 `register` 阶段被拒绝（越权不生效），本票仅校验清单层面声明合法性
- [ ] 启用态持久化于独立 `plugins.json`（`{[id]: enabled}`，与 `settings.json` 分离），开关读写往返，禁用项不参与后续拉起
- [ ] 仅有宿主 AOT 时，`entry.type=dll` 的插件在列表中置灰并提示“需 JIT 扩展主机”（占位逻辑，不含真实 ALC 拉起）
- [ ] 清单错误仅影响该插件，其余插件仍可列出；宿主启动不因单清单损坏而中断
