using Mew.Workbench;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 托盘分发测试（票据 01/05）：菜单只有打开/重启扩展主机与退出，左键呼出浮层；
/// 仅测纯分发表，不建窗口、不调 Win32。
/// </summary>
public class TrayIconMenuTests
{
    [Fact]
    public void Dispatch_打开扩展主机_仅调打开动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(TrayIcon.MenuOpenWorkspace,
            () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.True(ok);
        Assert.Equal(["open"], calls);
    }

    [Fact]
    public void Dispatch_重启扩展主机_仅调重启动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(TrayIcon.MenuRestartWorkspace,
            () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.True(ok);
        Assert.Equal(["restart"], calls);
    }

    [Fact]
    public void Dispatch_退出_仅调退出动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(TrayIcon.MenuQuit,
            () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.True(ok);
        Assert.Equal(["quit"], calls);
    }

    [Fact]
    public void Dispatch_动作为空_跳过不抛_仍返回真()
    {
        var calls = new List<string>();
        Assert.True(TrayIcon.TryDispatchMenu(TrayIcon.MenuOpenWorkspace,
            () => calls.Add("quit"), null, null));
        Assert.True(TrayIcon.TryDispatchMenu(TrayIcon.MenuRestartWorkspace,
            () => calls.Add("quit"), null, null));
        Assert.Empty(calls);
    }

    [Fact]
    public void Dispatch_未知id_返回假_无动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(999,
            () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.False(ok);
        Assert.Empty(calls);
    }

    [Fact]
    public void LeftClick_有自定义动作_执行并返回真()
    {
        var calls = new List<string>();
        Assert.True(TrayIcon.DispatchLeftClick(() => calls.Add("overlay")));
        Assert.Equal(["overlay"], calls);
    }

    [Fact]
    public void LeftClick_无自定义动作_返回假()
    {
        Assert.False(TrayIcon.DispatchLeftClick(null));
    }
}
