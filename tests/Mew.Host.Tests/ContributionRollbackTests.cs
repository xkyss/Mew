using Aprillz.MewUI.Controls;
using Mew.Workbench;
using Xunit;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host.Tests;

/// <summary>
/// 插件 Configure 事务化（ADR-000300 运行验收发现）:半途抛出的插件不得留下半份五区贡献,
/// 否则 Workbench.Build 的活动栏/侧边栏配对校验会以误导性异常崩掉整个扩展主机。
/// </summary>
public class ContributionRollbackTests
{
    [Fact]
    public void RollbackContributions_半装载贡献回退_Build配对校验通过()
    {
        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("settings", "设置", GlyphKind.Hamburger))
            .SideBar(side => side.View("settings", "设置", new StackPanel()));
        var snapshot = workbench.SnapshotContributions();

        // 模拟插件半装载:贡献了活动栏项后 Configure 抛出(如 0.20 移除的 API)
        try
        {
            workbench.ActivityBar(bar => bar.Item("mxd", "Mxd", GlyphKind.Hamburger));
            throw new InvalidOperationException("模拟 mxd 的 Method not found");
        }
        catch
        {
            workbench.RollbackContributions(snapshot);
        }

        workbench.Build(); // 配对校验通过 = 未被半份贡献污染
    }
}
