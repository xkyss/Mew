## Agent skills

### Issue tracker

Issues and specs for this repo live as local markdown files under `.scratch/<feature>/`.

`<feature>` = `<版本前缀>-<功能名>`,如 `v0.1.1-workbench-launcher`。每个目录下的 `spec.md` 即该版本的 PRD(不再使用 `docs/prd/`)。

See `docs/agents/issue-tracker.md`.

### Triage labels

Triage uses the default label vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` + `docs/adr/` at the repo root.

`adr` with prefix of version, like `000101`.

版本编码规则:ADR 前缀 = 6 位版本号(major/minor/patch 各 2 位补零),同版本多条加 `-NN` 序号;ADR 与 issue tracker 目录按版本对应(如 `000101` ↔ `v0.1.1`)。

See `docs/agents/domain.md`.

### Version bump

运行时版本号唯一来源是 `src/Mew.Host/MewHost.cs` 里的常量:

```csharp
private const string AppVersion = "v0.2.0";
```

该常量用于主窗口标题(`Mew Launcher — {AppVersion}`)与关于对话框(`Mew Launcher {AppVersion}`)。v0.2.0 起宿主为 `Mew.Host`(exe),模块 `Mew.Launcher` 不再持有版本号。

提升版本号时:

- **只需修改这一行常量**;csproj 无 `<Version>`/`<AssemblyVersion>`,全仓无 AssemblyInfo,`Mew.slnx` 不含版本,均无需改动。
- `.scratch/` 与 `docs/adr/` 中的旧版本号是已归档的 spec/验收记录/ADR 前缀,不回改。
- 历史先例:版本提升提交为单文件单行改动,如 `6e02e1a`(v0.1.3)、`ba2885e`(v0.1.4)。

开启下一个版本迭代(`vX.Y.Z`)时,除改常量外还需按 issue tracker / Domain docs 约定新建:

1. `.scratch/<版本前缀>-<功能名>/spec.md`(该版本 PRD)
2. `docs/adr/000XYZ-NN-*.md`(ADR 前缀 = 6 位版本号,如 `000105` ↔ `v0.1.5`)

### Git conventions

Commit messages follow Conventional Commits 1.0.0 with repo-local Chinese message rules. See `docs/agents/git-conventions.md`.
