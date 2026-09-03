using Mew.Workbench;
using Xunit;

namespace Mew.Launcher.Tests;

/// <summary>
/// 悬停计数纯逻辑:主按钮与悬停浮现的编辑按钮共用同一计数,进入任一递增、离开任一递减;
/// 计数在 0↔1 边界变化时回调。保证在主按钮与编辑按钮之间移动时悬停态不丢失,且与事件顺序无关。
/// </summary>
public class HoverRefCountTests
{
    [Fact]
    public void Enter_首入_激活()
    {
        var hover = new HoverRefCount();

        hover.Enter();

        Assert.True(hover.IsRaised);
    }

    [Fact]
    public void Leave_未进入过_保持未激活且不误触发()
    {
        var hover = new HoverRefCount();
        var changed = 0;
        hover.RaisedChanged += () => changed++;

        hover.Leave();

        Assert.False(hover.IsRaised);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void 两次进入一次离开_仍激活()
    {
        var hover = new HoverRefCount();
        hover.Enter(); // 主按钮
        hover.Enter(); // 编辑按钮

        hover.Leave();

        Assert.True(hover.IsRaised);
    }

    [Fact]
    public void 交替进入离开_始终激活()
    {
        var hover = new HoverRefCount();
        hover.Enter();  // 主按钮
        hover.Leave();  // 移到编辑按钮
        hover.Enter();  // 编辑按钮
        hover.Leave();  // 移回主按钮
        hover.Enter();  // 主按钮

        Assert.True(hover.IsRaised);
    }

    [Fact]
    public void 全部离开_去激活()
    {
        var hover = new HoverRefCount();
        hover.Enter();
        hover.Enter();

        hover.Leave();
        hover.Leave();

        Assert.False(hover.IsRaised);
    }

    [Fact]
    public void 仅边界变化触发回调()
    {
        var hover = new HoverRefCount();
        var changes = 0;
        hover.RaisedChanged += () => changes++;

        hover.Enter();  // 0 → 1:触发
        hover.Enter();  // 1 → 2:不触发
        hover.Leave();  // 2 → 1:不触发
        hover.Leave();  // 1 → 0:触发

        Assert.Equal(2, changes);
    }
}
