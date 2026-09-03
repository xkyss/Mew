# v0.2.0 验收记录

> 依据 `.scratch/v0.2.0-host-tool-modules/spec.md` 与票据 01–09 验收项。
> 状态:🟢 已自动化验证 / 🔲 待人工冒烟
> 主题:v0.2.0 宿主 + 编译期工具模块架构(Mew.Host.exe 唯一启动入口,Launcher 为模块)。

## 一、已自动化验证(本环境可执行部分)

| 验收项 | 结果 | 方式 |
|---|---|---|
| 宿主组装(真实 Launcher + 假模块) | 🟢 | `Mew.Host.Tests` 12 用例全绿:两模块五区贡献 + 设置节 + 浮层搜索源组装 Build 通过;活动栏↔侧边栏配对违规、跨模块停靠 ID 冲突被 Build 期拒绝 |
| 全局热键集中化(票据 07) | 🟢 | `HotkeyServiceTests` 全绿:注册/注销/IsRegistered、跨模块同组合冲突(文本写法无关)、Dispatch 按 id 路由、FindOwner 点名占用方(评审修复) |
| 按键词汇表(评审修复) | 🟢 | `HotkeyKeysTests` 全绿:解析与显示名共用单一来源,命名键/字母数字功能键往返一致,别名与大小写不敏感 |
| 设置分节与旧结构迁移 | 🟢 | `SettingsServiceTests` 7 用例全绿(根节 + 模块节往返、旧扁平 itemsViewMode → launcher 节迁移写回、损坏 JSON 回退、根值/模块节类型不符静默回退) |
| 浮层聚合(混排顺序/每源上限/全局上限/来源标记) | 🟢 | `OverlaySearchAggregatorTests` 全绿(单源/双源/违约源兜底/零源) |
| Launcher 领域回归(模块化后) | 🟢 | `Mew.Launcher.Tests` 全量 100 用例绿(引用对象由 exe 变 Library,内容不变;含契约替身用例) |
| NativeAOT 发布(win-x64) | 🟢 | `dotnet publish -c Release -r win-x64` 成功,单文件 `Mew.Host.exe` 7.2MB(产物入 `artifacts/v0.2.0/`);编译 0 错误(Trim/AOT 分析警告为既有 WorkbenchView 反射工作区,同 v0.1.3) |
| 启动冒烟(窗口路径) | 🟢 | `Mew.Host.exe` 启动后进程常驻(消息循环运行)、主窗口创建且可见、标题 = `Mew Launcher — v0.2.0`(Win32 枚举验证),随后强制终止 |
| AppVersion | 🟢 | 宿主内常量 `src/Mew.Host/MewHost.cs` = `v0.2.0`(随票据 06 落位;Agents.md 版本号唯一来源已更新) |

## 二、待人工冒烟清单(逐项操作 → 预期)

### 01 窗口与五区
- [ ] 🔲 启动 `artifacts/v0.2.0/Mew.Host.exe`:主窗口标题「Mew Launcher — v0.2.0」,活动栏「启动 / 设置」,侧边栏分类树,编辑器区启动项列表(卡片/列表),底部输出面板,状态栏
- [ ] 🔲 关闭主窗口 → 进程不退出(托盘常驻);托盘图标存在,「显示主窗口 / 退出」可用
- [ ] 🔲 标题栏 File/View/Help 菜单、主题循环按钮、齿轮(设置)与关于对话框可用

### 02 浮层与热键
- [ ] 🔲 全局热键(默认 Ctrl+Alt+Space)呼出浮层:输入过滤、回车启动选中项、上下键切换、Esc 关闭;结果与窗口内数据一致
- [ ] 🔲 设置 → 热键:点「更改」按下新组合 → 提示「已生效」、新键立即生效、旧键失效;Esc 取消;无效组合 / 与已注册热键(含启动项每项热键)冲突有提示
- [ ] 🔲 启动项每项热键(如 Ctrl+Shift+1):绑定后全局生效、关闭主窗口后仍生效;按住不连发(票据 07 改进)

### 03 设置与主题
- [ ] 🔲 设置侧边栏顺序 = 外观 / 热键 / 数据;数据节显示 launcher.json 路径,「打开所在文件夹」可用
- [ ] 🔲 主题三选一即时生效并持久化(settings.json themeMode);标题栏循环按钮与设置一致
- [ ] 🔲 真实首启设置迁移:旧扁平 settings.json(根节 itemsViewMode)自动迁入 launcher 节;`%APPDATA%\Mew\layout.json` 的旧 settings 组件迁移为独立设置文档 ID
- [ ] 🔲 主题切换局部渲染损坏(若有)属已知上游缺陷(ADR 000102-01,MewUI 0.19.1),重启恢复,与本版本架构无关

## 三、评审修复记录(代码评审后,已提交)

针对 code-review 两轴结论的落地情况:

| 评审发现 | 处置 | 提交 |
|---|---|---|
| LauncherModule 遗留 `(SettingsService)` 契约破坏性转型 | ✅ 修复:契约补 `Save()`,模块字段改 `ISettingsService`,仅按契约读写;新增契约替身用例 | `8796c43` |
| 损坏/类型不符的设置文件会抛异常阻塞启动 | ✅ 修复:`GetString`/`ReadSection` 对类型不符静默回退;新增 2 用例 | `8796c43` |
| `ActivityGlyph`/`IconElement`/`SettingsIconData`/`HotkeyWarning` 宿主与模块重复 | ✅ 修复:提取 `ShellIcons`(≥2 消费方,ADR-000200 防分叉);模块私有图标保留 | `6e28193` |
| `KeyToName` 与 `HotkeyParser.TryMapKey` 双份按键表 | ✅ 修复:提取 `HotkeyKeys` 词汇表单一来源;新增往返用例 | `6e28193` |
| 宿主组合根无 `AddModule(...)` API(spec 契约) | ✅ 修复:组合根经 `AddModule(module)` 显式注册 | `9a7574c` |
| 冲突提示不点名占用方(与 v0.1.6 不一致) | ✅ 修复:`Register` 带 label + `FindOwner`,改绑/每项热键冲突提示点名(「与启动项『X』…冲突」) | `9a7574c` |
| `ThemeVariant` 三处 switch(Repeated Switches) | ✅ 修复:`_themeModes` 数据驱动循环切换 | `6e28193` |
| `RevealDocumentHotkey` 命名误导(Mysterious Name) | ✅ 修复:改名 `RevealDetailShortcut` 并注明「窗口内快捷键,非全局热键」 | `6e28193` |
| 破坏性变更未带 `!` 标记 | 📝 登记采纳:v0.2.0 票据提交(exe 入口改名、Launcher exe→Library)未带 `!`;历史提交不回改(避免改写已归档票据记录),在此登记为已知偏差,后续破坏性变更提交须带 `!` | — |
| `AppVersion` 非单行独立提交 | 📝 登记采纳:提升提交(`15a59e1`)先于宿主存在,常量随票据 06 迁入 `MewHost.cs`;终态为单一常量(AGENTS.md 版本号来源),不改写历史 | — |
| MOD_NOREPEAT(按住不连发)行为差异 | 📝 登记采纳:票据 07 有意改进(避免误连发),已在票据 07 Comments 注明;非评审要求回退 | — |
| 热键失败反馈渠道(输出面板日志 → 宿主 Toast) | 📝 登记采纳:票据 07 有意取舍(浮层键注册失败属宿主职责),已在票据 07 Comments 注明 | — |
| 手工冒烟未执行 | 📝 保持 🔲:交互路径延至「待人工冒烟清单」,见下节 | — |

## 四、已知上游缺陷(非本版本缺陷)

- **运行时主题切换局部渲染损坏**:MewUI 0.19.1,普通窗口亦可复现;重启恢复;升级需先评审(ADR 000101-01)。本版本主题交互同样受影响。
