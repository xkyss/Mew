# NativeAOT 是 v1 发布目标

Launcher 以「热键一按、浮层秒开」为核心体验,且需常驻托盘。决定:v1 即以 **NativeAOT 单文件 exe** 为发布目标(`win-x64` + Direct2D + 完整裁剪),框架库从设计上保持 AOT/Trim 兼容(JSON 使用 System.Text.Json 源生成、避免运行时反射)。

## Considered Options

- **自包含 JIT**:发布简单,但冷启动明显更慢,与浮层体验目标冲突。
- **NativeAOT(选定)**:启动快、体积小;MewUI 官方以 NativeAOT 为首要目标,风险可控。

## Consequences

- 编码约束:避免运行时反射与未裁剪 API;若 MewUI 在 AOT 下暴露问题,退化为自包含单文件发布。
