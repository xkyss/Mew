using System.IO.Pipes;
using System.Text.Json;

namespace Mew.Workbench.Ipc;

/// <summary>
/// 宿主侧 IPC 服务：管理注册的搜索源客户端，校验协议版本与能力越权，提供跨源搜索聚合。
/// 对应 spec 03：JSON-RPC over NamedPipe，宿主为 server，管道名含用户隔离。
/// 本实现提供内存态用于测试，NamedPipe 监听在 StartAsync 中可选启用。
/// </summary>
public sealed class IpcServer
{
    private readonly List<IpcClientHandle> _clients = [];
    private readonly object _lock = new();
    private readonly HotkeyService? _hotkeys;
    private readonly SettingsService? _settings;

    public IpcServer(HotkeyService? hotkeys = null, SettingsService? settings = null)
    {
        _hotkeys = hotkeys;
        _settings = settings;
    }

    public IReadOnlyList<IpcClientHandle> Clients
    {
        get { lock (_lock) return _clients.ToList(); }
    }

    /// <summary>注册客户端；协议版本不匹配或能力越权则返回错误。</summary>
    public RegisterAckMessage TryRegister(RegisterMessage msg)
    {
        if (msg.ProtocolVersion != IpcProtocol.CurrentVersion)
            return new RegisterAckMessage(false, $"协议版本不匹配（期望 {IpcProtocol.CurrentVersion}，实际 {msg.ProtocolVersion}）");

        lock (_lock)
        {
            if (_clients.Any(c => string.Equals(c.Id, msg.Id, StringComparison.OrdinalIgnoreCase)))
                return new RegisterAckMessage(false, $"id 重复：{msg.Id}");
        }

        // 能力越权在后续 Search/Settings/Hotkey 调用时拒绝，本票仅记录注册信息
        return new RegisterAckMessage(true, null);
    }

    /// <summary>测试或本地直连用：注册一个内存搜索源客户端。</summary>
    public bool RegisterInMemoryClient(string id, string displayName, int protocolVersion, PluginCapabilitiesDto? capabilities, ISearchSource? source, out string? error)
    {
        var ack = TryRegister(new RegisterMessage(id, displayName, capabilities, protocolVersion));
        if (!ack.Ok) { error = ack.Error; return false; }
        if (source != null && capabilities?.Search == null)
        {
            error = "未声明 search 能力，拒绝注册搜索源";
            return false;
        }
        lock (_lock) _clients.Add(new IpcClientHandle(id, displayName, protocolVersion, capabilities, source));
        error = null;
        return true;
    }

    /// <summary>由插件经 IPC 代理注册全局热键，集中冲突检测。</summary>
    public HotkeyRegisterAckMessage TryRegisterHotkey(string pluginId, string hotkey, string? label, PluginCapabilitiesDto? capabilities)
    {
        if (capabilities?.Hotkeys == null || capabilities.Hotkeys.All(h => h.Id != label && h.Default != hotkey))
        {
            // 若清单未声明任何 hotkeys 能力，拒绝
            if (capabilities?.Hotkeys == null)
                return new HotkeyRegisterAckMessage(false, "未声明 hotkeys 能力，拒绝注册");
        }
        if (_hotkeys == null) return new HotkeyRegisterAckMessage(false, "宿主热键服务不可用");
        // 使用宿主热键服务集中注册，回调经 IPC 转发至插件
        var ok = _hotkeys.Register(IntPtr.Zero, hotkey, () => { /* 触发后经 pipe 通知 */ }, label);
        if (!ok)
        {
            var owner = _hotkeys.FindOwner(hotkey);
            return new HotkeyRegisterAckMessage(false, owner is null ? "热键已被占用" : $"与{owner}冲突");
        }
        return new HotkeyRegisterAckMessage(true, null);
    }

    /// <summary>设置节注册校验：未声明 settingsSection 能力则拒绝。</summary>
    public bool CanRegisterSettingsSection(string pluginId, PluginCapabilitiesDto? capabilities, out string? error)
    {
        if (capabilities?.SettingsSection == null)
        {
            error = "未声明 settingsSection 能力，拒绝注册";
            return false;
        }
        error = null;
        return true;
    }

    public void HotkeyUnregister(string hotkey) => _hotkeys?.Unregister(hotkey);
    public bool IsHotkeyRegistered(string hotkey) => _hotkeys?.IsRegistered(hotkey) ?? false;
    public string? FindHotkeyOwner(string hotkey) => _hotkeys?.FindOwner(hotkey);

    public event Action<string>? ClientDisconnected;

    public void Unregister(string id)
    {
        bool removed;
        lock (_lock) removed = _clients.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) ClientDisconnected?.Invoke(id);
    }

    /// <summary>跨源搜索聚合：向所有已注册客户端扇出 search，收集结果后扁平混排。</summary>
    public IReadOnlyList<OverlayResultEntry> Search(string query, int globalCap = OverlaySearchAggregator.DefaultGlobalCap)
    {
        List<ISearchSource> sources;
        lock (_lock) sources = _clients.Select(c => c.Source).Where(s => s != null).Cast<ISearchSource>().ToList();
        // 复用既有聚合策略：每源上限 = 全局上限/源数
        return OverlaySearchAggregator.Aggregate(sources, query, globalCap);
    }

    /// <summary>跨源搜索（DTO 视角，供真实 IPC 管道序列化后使用）。</summary>
    public IReadOnlyList<SearchResultDto> SearchDto(string query, int globalCap = OverlaySearchAggregator.DefaultGlobalCap)
    {
        var entries = Search(query, globalCap);
        return entries.Select(e => new SearchResultDto(
            Guid.NewGuid().ToString("N"),
            e.Result.Title,
            e.Result.Subtitle,
            e.Source.Id,
            e.Source.DisplayName
        )).ToList();
    }

    // ---- NamedPipe 监听（宿主为 server） ----
    private CancellationTokenSource? _cts;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(IpcProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(ct);
                _ = Task.Run(() => HandlePipeClientAsync(server, ct), ct);
            }
            catch (OperationCanceledException) { server.Dispose(); break; }
            catch { server.Dispose(); await Task.Delay(200, ct); }
        }
    }

    private async Task HandlePipeClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            var reader = new StreamReader(pipe);
            var writer = new StreamWriter(pipe) { AutoFlush = true };
            string? line;
            IpcClientHandle? handle = null;
            try
            {
                line = await reader.ReadLineAsync(ct);
                if (line == null) return;
                var reg = JsonSerializer.Deserialize(line, IpcJsonContext.Default.RegisterMessage);
                if (reg == null) return;
                var ack = TryRegister(reg);
                if (!ack.Ok)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(ack, IpcJsonContext.Default.RegisterAckMessage));
                    return;
                }
                // 延迟创建 handle：pipe 客户端的搜索源通过远端调用，无本地 ISearchSource，标记为 pipe 客户端
                lock (_lock) _clients.Add(handle = new IpcClientHandle(reg.Id, reg.DisplayName, reg.ProtocolVersion, reg.Capabilities, new PipeSearchProxy(pipe, writer, reader, reg.Id, reg.DisplayName)));
                await writer.WriteLineAsync(JsonSerializer.Serialize(new RegisterAckMessage(true, null), IpcJsonContext.Default.RegisterAckMessage));
                // 处理后续消息：热键注册、设置变更等（搜索由 server 侧主动发 SearchRequest，经 PipeSearchProxy 处理）
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var type = doc.RootElement.GetProperty("type").GetString();
                        if (type == "hotkeyRegister")
                        {
                            var msg = JsonSerializer.Deserialize(line, IpcJsonContext.Default.HotkeyRegisterMessage);
                            if (msg != null)
                            {
                                if (handle.Capabilities?.Hotkeys == null)
                                {
                                    var nack = new HotkeyRegisterAckMessage(false, "未声明 hotkeys 能力，拒绝注册");
                                    await writer.WriteLineAsync(JsonSerializer.Serialize(nack, IpcJsonContext.Default.HotkeyRegisterAckMessage));
                                }
                                else
                                {
                                    var label = msg.Label ?? msg.Hotkey;
                                    var ok = _hotkeys != null && _hotkeys.Register(IntPtr.Zero, msg.Hotkey, () =>
                                    {
                                        try { writer.WriteLine(JsonSerializer.Serialize(new HotkeyTriggeredMessage(msg.Hotkey, msg.PluginId), IpcJsonContext.Default.HotkeyTriggeredMessage)); } catch { }
                                    }, label);
                                    HotkeyRegisterAckMessage ack2;
                                    if (ok) ack2 = new HotkeyRegisterAckMessage(true, null);
                                    else
                                    {
                                        var owner = _hotkeys?.FindOwner(msg.Hotkey);
                                        ack2 = new HotkeyRegisterAckMessage(false, owner is null ? "热键已被占用" : $"与{owner}冲突");
                                    }
                                    await writer.WriteLineAsync(JsonSerializer.Serialize(ack2, IpcJsonContext.Default.HotkeyRegisterAckMessage));
                                }
                            }
                        }
                        else if (type == "settingsChanged")
                        {
                            // 设置变更推送，暂仅记录日志，不持久化（由宿主 SettingsService 处理）
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                if (handle != null) Unregister(handle.Id);
            }
        }
    }

    private sealed class PipeSearchProxy : ISearchSource
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        public PipeSearchProxy(NamedPipeServerStream pipe, StreamWriter writer, StreamReader reader, string id, string displayName) { _pipe = pipe; _writer = writer; _reader = reader; Id = id; DisplayName = displayName; }
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<SearchResult> Search(string query, int maxResults)
        {
            try
            {
                var reqId = Guid.NewGuid().ToString("N");
                var req = new SearchRequestMessage(reqId, query, maxResults);
                var json = JsonSerializer.Serialize(req, IpcJsonContext.Default.SearchRequestMessage);
                lock (_writer) { _writer.WriteLine(json); _writer.Flush(); }
                // 简化：同步等待响应（带超时）
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                while (!cts.IsCancellationRequested)
                {
                    if (_reader.ReadLine() is string line && !string.IsNullOrWhiteSpace(line))
                    {
                        var resp = JsonSerializer.Deserialize(line, IpcJsonContext.Default.SearchResponseMessage);
                        if (resp != null && resp.RequestId == reqId)
                            return resp.Results.Select(dto => new SearchResult(dto.Title, dto.Subtitle, null, () => { })).ToList();
                    }
                }
            }
            catch { }
            return [];
        }
    }
}

public sealed class IpcClientHandle
{
    public IpcClientHandle(string id, string displayName, int protocolVersion, PluginCapabilitiesDto? capabilities, ISearchSource? source)
    {
        Id = id;
        DisplayName = displayName;
        ProtocolVersion = protocolVersion;
        Capabilities = capabilities;
        Source = source;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public int ProtocolVersion { get; }
    public PluginCapabilitiesDto? Capabilities { get; }
    public ISearchSource? Source { get; }
}
