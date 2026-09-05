using Mew.PluginHost;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 扩展主机侧插件管理 adapter（ADR-000204 票据 03）：DLL 行来源、启用态取本地只读缓存、
/// 实态标注；写路径三态（宿主确认 / 宿主拒绝 / 不可达），本地缓存仅在确认后内存同步。
/// 传输以假 delegate 驱动，无真实管道。
/// </summary>
public class ExtensionHostPluginAdminServiceTests
{
    private sealed class FakeTransport
    {
        public List<(string Id, bool Enabled)> Calls { get; } = [];
        public PluginAdminResult Result { get; set; } = PluginAdminResult.Ok();

        public EnableTransport Delegate => (string id, bool enabled, out string? transportError) =>
        {
            Calls.Add((id, enabled));
            transportError = null;
            return Result;
        };
    }

    private static PluginDescriptor Descriptor(string id, string entryType = "dll")
    {
        var manifest = new PluginManifest
        {
            Id = id,
            DisplayName = id + "-disp",
            Version = "2.0.0",
            Entry = new PluginEntry { Type = entryType, Path = "plug" },
            ProtocolVersion = 1,
        };
        return new PluginDescriptor(manifest, $"/tmp/{id}/plugin.json", []);
    }

    private static (ExtensionHostPluginAdminService Service, PluginEnableStore Store, FakeTransport Transport) Build(
        List<PluginDescriptor>? descs = null, List<string>? loadedIds = null)
    {
        var store = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-host-tests", Guid.NewGuid().ToString("N") + "-ph-plugins.json"));
        var transport = new FakeTransport();
        var service = new ExtensionHostPluginAdminService(descs ?? [], store,
            () => loadedIds ?? [], transport.Delegate);
        return (service, store, transport);
    }

    [Fact]
    public void Snapshot_只列DLL行_启用态与实态标注正确()
    {
        List<PluginDescriptor> descs = [Descriptor("alpha"), Descriptor("beta"), Descriptor("t3app", entryType: "exe")];
        var (service, store, _) = Build(descs, loadedIds: ["beta"]);
        store.SetEnabled("alpha", false);

        var rows = service.Snapshot();

        Assert.Equal(["alpha", "beta"], rows.Select(r => r.Id).ToArray()); // exe 行不出现
        var alpha = rows.Single(r => r.Id == "alpha");
        Assert.Equal(PluginRowStatus.Disabled, alpha.State.Status);
        // 禁用但本会话未加载（alpha 未装载）：无副提示
        Assert.Null(alpha.State.Hint);
        var beta = rows.Single(r => r.Id == "beta");
        Assert.Equal(PluginRowStatus.Enabled, beta.State.Status);
        Assert.Null(beta.State.Hint);
    }

    [Fact]
    public void Snapshot_禁用且本会话仍加载_副提示出现()
    {
        List<PluginDescriptor> descs = [Descriptor("alpha")];
        var (service, store, _) = Build(descs, loadedIds: ["alpha"]);
        store.SetEnabled("alpha", false);

        var row = Assert.Single(service.Snapshot());

        Assert.Equal("已禁用，重启扩展主机后生效", row.State.Hint);
    }

    [Fact]
    public void Apply_Enable_宿主确认_本地缓存内存同步_不改盘()
    {
        var (service, store, transport) = Build();
        transport.Result = PluginAdminResult.Ok();

        var result = service.Apply("alpha", PluginRowAction.Enable);

        Assert.Equal(PluginAdminOutcome.Ok, result.Outcome);
        Assert.Equal([("alpha", true)], transport.Calls);
        Assert.True(store.IsEnabled("alpha"));
        Assert.False(File.Exists(store.FilePath)); // 扩展主机永不本地落盘
    }

    [Fact]
    public void Apply_宿主拒绝_透传原因_缓存不动()
    {
        var (service, store, transport) = Build();
        transport.Result = PluginAdminResult.Rejected("未知插件：alpha");

        var result = service.Apply("alpha", PluginRowAction.Disable);

        Assert.Equal(PluginAdminOutcome.Rejected, result.Outcome);
        Assert.Equal("未知插件：alpha", result.Error);
        Assert.True(store.IsEnabled("alpha")); // 缺省启用语义未被改变
    }

    [Fact]
    public void Apply_宿主不可达_透传传输错误_缓存不动()
    {
        var (service, store, transport) = Build();
        transport.Result = PluginAdminResult.Unreachable("宿主无响应");

        var result = service.Apply("alpha", PluginRowAction.Disable);

        Assert.Equal(PluginAdminOutcome.Unreachable, result.Outcome);
        Assert.Equal("宿主无响应", result.Error);
        Assert.True(store.IsEnabled("alpha"));
    }

    [Fact]
    public void Apply_重启动作_拒绝()
    {
        var (service, _, transport) = Build();

        var result = service.Apply("alpha", PluginRowAction.Restart);

        Assert.Equal(PluginAdminOutcome.Rejected, result.Outcome);
        Assert.Empty(transport.Calls);
    }
}

/// <summary>
/// EnablePluginIpc 真管道回环与读超时（ADR-000204 票据 03）：独立管道名，不与宿主的真实管道冲突。
/// </summary>
public class EnablePluginIpcTimeoutTests
{
    private static string UniquePipeName() => $"mew-host-tests-{Guid.NewGuid():N}";

    [Fact]
    public async Task TrySet_宿主回ack_正常收到确认()
    {
        var pipeName = UniquePipeName();
        using var server = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut,
            1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        using var acceptTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 客户端先发（生产时序：TrySet 与宿主并发），服务端随后接受并回 ack
        var trySetTask = Task.Run(() =>
        {
            var ack = EnablePluginIpc.TrySet("alpha", true, out var transportError, pipeName);
            return (Ack: ack, TransportError: transportError);
        });
        await server.WaitForConnectionAsync(acceptTimeout.Token);
        using var reader = new StreamReader(server);
        await reader.ReadLineAsync(acceptTimeout.Token);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(
            new PluginEnableSetAckMessage(true, null, "alpha", true), IpcJsonContext.Default.PluginEnableSetAckMessage));

        var (ack, transportError) = await trySetTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(ack is not null, transportError ?? "(无 transportError)");
        Assert.True(ack.Ok);
        Assert.Null(transportError);
    }

    [Fact]
    public async Task TrySet_宿主接受连接但不回话_读超时按不可达处理()
    {
        var pipeName = UniquePipeName();
        using var server = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut,
            1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        using var acceptTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var trySetTask = Task.Run(() =>
        {
            var ack = EnablePluginIpc.TrySet("alpha", true, out var transportError, pipeName);
            return (Ack: ack, TransportError: transportError);
        });
        await server.WaitForConnectionAsync(acceptTimeout.Token);
        using var reader = new StreamReader(server);
        await reader.ReadLineAsync(acceptTimeout.Token); // 收请求但不回话，客户端读超时

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var (ack, transportError) = await trySetTask.WaitAsync(TimeSpan.FromSeconds(10));

        stopwatch.Stop();
        Assert.Null(ack);
        Assert.Equal("宿主响应超时", transportError);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(8));
    }
}
