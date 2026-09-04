using System.IO.Pipes;
using System.Text.Json;
using Mew.Workbench.Ipc;

namespace Mew.PluginHost;

/// <summary>呼出热键一次性 IPC：短连接发 overlayHotkeySet、收 ack 即关，不占用长连接搜索通道。</summary>
internal static class OverlayHotkeyIpc
{
    public static OverlayHotkeySetAckMessage? TrySet(string? hotkey, bool enabled, out string? transportError)
    {
        transportError = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            using var reader = new StreamReader(pipe);
            writer.WriteLine(JsonSerializer.Serialize(new OverlayHotkeySetMessage(hotkey, enabled), IpcJsonContext.Default.OverlayHotkeySetMessage));
            var line = reader.ReadLine();
            if (line is null)
            {
                transportError = "宿主无响应";
                return null;
            }

            return JsonSerializer.Deserialize(line, IpcJsonContext.Default.OverlayHotkeySetAckMessage);
        }
        catch (Exception ex)
        {
            transportError = ex.Message;
            return null;
        }
    }
}
