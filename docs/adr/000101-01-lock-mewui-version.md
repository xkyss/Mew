# 锁定 MewUI 包版本 0.19.1

MewUI 官方声明其公共 API 仍在稳定化过程中,次要版本之间可能出现破坏性变更,并明确建议生产应用锁定包版本。决定:本仓库所有 MewUI 相关包(`Aprillz.MewUI` 与 `Aprillz.MewUI.MewDock`)固定为 **0.19.1**,本地参考 clone 同步 checkout 到 `v0.19.1`;升级必须单独评审发布说明后再进行。

## Consequences

- 升级 MewUI 是一个显式决策,不会随依赖更新悄悄发生。
- 0.19.1 之后的新特性需等待下一次锁定评审。
