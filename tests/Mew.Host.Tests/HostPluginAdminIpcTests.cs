using System.Text.Json;
using Mew.PluginHost;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 独立插件（T3）行拉取/动作 IPC 缝测试（ADR-000303）：设置→插件页经一次性管道向宿主拉行、发动作。
/// 与 PluginEnableIpcTests 同构：断言无宿主处理器时明确降级、有处理器时透传、行 DTO JSON 往返保真、
/// 宿主代理 adapter 的行缓存与动作转发语义。
/// </summary>
public class HostPluginAdminIpcTests
{
    private static PluginAdminRow Row(string id, PluginRowState? state = null)
    {
        var manifest = new PluginManifest
        {
            Id = id,
            DisplayName = id + "-disp",
            Version = "1.2.0",
            Entry = new PluginEntry { Type = "exe", Path = "plug.exe" },
            ProtocolVersion = 1,
        };
        var descriptor = new PluginDescriptor(manifest, $"/tmp/{id}/plugin.json", ["校验错误示例"]);
        return new PluginAdminRow(descriptor, state ?? PluginRowState.Derive(enabled: true, PluginHealth.Healthy, crashed: false, stillLoaded: false));
    }

    // ---- IpcServer 行拉取入口 ----

    [Fact]
    public void TryFetchPluginRows_无处理器_返回错误且空行()
    {
        var server = new IpcServer();

        var ack = server.TryFetchPluginRows();

        Assert.Empty(ack.Rows);
        Assert.Contains("不支持", ack.Error!);
    }

    [Fact]
    public void TryFetchPluginRows_有处理器_透传宿主行()
    {
        var server = new IpcServer(null, null, null, null,
            () => new PluginRowsAckMessage([PluginAdminRowDto.FromRow(Row("alpha"))], null));

        var ack = server.TryFetchPluginRows();

        Assert.Null(ack.Error);
        var row = Assert.Single(ack.Rows);
        Assert.Equal("alpha", row.ToRow().Id);
    }

    // ---- IpcServer 动作入口 ----

    [Fact]
    public void TryPluginAction_无处理器_明确拒绝()
    {
        var server = new IpcServer();

        var ack = server.TryPluginAction("alpha", "restart");

        Assert.False(ack.Ok);
        Assert.Contains("不支持", ack.Error!);
        Assert.Equal("alpha", ack.Id);
        Assert.Equal("restart", ack.Action);
    }

    [Fact]
    public void TryPluginAction_有处理器_透传id与动作()
    {
        string? seenId = null;
        string? seenAction = null;
        var server = new IpcServer(null, null, null, null, null,
            (id, action) =>
            {
                seenId = id;
                seenAction = action;
                return new PluginAdminActionAckMessage(true, null, id, action);
            });

        var ack = server.TryPluginAction("alpha", "restart");

        Assert.True(ack.Ok);
        Assert.Equal("alpha", seenId);
        Assert.Equal("restart", seenAction);
    }

    // ---- 行 DTO 往返 ----

    [Fact]
    public void PluginAdminRowDto_行到DTO到行_字段保真()
    {
        var original = Row("alpha", new PluginRowState(PluginRowStatus.Crashed, PluginRowAction.Restart, "进程异常退出，点击重启重新拉起"));

        var back = PluginAdminRowDto.FromRow(original).ToRow();

        Assert.Equal(original.Id, back.Id);
        Assert.Equal(original.Title, back.Title);
        Assert.Equal(original.Descriptor.ManifestPath, back.Descriptor.ManifestPath);
        Assert.Equal(original.Descriptor.ValidationErrors, back.Descriptor.ValidationErrors);
        Assert.Equal(original.State.Status, back.State.Status);
        Assert.Equal(original.State.Action, back.State.Action);
        Assert.Equal(original.State.Hint, back.State.Hint);
    }

    [Fact]
    public void PluginAdminRowDto_JSON往返_类型保留()
    {
        var json = JsonSerializer.Serialize(PluginAdminRowDto.FromRow(Row("beta")), IpcJsonContext.Default.PluginAdminRowDto);
        Assert.Contains("\"entry\"", json);

        var back = JsonSerializer.Deserialize(json, IpcJsonContext.Default.PluginAdminRowDto)!;
        Assert.Equal("beta", back.ToRow().Id);
    }

    [Theory]
    [InlineData("enable", PluginRowAction.Enable)]
    [InlineData("Restart", PluginRowAction.Restart)]
    public void TryParseAction_合法动作名_解析成功(string text, PluginRowAction expected)
    {
        Assert.True(PluginAdminRowDto.TryParseAction(text, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("bogus")]
    [InlineData("")]
    public void TryParseAction_非法动作名_拒绝(string text)
    {
        Assert.False(PluginAdminRowDto.TryParseAction(text, out _));
    }

    // ---- 宿主代理 adapter ----

    [Fact]
    public void HostProxyPluginAdminService_Snapshot_返回最近一次行缓存()
    {
        var service = new HostProxyPluginAdminService();
        Assert.Empty(service.Snapshot());

        service.SetRows([Row("alpha"), Row("beta")]);

        Assert.Equal(["alpha", "beta"], service.Snapshot().Select(r => r.Id).ToArray());
    }

    [Fact]
    public void HostProxyPluginAdminService_Apply_宿主不可达_返回Unreachable()
    {
        var service = new HostProxyPluginAdminService();

        // 默认管道无监听方：连接即失败，按不可达处理（不抛出、不冻结）
        var result = service.Apply("alpha", PluginRowAction.Restart);

        Assert.Equal(PluginAdminOutcome.Unreachable, result.Outcome);
        Assert.NotNull(result.Error);
    }
}
