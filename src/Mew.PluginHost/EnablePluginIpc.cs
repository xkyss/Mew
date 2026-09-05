using System.IO.Pipes;
using System.Text.Json;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;

namespace Mew.PluginHost;

/// <summary>
/// 插件启用/禁用一次性 IPC（ADR-000203 单写者）：短连接发 pluginEnableSet 请求宿主落盘、收 ack 即关。
/// 扩展主机不再本地写 `plugins.json`；宿主不可达时明确报错，不回退本地写。
/// 连接与读回都有 2s 超时：宿主接受连接后不回话时按不可达处理，不冻结扩展主界面（ADR-000204）。
/// ack 报文映射留在本层（ADR-000204 决策 3），adapter 只见 PluginAdminResult。
/// </summary>
internal static class EnablePluginIpc
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    /// <summary>管理缝入口（ADR-000204）：发请求并映射为面板结果；Out = 宿主确认（含落盘）、Rejected = 宿主拒绝、Unreachable = 不可达/超时。</summary>
    public static PluginAdminResult TryRequest(string id, bool enabled, out string? transportError)
    {
        var ack = TrySet(id, enabled, out transportError);
        if (ack is { Ok: true }) return PluginAdminResult.Ok();
        if (ack is { Error: not null }) return PluginAdminResult.Rejected(ack.Error);
        return PluginAdminResult.Unreachable(transportError ?? "宿主无响应");
    }

    public static PluginEnableSetAckMessage? TrySet(string id, bool enabled, out string? transportError, string? pipeName = null)
    {
        transportError = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
            // writer/reader 不各自 dispose：两者共享同一条管道，先关其一会令另一个的清理在成功路径上抛 ObjectDisposedException 吞掉 ack
            var writer = new StreamWriter(pipe) { AutoFlush = true };
            var reader = new StreamReader(pipe);
            writer.WriteLine(JsonSerializer.Serialize(new PluginEnableSetMessage(id, enabled), IpcJsonContext.Default.PluginEnableSetMessage));
            string? line;
            try
            {
                using var timeout = new CancellationTokenSource(ReadTimeout);
                line = reader.ReadLineAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                transportError = "宿主响应超时";
                return null;
            }
            if (line is null)
            {
                transportError = "宿主无响应";
                return null;
            }

            return JsonSerializer.Deserialize(line, IpcJsonContext.Default.PluginEnableSetAckMessage);
        }
        catch (Exception ex)
        {
            transportError = ex.Message;
            return null;
        }
    }
}
