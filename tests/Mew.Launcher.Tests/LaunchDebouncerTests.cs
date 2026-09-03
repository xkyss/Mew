using Mew.Workbench;
using Xunit;

namespace Mew.Launcher.Tests;

/// <summary>
/// 列表单击启动的双击防重纯逻辑:同项在时间窗内的重复请求被忽略,时间窗外重新放行,不同项互不影响。
/// </summary>
public class LaunchDebouncerTests
{
    private static readonly DateTime T0 = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldLaunch_首次请求_放行()
    {
        var debouncer = new LaunchDebouncer(TimeSpan.FromMilliseconds(500));

        Assert.True(debouncer.ShouldLaunch("item-1", T0));
    }

    [Fact]
    public void ShouldLaunch_时间窗内同项重复请求_忽略()
    {
        var debouncer = new LaunchDebouncer(TimeSpan.FromMilliseconds(500));
        debouncer.ShouldLaunch("item-1", T0);

        Assert.False(debouncer.ShouldLaunch("item-1", T0.AddMilliseconds(200)));
    }

    [Fact]
    public void ShouldLaunch_时间窗外同项请求_重新放行()
    {
        var debouncer = new LaunchDebouncer(TimeSpan.FromMilliseconds(500));
        debouncer.ShouldLaunch("item-1", T0);

        Assert.True(debouncer.ShouldLaunch("item-1", T0.AddMilliseconds(501)));
    }

    [Fact]
    public void ShouldLaunch_恰好等于窗口时长_放行()
    {
        var debouncer = new LaunchDebouncer(TimeSpan.FromMilliseconds(500));
        debouncer.ShouldLaunch("item-1", T0);

        Assert.True(debouncer.ShouldLaunch("item-1", T0.AddMilliseconds(500)));
    }

    [Fact]
    public void ShouldLaunch_不同项_互不影响()
    {
        var debouncer = new LaunchDebouncer(TimeSpan.FromMilliseconds(500));
        debouncer.ShouldLaunch("item-1", T0);

        Assert.True(debouncer.ShouldLaunch("item-2", T0.AddMilliseconds(100)));
        Assert.False(debouncer.ShouldLaunch("item-1", T0.AddMilliseconds(100)));
    }
}
