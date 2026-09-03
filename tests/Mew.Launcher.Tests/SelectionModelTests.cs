using Mew.Workbench;
using Xunit;

namespace Mew.Launcher.Tests;

/// <summary>
/// 列表选中模型纯逻辑:上下移动环绕、空列表保持无选中、刷新后收敛不越界;启动项列表与呼出浮层共用。
/// </summary>
public class SelectionModelTests
{
    [Fact]
    public void MoveDown_初始_选中首项()
    {
        var model = new SelectionModel();

        model.MoveDown(3);

        Assert.Equal(1, model.Selected);
    }

    [Fact]
    public void MoveDown_末位_环绕到首位()
    {
        var model = new SelectionModel();
        model.MoveDown(3);
        model.MoveDown(3);

        model.MoveDown(3);

        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void MoveUp_首位_环绕到末位()
    {
        var model = new SelectionModel();

        model.MoveUp(3);

        Assert.Equal(2, model.Selected);
    }

    [Fact]
    public void MoveUp_上移一位()
    {
        var model = new SelectionModel();
        model.MoveDown(3);

        model.MoveUp(3);

        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void Move_空列表_保持无选中()
    {
        var model = new SelectionModel();

        model.MoveDown(0);
        model.MoveUp(0);

        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void Clamp_越界_收敛到可见范围()
    {
        var model = new SelectionModel();
        model.MoveDown(3);
        model.MoveDown(3); // 2

        model.Clamp(2);

        Assert.Equal(1, model.Selected);
    }

    [Fact]
    public void Clamp_空列表_归零()
    {
        var model = new SelectionModel();
        model.MoveDown(3);

        model.Clamp(0);

        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void Clamp_范围内_保持不变()
    {
        var model = new SelectionModel();
        model.MoveDown(3); // 1

        model.Clamp(5);

        Assert.Equal(1, model.Selected);
    }
}
