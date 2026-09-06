using System.IO.Pipes;
using System.Text.Json;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;

namespace Mew.PluginHost;

/// <summary>
/// 独立插件（T3）行拉取/动作一次性 IPC（ADR-000303）：短连接发 pluginRowsRequest / pluginAdminAction、收 ack 即关。
/// 行权威在宿主（引擎实态 + plugins.json）；连接与读回都有 2s 超时，宿主不可达时明确报错不冻结扩展主界面。
/// ack 报文映射留在本层（沿用 ADR-000204 决策 3），adapter 只见 PluginAdminResult。
/// </summary>
internal static class HostPluginAdminIpc
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    /// <summary>拉取宿主托管的独立插件行；宿主不可达或无法提供（含明确拒绝）返回 null（transportError 说明原因）。</summary>
    public static IReadOnlyList<PluginAdminRow>? TryFetchRows(out string? transportError, string? pipeName = null)
    {
        transportError = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
            var writer = new StreamWriter(pipe) { AutoFlush = true };
            var reader = new StreamReader(pipe);
            writer.WriteLine(JsonSerializer.Serialize(new PluginRowsRequestMessage(), IpcJsonContext.Default.PluginRowsRequestMessage));
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

            var ack = JsonSerializer.Deserialize(line, IpcJsonContext.Default.PluginRowsAckMessage);
            if (ack is null)
            {
                transportError = "宿主应答为空";
                return null;
            }
            if (ack.Error is not null)
            {
                // 宿主侧无法提供行（无处理器等）：按不可用降级，让页面呈现警示而非空态误导
                transportError = ack.Error;
                return null;
            }
            return ack.Rows.Select(dto => dto.ToRow()).ToList();
        }
        catch (Exception ex)
        {
            transportError = ex.Message;
            return null;
        }
    }

    /// <summary>发独立插件行内动作并映射为面板结果：Ok = 宿主确认执行、Rejected = 宿主拒绝、Unreachable = 不可达/超时。</summary>
    public static PluginAdminResult TryAction(string id, PluginRowAction action, string? pipeName = null)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
            var writer = new StreamWriter(pipe) { AutoFlush = true };
            var reader = new StreamReader(pipe);
            writer.WriteLine(JsonSerializer.Serialize(new PluginAdminActionMessage(id, action.ToString()), IpcJsonContext.Default.PluginAdminActionMessage));
            string? line;
            try
            {
                using var timeout = new CancellationTokenSource(ReadTimeout);
                line = reader.ReadLineAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return PluginAdminResult.Unreachable("宿主响应超时");
            }
            if (line is null)
                return PluginAdminResult.Unreachable("宿主无响应");

            var ack = JsonSerializer.Deserialize(line, IpcJsonContext.Default.PluginAdminActionAckMessage);
            if (ack is null)
                return PluginAdminResult.Unreachable("宿主应答无法解析");
            return ack.Ok ? PluginAdminResult.Ok() : PluginAdminResult.Rejected(ack.Error ?? "宿主拒绝");
        }
        catch (Exception ex)
        {
            return PluginAdminResult.Unreachable(ex.Message);
        }
    }
}

/// <summary>
/// 宿主代理 adapter（ADR-000303）：扩展主机设置→插件页的 T3 行来源。
/// Snapshot = 最近一次拉取的行缓存（重绘不重拉，避免 UI 线程反复打管道）；Apply = 一次性 IPC 转发宿主执行。
/// </summary>
internal sealed class HostProxyPluginAdminService : IPluginAdminService
{
    private IReadOnlyList<PluginAdminRow> _rows = [];

    /// <summary>用宿主拉取结果替换行缓存；页面每次重建时调用一次。</summary>
    public void SetRows(IReadOnlyList<PluginAdminRow> rows) => _rows = rows;

    public IReadOnlyList<PluginAdminRow> Snapshot() => _rows;

    public PluginAdminResult Apply(string id, PluginRowAction action) => HostPluginAdminIpc.TryAction(id, action);
}
