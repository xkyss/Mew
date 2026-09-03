using System.IO.Pipes;
using System.Text.Json;

namespace Mew.Workbench.Ipc;

/// <summary>
/// 插件侧 IPC 客户端：支持内存直连（测试）与 NamedPipe 连接（生产）。
/// </summary>
public sealed class IpcClient
{
    private readonly IpcServer? _server;
    private readonly ISearchSource _source;
    private readonly string _id;
    private readonly string _displayName;
    private readonly int _protocolVersion;
    private readonly PluginCapabilitiesDto? _capabilities;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private CancellationTokenSource? _cts;

    // 内存直连构造（测试）
    public IpcClient(IpcServer server, ISearchSource source, string id, string displayName, int protocolVersion = 1, PluginCapabilitiesDto? capabilities = null)
    {
        _server = server;
        _source = source;
        _id = id;
        _displayName = displayName;
        _protocolVersion = protocolVersion;
        _capabilities = capabilities ?? new PluginCapabilitiesDto(new SearchCapabilityDto(id, displayName), null, null);
    }

    // 管道构造（生产）：无需 server 实例
    public IpcClient(ISearchSource source, string id, string displayName, int protocolVersion = 1, PluginCapabilitiesDto? capabilities = null)
    {
        _source = source;
        _id = id;
        _displayName = displayName;
        _protocolVersion = protocolVersion;
        _capabilities = capabilities ?? new PluginCapabilitiesDto(new SearchCapabilityDto(id, displayName), null, null);
    }

    public bool Connect(out string? error)
    {
        if (_server != null)
            return _server.RegisterInMemoryClient(_id, _displayName, _protocolVersion, _capabilities, _source, out error);
        // 管道模式：尝试连接宿主
        try
        {
            _pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipe.Connect(2000);
            _writer = new StreamWriter(_pipe) { AutoFlush = true };
            _reader = new StreamReader(_pipe);
            var reg = new RegisterMessage(_id, _displayName, _capabilities, _protocolVersion);
            _writer.WriteLine(JsonSerializer.Serialize(reg, IpcJsonContext.Default.RegisterMessage));
            var ackLine = _reader.ReadLine();
            var ack = ackLine != null ? JsonSerializer.Deserialize(ackLine, IpcJsonContext.Default.RegisterAckMessage) : null;
            if (ack == null || !ack.Ok) { error = ack?.Error ?? "管道注册无响应"; return false; }
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenLoopAsync(_cts.Token));
            error = null;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public void Disconnect()
    {
        if (_server != null) { _server.Unregister(_id); return; }
        try { _cts?.Cancel(); } catch { }
        try { _pipe?.Dispose(); } catch { }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        if (_reader == null || _writer == null) return;
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await _reader.ReadLineAsync(ct); } catch { break; }
            if (line == null) break;
            try
            {
                var req = JsonSerializer.Deserialize(line, IpcJsonContext.Default.SearchRequestMessage);
                if (req != null)
                {
                    var results = _source.Search(req.Query, req.MaxResults);
                    var dtos = results.Select(r => new SearchResultDto(Guid.NewGuid().ToString("N"), r.Title, r.Subtitle, _id, _displayName)).ToList();
                    var resp = new SearchResponseMessage(req.RequestId, dtos);
                    await _writer.WriteLineAsync(JsonSerializer.Serialize(resp, IpcJsonContext.Default.SearchResponseMessage));
                }
            }
            catch { }
        }
    }
}
