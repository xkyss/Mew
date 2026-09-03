# v0.1.1 Workbench 框架与 Launcher 规格

Status: ready-for-agent

> 版本:v0.1.1(本 spec 即该版本的 PRD,存于 issue tracker)
> 对应 ADR:`docs/adr/000101-*`(锁版本、MewDock、编译期组合、NativeAOT)

## Problem Statement

作为个人开发者,我想基于 MewUI 快速搭出 VSCode 式布局的桌面工具,但 MewUI 只提供控件、面板与停靠原语,没有「工作台」外壳(活动栏、侧边栏、编辑器区、底部面板、状态栏、主题、布局记忆)。每次做新工具都要重复搭这套外壳。同时,我想要一个常驻托盘、热键秒开的启动器来快速打开程序/脚本/URL——现有启动器要么太重,要么不能按自己的方式定制。

## Solution

构建一个可复用的 **Workbench 框架库**,提供 VSCode 式五区布局、亮暗主题与布局保存/恢复,并用类型安全 Fluent API 在编译期组装。同仓交付一个示例工具 **Launcher(应用启动管理器)** 验证框架能力:管理并快速启动程序/脚本/URL,支持搜索过滤、每项热键与全局热键呼出浮层,托盘常驻。

## User Stories

### Workbench(框架使用者视角)

1. As 框架使用者, I want 用类型安全 Fluent API 在代码里声明五区布局, so that 不用写 XAML 或配置文件就能搭出工作台。
2. As 框架使用者, I want 在活动栏注册分类图标, so that 侧边栏能按分类切换显示工具视图。
3. As 框架使用者, I want 向侧边栏注册工具视图, so that 每个分类有自己的面板。
4. As 框架使用者, I want 向编辑器区注册文档视图(标签页), so that 内容以 tab 组织并支持停靠与拆分。
5. As 框架使用者, I want 向底部面板注册输出类视图, so that 日志/终端等辅助内容可停靠、可折叠。
6. As 框架使用者, I want 在状态栏展示上下文信息与操作入口, so that 用户能看到当前状态。
7. As 框架使用者, I want 工具通过 Workbench 主题上下文读取五区色板, so that 工具视图与工作台外观保持一致。
8. As 框架使用者, I want 应用内手动切换亮/暗主题, so that 用户可覆盖系统默认外观。
9. As 框架使用者, I want 布局自动保存并在启动时恢复, so that 用户调好的布局下次打开还在。
10. As 框架使用者, I want 工作台以编译期组合的方式组装(无 DI、无插件加载), so that 应用可 AOT/Trim 裁剪、依赖显式、调试简单。

### Launcher(终端用户视角)

11. As 用户, I want 在窗口内管理启动项(增删改查), so that 不用手改 JSON 文件。
12. As 用户, I want 启动项带分类, so that 活动栏可按分类切换浏览。
13. As 用户, I want 侧边栏列表支持搜索过滤, so that 能快速找到要启动的项。
14. As 用户, I want 在编辑器区查看并编辑启动项详情(名称、命令、参数、工作目录、分类、图标、每项热键), so that 配置可视化、不易填错。
15. As 用户, I want 底部面板显示启动日志, so that 启动失败时能知道原因。
16. As 用户, I want 状态栏显示运行状态(如最近一次启动结果), so that 当前状态一目了然。
17. As 用户, I want 按全局热键(默认 Ctrl+Alt+Space)呼出浮层, so that 在任何窗口之上都能快速启动。
18. As 用户, I want 浮层输入即过滤、回车即启动、Esc 即关闭, so that 全程不用离开键盘。
19. As 用户, I want 浮层与窗口内管理共用同一份启动项数据, so that 两处看到的永远一致。
20. As 用户, I want 给常用启动项绑定每项热键, so that 常用项一键直达。
21. As 用户, I want 关闭主窗口后托盘常驻、热键仍可用, so that 不打断当前工作流。
22. As 用户, I want 启动项图标缺省取 exe 图标, so that 列表与浮层容易辨识。
23. As 用户, I want 数据源是单个 JSON 文件, so that 可手写、可备份、可 diff。

### 发布(终端用户视角)

24. As 用户, I want 应用以 NativeAOT 单文件发布且启动接近瞬时, so that 热键呼出体验顺滑。

## Implementation Decisions

- **模块划分**:两个项目——`Mew.Workbench`(框架库)与 `Mew.Launcher`(示例 App,引用框架库);解决方案 `Mew.slnx`。
- **依赖锁定**:所有 MewUI 相关包固定 0.19.1(`Aprillz.MewUI`、`Aprillz.MewUI.MewDock`),升级需先评审(ADR `000101-01`)。
- **停靠引擎**:五区布局由 Workbench 在 MewDock 之上封装类型化 API,使用者不接触 MewDock 原始模型;布局序列化为 JSON 保存/恢复(`%APPDATA%\Mew\layout.json`,ADR `000101-02`)。
- **组合方式**:编译期组合,类型安全 Fluent API;无 IoC 容器、无运行时插件加载(ADR `000101-03`)。
- **主题**:默认跟随系统,可手动亮/暗切换;五区色板收敛在 Workbench,通过主题上下文提供给工具;强调色默认 `Accent.Blue`。
- **Launcher 双形态**:窗口内管理(活动栏切分类、侧边栏列表+搜索、编辑器区详情编辑、底部面板日志、状态栏运行状态)与呼出浮层(默认 `Ctrl+Alt+Space`),共用同一启动项数据源。
- **启动项数据源**:单一 JSON 配置文件(`%APPDATA%\Mew\launcher.json`),可手写、变更后自动保存;模型含名称、命令(程序/脚本/URL)、可选参数与工作目录、分类、可选图标(缺省取 exe 图标)、可选每项热键。
- **常驻**:托盘常驻,关闭主窗口不退出;v1 不做开机自启。
- **发布形态**:NativeAOT 单文件(`win-x64` + Direct2D + 完整裁剪);框架保持 AOT/Trim 兼容——JSON 用 System.Text.Json 源生成、避免运行时反射(ADR `000101-04`)。

## Testing Decisions

- **测试缝**:唯一主缝 = **Launcher App 本身**——所有 Workbench/Launcher 功能通过示例应用验收(符合已确认的 Q13 决定,不建独立测试项目)。
- **好测试的标准**:从用户视角验证可观察行为(窗口出现、五区可见、主题切换生效、布局重启后保留、热键呼出浮层、启动项被正确启动、关闭窗口后仍常驻),不测实现细节。
- **验收方式**:按本规格与 PRD 验收标准执行**手工冒烟清单**,逐条勾验:
  1. Workbench 呈现五区布局,亮/暗主题可切换,布局调整后重启仍恢复。
  2. Launcher 可增删改查启动项,搜索过滤生效。
  3. 全局热键呼出浮层,输入过滤、回车启动、Esc 关闭。
  4. 每项热键可绑定并生效。
  5. 关闭主窗口后托盘常驻,热键仍可用;启动日志与状态栏信息正确。
  6. 可执行启动项(NativeAOT 单文件,Direct2D 后端)发布并秒开。
- **未来缝**:启动项模型(JSON 往返、搜索过滤)与布局模型(JSON 往返)等纯逻辑出现后,如值得锁行为,再补单元测试;现阶段不建测试项目。

## Out of Scope

- 命令面板、设置页(v1.1)
- 开机自启开关(v1.1;可用启动文件夹快捷方式替代)
- 多平台后端(Linux/macOS)
- 运行时插件加载与 DI
- 自研停靠引擎
- MewUI 版本升级(需独立评审,ADR `000101-01`)

## Further Notes

- 术语与 Avoid 词见 `CONTEXT.md`(Workbench、Launcher、启动项、呼出浮层、五区色板等)。
- 停靠能力边界受 MewDock 约束(ADR `000101-02`);扩展以编译期注册接入(ADR `000101-03`)。
- 本 spec 即 v0.1.1 的 PRD;下一版本的需求(命令面板、设置页、开机自启)将进入新的 `.scratch/vX.Y.Z-<feature>/` 目录与新的 ADR 前缀。
