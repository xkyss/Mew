using System.IO.Pipes;
using System.Text.Json;
using Mew.Workbench.Ipc;

namespace Mew.PluginHost;

/// <summary>
/// 插件启用/禁用一次性 IPC（ADR-000203 单写者）：短连接发 pluginEnableSet 请求宿主落盘、收 ack 即关。
/// 扩展主机不再本地写 `plugins.json`；宿主不可达时明确报错，不回退本地写。
/// </summary>
internal static class EnablePluginIpc
{
    public static PluginEnableSetAckMessage? TrySet(string id, bool enabled, out string? transportError)
    {
        transportError = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            using var reader = new StreamReader(pipe);
            writer.WriteLine(JsonSerializer.Serialize(new PluginEnableSetMessage(id, enabled), IpcJsonContext.Default.PluginEnableSetMessage));
            var line = reader.ReadLine();
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
