using Mew.Workbench;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 托盘菜单分发测试（票据 01）：菜单 id 与动作一一对应，左键/显示主窗口不再附带拉起；
/// 仅测纯分发表，不建窗口、不调 Win32。
/// </summary>
public class TrayIconMenuTests
{
    [Fact]
    public void Dispatch_显示主窗口_仅调显示动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(TrayIcon.MenuShowMain,
            () => calls.Add("show"), () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.True(ok);
        Assert.Equal(["show"], calls);
    }

    [Fact]
    public void Dispatch_打开扩展主机_仅调打开动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(TrayIcon.MenuOpenWorkspace,
            () => calls.Add("show"), () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.True(ok);
        Assert.Equal(["open"], calls);
    }

    [Fact]
    public void Dispatch_重启扩展主机_仅调重启动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(TrayIcon.MenuRestartWorkspace,
            () => calls.Add("show"), () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.True(ok);
        Assert.Equal(["restart"], calls);
    }

    [Fact]
    public void Dispatch_退出_仅调退出动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(TrayIcon.MenuQuit,
            () => calls.Add("show"), () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.True(ok);
        Assert.Equal(["quit"], calls);
    }

    [Fact]
    public void Dispatch_动作为空_跳过不抛_仍返回真()
    {
        var calls = new List<string>();
        Assert.True(TrayIcon.TryDispatchMenu(TrayIcon.MenuOpenWorkspace,
            () => calls.Add("show"), () => calls.Add("quit"), null, null));
        Assert.True(TrayIcon.TryDispatchMenu(TrayIcon.MenuRestartWorkspace,
            () => calls.Add("show"), () => calls.Add("quit"), null, null));
        Assert.Empty(calls);
    }

    [Fact]
    public void Dispatch_未知id_返回假_无动作()
    {
        var calls = new List<string>();
        var ok = TrayIcon.TryDispatchMenu(999,
            () => calls.Add("show"), () => calls.Add("quit"),
            () => calls.Add("open"), () => calls.Add("restart"));
        Assert.False(ok);
        Assert.Empty(calls);
    }

    [Fact]
    public void LeftClick_有自定义动作_执行它_不显示主窗口()
    {
        var calls = new List<string>();
        TrayIcon.DispatchLeftClick(() => calls.Add("overlay"), () => calls.Add("show"));
        Assert.Equal(["overlay"], calls);
    }

    [Fact]
    public void LeftClick_无自定义动作_回退显示主窗口()
    {
        var calls = new List<string>();
        TrayIcon.DispatchLeftClick(null, () => calls.Add("show"));
        Assert.Equal(["show"], calls);
    }
}
