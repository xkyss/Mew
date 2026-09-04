using System.Text.Json;
using Mew.Workbench;
using Mew.Workbench.Ipc;
using Xunit;

namespace Mew.Host.Tests;

public class OverlayHotkeyAdminTests
{
    private static (HotkeyService Hotkeys, SettingsService Settings) Setup(out string current)
    {
        var hotkeys = new HotkeyService();
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "mew-hk-" + Guid.NewGuid() + ".json"));
        current = HotkeyService.DefaultOverlayHotkey;
        Assert.True(hotkeys.Register(IntPtr.Zero, current, () => { }, "浮层呼出键"));
        return (hotkeys, settings);
    }

    [Fact]
    public void Apply_禁用_注销旧键并返回禁用态()
    {
        var (hotkeys, _) = Setup(out var current);
        var (ok, error, effectiveHotkey, effectiveEnabled) =
            OverlayHotkeyAdmin.Apply(hotkeys, current, IntPtr.Zero, () => { }, "浮层呼出键", current, enabled: false);

        Assert.True(ok);
        Assert.Null(error);
        Assert.False(effectiveEnabled);
        Assert.Equal(current, effectiveHotkey);
        Assert.False(hotkeys.IsRegistered(current));
    }

    [Fact]
    public void Apply_非法组合_拒绝且旧键保留()
    {
        var (hotkeys, _) = Setup(out var current);
        var (ok, error, effectiveHotkey, effectiveEnabled) =
            OverlayHotkeyAdmin.Apply(hotkeys, current, IntPtr.Zero, () => { }, "浮层呼出键", "不是组合", enabled: true);

        Assert.False(ok);
        Assert.Contains("不支持", error!);
        Assert.True(effectiveEnabled);
        Assert.Equal(current, effectiveHotkey);
        Assert.True(hotkeys.IsRegistered(current));
    }

    [Fact]
    public void Apply_与他人冲突_点名且旧键保留()
    {
        var (hotkeys, _) = Setup(out var current);
        Assert.True(hotkeys.Register(IntPtr.Zero, "Ctrl+Shift+V", () => { }, "快速粘贴"));
        var (ok, error, _, _) =
            OverlayHotkeyAdmin.Apply(hotkeys, current, IntPtr.Zero, () => { }, "浮层呼出键", "Ctrl+Shift+V", enabled: true);

        Assert.False(ok);
        Assert.Contains("快速粘贴", error!);
        Assert.True(hotkeys.IsRegistered(current));
    }

    [Fact]
    public void Apply_改键成功_旧键注销新键生效()
    {
        var (hotkeys, _) = Setup(out var current);
        const string next = "Ctrl+Alt+Space";
        var (ok, error, effectiveHotkey, effectiveEnabled) =
            OverlayHotkeyAdmin.Apply(hotkeys, current, IntPtr.Zero, () => { }, "浮层呼出键", next, enabled: true);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(next, effectiveHotkey);
        Assert.True(effectiveEnabled);
        Assert.False(hotkeys.IsRegistered(current));
        Assert.True(hotkeys.IsRegistered(next));
        Assert.Equal("浮层呼出键", hotkeys.FindOwner(next));
    }

    [Fact]
    public void Apply_同键重设_成功()
    {
        var (hotkeys, _) = Setup(out var current);
        var (ok, _, effectiveHotkey, _) =
            OverlayHotkeyAdmin.Apply(hotkeys, current, IntPtr.Zero, () => { }, "浮层呼出键", current, enabled: true);

        Assert.True(ok);
        Assert.Equal(current, effectiveHotkey);
        Assert.True(hotkeys.IsRegistered(current));
    }
}

public class OverlayHotkeyIpcTests
{
    [Fact]
    public void TrySetOverlayHotkey_无处理器_明确拒绝()
    {
        var server = new IpcServer(new HotkeyService());
        var ack = server.TrySetOverlayHotkey("Alt+Space", true);

        Assert.False(ack.Ok);
        Assert.Contains("不支持", ack.Error!);
    }

    [Fact]
    public void TrySetOverlayHotkey_有处理器_透传()
    {
        var server = new IpcServer(new HotkeyService(), null,
            (hotkey, enabled) => new OverlayHotkeySetAckMessage(true, null, hotkey, enabled));
        var ack = server.TrySetOverlayHotkey("Ctrl+Space", true);

        Assert.True(ack.Ok);
        Assert.Equal("Ctrl+Space", ack.Hotkey);
        Assert.True(ack.Enabled);
    }

    [Fact]
    public void OverlayHotkeySet消息_JSON往返_类型保留()
    {
        var json = JsonSerializer.Serialize(new OverlayHotkeySetMessage("Alt+Space", true), IpcJsonContext.Default.OverlayHotkeySetMessage);
        Assert.Contains("\"overlayHotkeySet\"", json);
        var back = JsonSerializer.Deserialize(json, IpcJsonContext.Default.OverlayHotkeySetMessage);
        Assert.Equal("Alt+Space", back!.Hotkey);
        Assert.True(back.Enabled);

        var ackJson = JsonSerializer.Serialize(new OverlayHotkeySetAckMessage(false, "冲突", "Alt+Space", true), IpcJsonContext.Default.OverlayHotkeySetAckMessage);
        Assert.Contains("\"overlayHotkeySetAck\"", ackJson);
        var ackBack = JsonSerializer.Deserialize(ackJson, IpcJsonContext.Default.OverlayHotkeySetAckMessage);
        Assert.False(ackBack!.Ok);
        Assert.Equal("冲突", ackBack.Error);
    }
}
