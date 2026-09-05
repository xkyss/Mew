using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Mew.Workbench;
using Mew.Workbench.Plugins;
using Xunit;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host.Tests;

/// <summary>
/// 插件管理面板（ADR-000204 票据 01）：行渲染与动作路由只此一份、进程无关。
/// 复用组合测试先例：零句柄组装真实控件树，树遍历断言 Label 文本与 Foreground 颜色；
/// 动作路由经 Activate（按钮点击与测试共用的程序化入口）对 fake adapter 断言。
/// </summary>
public class PluginAdminPanelTests
{
    private readonly WorkbenchThemeContext _theme = new WorkbenchType().ThemeContext;

    // ---- 基建：fake adapter 与描述符构造 ----

    private sealed class FakeService : IPluginAdminService
    {
        public List<PluginAdminRow> Rows { get; set; } = [];
        public List<(string Id, PluginRowAction Action)> Applied { get; } = [];
        public PluginAdminResult NextResult { get; set; } = PluginAdminResult.Ok();

        public IReadOnlyList<PluginAdminRow> Snapshot() => Rows;

        public PluginAdminResult Apply(string id, PluginRowAction action)
        {
            Applied.Add((id, action));
            return NextResult;
        }
    }

    private static PluginDescriptor Descriptor(string id, string entryType = "dll", IReadOnlyList<string>? errors = null)
    {
        var manifest = new PluginManifest
        {
            Id = id,
            DisplayName = id + "-disp",
            Version = "1.2.3",
            Entry = new PluginEntry { Type = entryType, Path = "plug" },
            ProtocolVersion = 1,
        };
        return new PluginDescriptor(manifest, $"/tmp/{id}/plugin.json", errors ?? []);
    }

    private static PluginAdminRow Row(PluginDescriptor desc, bool enabled, bool crashed = false, bool stillLoaded = false)
        => new(desc, PluginRowState.Derive(enabled, desc.Health(isJitAvailable: true), crashed, stillLoaded));

    private static List<Label> Labels(UIElement root) => FindAllByType(root, typeof(Label)).OfType<Label>().ToList();

    private static List<Button> Buttons(UIElement root) => FindAllByType(root, typeof(Button)).OfType<Button>().ToList();

    // ---- 渲染断言 ----

    [Fact]
    public void Refresh_行文案与配色与既有两侧一致_标题含名称id版本()
    {
        var service = new FakeService
        {
            Rows =
            [
                Row(Descriptor("alpha"), enabled: true),
                Row(Descriptor("beta"), enabled: false, stillLoaded: true),
            ],
        };
        var panel = new PluginAdminPanel(service, _theme, "空");
        panel.Refresh();

        var labels = Labels(panel);
        Assert.Contains(labels, l => l.Text == "alpha-disp (alpha) v1.2.3");
        Assert.Contains(labels, l => l.Text == "beta-disp (beta) v1.2.3");
        Assert.Contains(labels, l => l.Text == "已启用");
        Assert.Contains(labels, l => l.Text == "已禁用");
        // T2 禁用仍加载：诚实副提示
        Assert.Contains(labels, l => l.Text == "已禁用，重启扩展主机后生效");
        // 健康行前景 = 编辑器区色
        var title = labels.Single(l => l.Text == "alpha-disp (alpha) v1.2.3");
        Assert.Equal(_theme.EditorArea.Foreground, title.Foreground);
    }

    [Fact]
    public void Refresh_健康态覆盖行_标红_校验文案_无误导开关()
    {
        var service = new FakeService
        {
            Rows =
            [
                Row(Descriptor("bad", errors: ["plugin.json 解析失败：x"]), enabled: true),
                Row(Descriptor("dead", entryType: "exe"), enabled: true, crashed: true),
            ],
        };
        var panel = new PluginAdminPanel(service, _theme, "空");
        panel.Refresh();

        var labels = Labels(panel);
        Assert.Contains(labels, l => l.Text == "清单错误");
        Assert.Contains(labels, l => l.Text == "已崩溃");
        Assert.Contains(labels, l => l.Text == "进程异常退出，点击重启重新拉起");
        Assert.Contains(labels, l => l.Text == "plugin.json 解析失败：x");
        // 标红：标题与状态词
        Assert.Equal(ShellIcons.HotkeyWarning, labels.Single(l => l.Text == "bad-disp (bad) v1.2.3").Foreground);
        Assert.Equal(ShellIcons.HotkeyWarning, labels.Single(l => l.Text == "已崩溃").Foreground);
        Assert.Equal(ShellIcons.HotkeyWarning, labels.Single(l => l.Text == "plugin.json 解析失败：x").Foreground);
        // 不可参与行动作显「—」；崩溃行动作「重启」
        Assert.Contains(labels, l => l.Text == "—");
        Assert.Contains(labels, l => l.Text == "重启");
    }

    [Fact]
    public void Refresh_行序按id排序_大小写不敏感()
    {
        var service = new FakeService
        {
            Rows =
            [
                Row(Descriptor("zulu"), enabled: true),
                Row(Descriptor("Alpha"), enabled: true),
                Row(Descriptor("mike"), enabled: true),
            ],
        };
        var panel = new PluginAdminPanel(service, _theme, "空");
        panel.Refresh();

        var titles = Labels(panel).Select(l => l.Text).Where(t => t.Contains(") v1.2.3")).ToList();
        Assert.Equal(["Alpha-disp (Alpha) v1.2.3", "mike-disp (mike) v1.2.3", "zulu-disp (zulu) v1.2.3"], titles);
    }

    [Fact]
    public void Refresh_空态_仅空态提示无行无页脚()
    {
        var service = new FakeService { Rows = [] };
        var panel = new PluginAdminPanel(service, _theme, "无独立进程插件", footerText: "崩溃说明");
        panel.Refresh();

        var labels = Labels(panel);
        Assert.Contains(labels, l => l.Text == "无独立进程插件");
        Assert.DoesNotContain(labels, l => l.Text == "崩溃说明");
        Assert.Empty(Buttons(panel));
        Assert.Equal(0, panel.RowCount);
    }

    [Fact]
    public void Refresh_行存在时页脚渲染_空态不渲染()
    {
        var service = new FakeService { Rows = [Row(Descriptor("alpha"), enabled: true)] };
        var panel = new PluginAdminPanel(service, _theme, "空", footerText: "已崩溃不自动重拉");
        panel.Refresh();
        Assert.Contains(Labels(panel), l => l.Text == "已崩溃不自动重拉");

        service.Rows.Clear();
        panel.Refresh();
        Assert.DoesNotContain(Labels(panel), l => l.Text == "已崩溃不自动重拉");
    }

    [Fact]
    public void Refresh_重复刷新_不残留旧行()
    {
        var service = new FakeService { Rows = [Row(Descriptor("alpha"), enabled: true)] };
        var panel = new PluginAdminPanel(service, _theme, "空");
        panel.Refresh();
        service.Rows = [Row(Descriptor("alpha"), enabled: true), Row(Descriptor("beta"), enabled: true)];
        panel.Refresh();

        var titles = Labels(panel).Select(l => l.Text).Where(t => t.Contains(") v1.2.3")).ToList();
        Assert.Equal(["alpha-disp (alpha) v1.2.3", "beta-disp (beta) v1.2.3"], titles);
    }

    // ---- 动作路由断言 ----

    [Fact]
    public void Activate_路由到adapter并回调_随后重绘()
    {
        var service = new FakeService { Rows = [Row(Descriptor("alpha"), enabled: true)] };
        var applied = new List<(string Id, PluginRowAction Action, PluginAdminResult Result)>();
        var panel = new PluginAdminPanel(service, _theme, "空",
            onApplied: (id, action, result) => applied.Add((id, action, result)));
        panel.Refresh();

        panel.Activate("alpha");

        var (id, action, result) = Assert.Single(applied);
        Assert.Equal("alpha", id);
        Assert.Equal(PluginRowAction.Disable, action); // 启用行的动作是禁用
        Assert.Equal(PluginAdminOutcome.Ok, result.Outcome);
    }

    [Fact]
    public void Activate_动作None不路由_不可参与插件无误导开关()
    {
        var service = new FakeService
        {
            Rows = [Row(Descriptor("bad", errors: ["清单错误"]), enabled: true)],
        };
        var panel = new PluginAdminPanel(service, _theme, "空");
        panel.Refresh();

        panel.Activate("bad");

        Assert.Empty(service.Applied);
    }

    [Fact]
    public void Activate_未知id_静默忽略()
    {
        var service = new FakeService { Rows = [Row(Descriptor("alpha"), enabled: true)] };
        var panel = new PluginAdminPanel(service, _theme, "空");
        panel.Refresh();

        panel.Activate("ghost");

        Assert.Empty(service.Applied);
    }

    // ---- 树遍历（与 HostCompositionTests 同构） ----

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
