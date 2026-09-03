# Mew

个人自用桌面 UI 框架仓库：在 `MewUI` 之上构建可复用的 `Workbench` 主框架与示例工具 `Launcher`，`v0.2.1` 起为三层插件宿主（`Mew.Host(AOT)` + `Mew.PluginHost(JIT)` + 独立插件）。

## 快速开始

```bash
dotnet build Mew.slnx
dotnet run --project src/Mew.Host/Mew.Host.csproj   # 宿主自动拉起扩展主机
```

发布双 exe：

```bash
dotnet publish src/Mew.Host -c Release -r win-x64 /p:PublishAot=true -o publish
dotnet publish src/Mew.PluginHost -c Release -r win-x64 -o publish
```

详见 [`docs/run.md`](docs/run.md)（运行/发布/插件放置/设置/热键/浮层/测试/排障）。

## 文档

- 架构：`docs/adr/000201-01-three-layer-plugin-host.md`（三层宿主）、`docs/adr/000200-01-host-tool-modules.md`
- 规格：`.scratch/v0.2.1-three-layer-plugin-host/spec.md`
- 术语：`CONTEXT.md`
- 运行：`docs/run.md`
