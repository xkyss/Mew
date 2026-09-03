# 宿主独立启动：常驻干净 + 插件分层代管

> **一句话定调：Host 只管常驻与 T3，dll 全权委托 PluginHost；名单归 Host，执行分开。**

v0.2.1 的三层拆分（ADR-000201）在代码里没落到底：`MewHost` 首启即 `EnsurePluginHostRunning()`、退出时 `Kill()`、崩溃 1s 后自动拉起，`ShowMain()` 与 `Loaded` 都顺手拉起；且 Host 与 PluginHost 各调一次 `PluginDiscovery.Discover()`，同一份 `plugin.json` 两边各扫一遍。结果是常驻不干净——调 Host 必弹五区，单 Host 分发、无五区场景、崩溃隔离的验收都写不出来。本 ADR 把独立启动与代管边界钉死。

## Considered Options

- **维持绑定启动（首启即拉起 + 退出 Kill + 崩溃自愈）**：五区首屏最快，用户一次点击即见 Launcher；但 Host 常驻被五区进程污染，调试 Host、单 AOT 分发、崩溃隔离都无从谈起，与 ADR-000201“崩溃不影响常驻”矛盾。→ 未选。
- **Host 收 exe+dll，PluginHost 收 T1+dll（双重加载）**：物理上不可行——Host 是 NativeAOT（ADR-000101-04），`AssemblyLoadContext.LoadFrom` 装不下托管 dll；且同一 dll 两边谁 `Configure()` 会脑裂。→ 未选。
- **PluginHost 发 `plugin.json` 与 T3 平起平坐（可禁用）**：一旦禁用，五区+T1 无家可归；底座不应是可插拔项。→ 未选。
- **协议彻底分叉（含 `protocolVersion` 另起炉灶）**：清单与 IPC 各搞一套，T2/T3 永不互通；但 `PluginManifest`/`IpcProtocol.CurrentVersion` 已是同一套且够用，分叉只增维护成本。→ 未选。
- **独立启动 + 分层代管（选定）**：见下。

## 决策

同一源码双发布的前提不变（分裂线是 AOT 秒开常驻 vs JIT 可扩展 + 故障隔离，T1/T2/T3 只是后果）。本 ADR 只定启动与管辖：

```
Host (Mew.Host.exe, AOT)            PluginHost (Mew.PluginHost.exe, JIT)
常驻、轻                             重、可扩展
• 托盘 + 呼出浮层 + 全局热键          • Workbench 五区 + 主题/布局
• 设置根节 + 持久化                   • T1 编译期工具模块（写死 Configure）
• 插件发现唯一来源                    • T2 dll 代管执行（ALC，按名单加载）
  （扫目录/读清单/写 plugins.json）     （不再自扫，被动接收 Host 名单）
• exe(T3) 生命周期                     • 搜索/设置/热键经 IPC 代理回 Host
  （Process.Start/探活/标崩溃）        • 关闭 = Hide()，进程保留
```

1. **常驻干净**：Host 首启不再 `EnsurePluginHostRunning()`，`ShowMain()` 不再顺手拉起，退出不再 `Kill()`，`OnPluginHostExited` 不再 1s 自动拉起，仅 Toast + 日志标“已崩溃/已退出”。任一端崩溃不影响另一端。
2. **分工**：Host 只收 `entry.type=exe`（T3 瘦插件，走 NamedPipe JSON-RPC）；PluginHost 收 T1 + `entry.type=dll`（T2 富插件，走 `IMewToolModule.Configure()` + ALC）。`RequiresJit` 的项在 Host 侧置灰（复用 `PluginDescriptor.Health(isJitAvailable)`），只在 PluginHost 的设置→插件里可管。
3. **代管边界**：发现 + `plugins.json` 启用态只留 Host 一份；PluginHost 去掉 `Discover`，改为接收 Host 推过来的待装 dll 清单。清单 schema（`PluginManifest.Validate()`）与 `protocolVersion` 不分叉；执行面天然不同——exe 只能声明式三能力（search/settingsSection/hotkeys，越权在 register 阶段拒绝），dll/T1 可直拼五区。
4. **身份**：PluginHost 是特殊容器（Layer2），不进插件列表、不允许禁用；Host 保留“打开/重启扩展主机”手动按钮。
5. **手动入口**：Host 首启直接隐藏到托盘（仅托盘与浮层常驻）；托盘左键呼出搜索浮层，右键“打开扩展主机/重启扩展主机”手动拉起；失败告警（热键被占、扩展主机缺失）会亮出主窗口，余时主窗口不出现。

## Consequences

- `MewHost.cs`：删 `Loaded` 中的 `EnsurePluginHostRunning()+Hide`、`ShowMain` 中的 `Ensure`、`Quit`/结尾的 `Kill`、`OnPluginHostExited` 的自愈；`Ensure/Restart` 仅由两个手动按钮与托盘项触发；崩溃走 `_crashedPlugins` 标记。
- `PluginHostApp.cs`：删 `PluginDiscovery.Discover` 自扫，改为接收 Host 名单；`Closing` 保持 `Cancel+Hide()` 语义（隐藏≠退出）；设置→插件页只列 T1+dll。
- `TrayIcon.cs`：无“显示主窗口”项；左键呼出浮层（空动作无操作），右键“打开扩展主机/重启扩展主机/退出”。
- **隐藏语义钉死**：关五区窗口只是藏，进程保留秒显；真退出只走杀进程（Host 退出/重启按钮、PluginHost 内重启项）。
- **一句话记忆**：exe 生死 Host 全权，dll 生死 PluginHost 全权，dll 看不看得见 Host 说了算。
- 术语不变（CONTEXT.md 的插件=T2+T3、工具模块=T1/T2、扩展主机=Layer2、独立插件=T3 均继续有效）；本 ADR 是 000201 的启动/管辖补丁，不推翻 AOT 范围（000101-04）与编译期组合（000101-03）。
