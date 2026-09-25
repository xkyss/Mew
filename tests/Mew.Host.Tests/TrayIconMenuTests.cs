using Mew.Workbench;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 托盘分发测试：菜单只有打开/重启主界面与退出，单击打开主界面（只唤出、不隐藏），浮层仅经热键呼出；
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
    public void SingleClick_有自定义动作_执行并返回真()
    {
        var calls = new List<string>();
        Assert.True(TrayIcon.DispatchSingleClick(() => calls.Add("open-main")));
        Assert.Equal(["open-main"], calls);
    }

    [Fact]
    public void SingleClick_无自定义动作_返回假()
    {
        Assert.False(TrayIcon.DispatchSingleClick(null));
    }

    [Fact]
    public void Balloon_常规文本_原样返回()
    {
        var (title, text) = TrayIcon.BuildBalloonText("Mew Launcher", "主界面已唤出");
        Assert.Equal("Mew Launcher", title);
        Assert.Equal("主界面已唤出", text);
    }

    [Fact]
    public void Balloon_超长文本_按气球容量截断()
    {
        var (title, text) = TrayIcon.BuildBalloonText(new string('T', 100), new string('X', 300));
        Assert.Equal(63, title.Length);
        Assert.Equal(255, text.Length);
    }
}
