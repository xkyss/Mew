using System.Text.Json;
using Mew.Workbench.Ipc;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 启用/禁用写权力归一宿主的 IPC 缝测试（票据 03，ADR-000203 单写者）：
/// 扩展主机不再本地 Save，改经 pluginEnableSet 一次性消息请求宿主落盘。
/// 与 OverlayHotkeyIpcTests 同构：断言无宿主处理器时明确拒绝、有处理器时透传、JSON 往返保型。
/// </summary>
public class PluginEnableIpcTests
{
    [Fact]
    public void TrySetPluginEnabled_无处理器_明确拒绝()
    {
        var server = new IpcServer();
        var ack = server.TrySetPluginEnabled("demo-search", false);

        Assert.False(ack.Ok);
        Assert.Contains("不支持", ack.Error!);
    }

    [Fact]
    public void TrySetPluginEnabled_有处理器_透传并返回宿主结果()
    {
        string? seenId = null;
        bool seenEnabled = false;
        var server = new IpcServer(null, null, null,
            (id, enabled) =>
            {
                seenId = id;
                seenEnabled = enabled;
                return new PluginEnableSetAckMessage(true, null, id, enabled);
            });

        var ack = server.TrySetPluginEnabled("demo-search", false);

        Assert.True(ack.Ok);
        Assert.Equal("demo-search", seenId);
        Assert.False(seenEnabled);
        Assert.Equal("demo-search", ack.Id);
        Assert.False(ack.Enabled);
    }

    [Fact]
    public void PluginEnableSet消息_JSON往返_类型保留()
    {
        var json = JsonSerializer.Serialize(new PluginEnableSetMessage("demo-search", false), IpcJsonContext.Default.PluginEnableSetMessage);
        Assert.Contains("\"pluginEnableSet\"", json);
        var back = JsonSerializer.Deserialize(json, IpcJsonContext.Default.PluginEnableSetMessage);
        Assert.Equal("demo-search", back!.Id);
        Assert.False(back.Enabled);

        var ackJson = JsonSerializer.Serialize(new PluginEnableSetAckMessage(true, null, "demo-search", false), IpcJsonContext.Default.PluginEnableSetAckMessage);
        Assert.Contains("\"pluginEnableSetAck\"", ackJson);
        var ackBack = JsonSerializer.Deserialize(ackJson, IpcJsonContext.Default.PluginEnableSetAckMessage);
        Assert.True(ackBack!.Ok);
        Assert.Equal("demo-search", ackBack.Id);
        Assert.False(ackBack.Enabled);
    }
}
