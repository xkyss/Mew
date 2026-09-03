# 09 — 版本收尾与冒烟

**What to build:** 版本提升与发布收尾:AppVersion 常量提升为 v0.2.0(宿主内,单文件单行提交先例)、发布产物更新、全量手工冒烟(浮层、热键、托盘、设置迁移、主题、AOT 发布)并落 verification 记录。

**Blocked by:** 07,08

**Status:** resolved

- [x] AppVersion 提升 v0.2.0,单行提交(先例:6e02e1a / ba2885e)——常量随票据 06 落位 `src/Mew.Host/MewHost.cs` = v0.2.0,Agents.md 版本号唯一来源已指向宿主
- [x] artifacts 更新为 v0.2.0 产物
- [ ] 冒烟清单全过:浮层呼出/搜索/启动、呼出键改绑与冲突、托盘常驻、设置迁移无损、主题切换、AOT 发布(AOT 发布与启动冒烟已自动化验证;交互项见 verification.md 待人工冒烟)
- [x] verification 记录落盘 `.scratch/v0.2.0-host-tool-modules/`

## Comments

- **AppVersion**:宿主常量 `private const string AppVersion = "v0.2.0";`(票据 06 随 MewHost 建立落位,非本票据单行提交;「单行提交」先例适用于后续 v0.2.1 等提升)。Agents.md 的 Version bump 节已更新为指向 `src/Mew.Host/MewHost.cs`。
- **artifacts**:`artifacts/v0.2.0/` 收录 NativeAOT 发布产物 `Mew.Host.exe`(单文件 7.2MB,同 v0.1.3 的 6.5MB 量级)+ `Mew.Host/Mew.Launcher/Mew.Workbench.pdb`。发布命令 `dotnet publish src/Mew.Host -c Release -r win-x64` 成功;Trim/AOT 分析警告(IL2026/IL2075/IL3050)全部来自既有 WorkbenchView 反射样式工作区,与 v0.1.3 同类,非本版本回归。
- **启动冒烟(自动化)**:`Mew.Host.exe` 启动后进程常驻 6 秒+(平台注册/组合根/Build/窗口/托盘/热键注册/消息循环全路径无崩溃),Win32 枚举确认主窗口可见且标题 = `Mew Launcher — v0.2.0`,随后强制终止。设置迁移/浮层聚合/热键冲突由两套测试全绿兜底(见 verification.md)。
- **verification.md**:`.scratch/v0.2.0-host-tool-modules/verification.md` 落盘——已自动化验证 8 项(测试 97+8 全绿、AOT 发布、启动冒烟、AppVersion)+ 待人工冒烟清单(窗口五区/托盘/浮层呼出与搜索启动/呼出键改绑与冲突 UI/主题切换/真实首启迁移)。
- 未勾选项说明:交互冒烟(呼出浮层、改绑热键 UI、托盘常驻、主题切换)需人工逐项操作,已列 verification.md 待人工冒烟,不冒充「全过」。
