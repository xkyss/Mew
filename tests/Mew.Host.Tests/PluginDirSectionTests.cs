using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Mew.PluginHost;
using Mew.Workbench;
using Xunit;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host.Tests;

/// <summary>
/// 插件页目录节（ADR-000301 票据 02）：单一主目录 = 当前路径展示 + 「修改」入口 + 隐藏式反馈 + 精简说明。
/// 无头控件树断言（组合测试先例）；文件夹选择器本体为运行期行为，进 Windows 走查。
/// </summary>
public class PluginDirSectionTests
{
    private readonly WorkbenchThemeContext _theme = new WorkbenchType().ThemeContext;

    [Fact]
    public void Build_路径展示_修改入口_反馈初始隐藏()
    {
        var panel = new StackPanel();
        var picked = false;

        var feedback = PluginHostApp.BuildPluginDirsSection(panel, _theme, @"C:\mew-plugins", () => picked = true);

        var labels = FindAllByType(panel, typeof(Label)).OfType<Label>().ToList();
        Assert.Contains(labels, l => l.Text == @"C:\mew-plugins");
        Assert.Contains(labels, l => l.Text == "修改");
        Assert.Contains(labels, l => l.Text.Contains("重启宿主生效"));
        Assert.Contains(labels, l => l.Text.Contains("mklink /D"));
        Assert.Contains(labels, l => l.Text.Contains("mklink /J"));
        // 反馈初始隐藏不占位
        Assert.False(feedback.IsVisible);
        // 修改入口存在(单个按钮;点击回调由 pickPluginDir 直连,对话框本体为运行期行为)
        Assert.Single(FindAllByType(panel, typeof(Button)).OfType<Button>());
    }

    [Fact]
    public void Build_反馈点亮_显示失败信息()
    {
        var panel = new StackPanel();

        var feedback = PluginHostApp.BuildPluginDirsSection(panel, _theme, @"C:\mew-plugins", () => { });

        feedback.Text = "目录不存在，将被忽略";
        feedback.IsVisible = true;
        Assert.True(feedback.IsVisible);
        Assert.Contains(FindAllByType(panel, typeof(Label)).OfType<Label>(), l => l.Text == "目录不存在，将被忽略");
    }

    private static List<UIElement> FindAllByType(UIElement root, Type type)
    {
        var found = new List<UIElement>();
        if (type.IsInstanceOfType(root))
            found.Add(root);
        if (root is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                found.AddRange(FindAllByType((UIElement)child, type));
                return true;
            });
        }

        return found;
    }
}
