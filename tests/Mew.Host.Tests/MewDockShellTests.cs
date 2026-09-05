using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Mew.Workbench;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// MewDockShell 适配层（ADR-000205 票据 01）：字符串表中文化、分组菜单去重（以字符串表当前值为准）、
/// Tune 幂等契约。Tune 用真实 DockingManager 无头驱动（组合测试先例：MewUI 控件可零窗口构建）。
/// </summary>
public class MewDockShellTests
{
    // ---- 字符串表迁移 ----

    [Fact]
    public void Localize_字符串表中文化生效_空标题留白()
    {
        MewDockShell.Localize();

        Assert.Equal("自动隐藏", MewUIDockString.MenuAutoHide.Value);
        Assert.Equal("关闭", MewUIDockString.MenuClose.Value);
        Assert.Equal("", MewUIDockString.TitleUnnamedTab.Value);
    }

    // ---- 菜单去重：以字符串表当前值为准 ----

    [Fact]
    public void RedundantGroupMenuTexts_Localize后取当前文案()
    {
        MewDockShell.Localize();

        var redundant = MewDockShell.RedundantGroupMenuTexts();

        Assert.Contains("自动隐藏", redundant);
        Assert.Contains("关闭", redundant);
    }

    [Fact]
    public void PruneGroupMenuTexts_纯函数_摘除冗余保留其余()
    {
        List<string> texts = ["浮动", "自动隐藏", "关闭", "关闭全部"];
        HashSet<string> redundant = new(StringComparer.Ordinal) { "自动隐藏", "关闭" };

        var kept = MewDockShell.PruneGroupMenuTexts(texts, redundant);

        Assert.Equal(["浮动", "关闭全部"], kept);
    }

    [Fact]
    public void PruneGroupMenu_真实菜单_同义项被摘除_独立项保留()
    {
        MewDockShell.Localize();
        var menu = new ContextMenu();
        menu.Item("浮动", () => { });
        menu.Item("自动隐藏", () => { });
        menu.Item("关闭", () => { });
        menu.Item("关闭全部", () => { });

        MewDockShell.PruneGroupMenu(menu);

        Assert.Equal(["浮动", "关闭全部"], menu.Items.OfType<MenuItem>().Select(i => i.Text).ToArray());
    }

    // ---- Tune 幂等契约 ----

    [Fact]
    public void Tune_真实DockingManager_连调两次_接线去重不重复()
    {
        var docking = new DockingManager();
        docking.AddDocumentPane("文档", new StackPanel(), "doc1");
        var shell = new MewDockShell(docking);

        shell.Tune();
        var afterFirst = shell.ConfiguredTabCloseCount;

        shell.Tune();

        Assert.True(afterFirst > 0, $"应有文档 tab 接线了关闭悬浮,实际 {afterFirst}");
        Assert.Equal(afterFirst, shell.ConfiguredTabCloseCount);
    }

    [Fact]
    public void Tune_空停靠_连调两次_不抛异常()
    {
        var shell = new MewDockShell(new DockingManager());

        shell.Tune();
        shell.Tune();

        Assert.Equal(0, shell.ConfiguredTabCloseCount);
    }
}
