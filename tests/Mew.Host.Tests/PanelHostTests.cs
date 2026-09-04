using Aprillz.MewUI.Controls;
using Mew.Launcher;
using Mew.Workbench;
using Xunit;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host.Tests;

/// <summary>底部面板宿主窗格：多视图收敛为单个工具窗格 + 自建顶部页签条（VSCode 式）。</summary>
public class PanelHostTests
{
    [Fact]
    public void SelectPanel_默认首项_未知忽略_已知切换()
    {
        var workbench = new WorkbenchType();
        workbench.Panel(panel => panel
            .View("output", "输出", new StackPanel())
            .View("plugin-log", "插件日志", new StackPanel()));

        Assert.Equal("output", workbench.ActivePanelId);

        workbench.SelectPanel("nope");
        Assert.Equal("output", workbench.ActivePanelId);

        workbench.SelectPanel("plugin-log");
        Assert.Equal("plugin-log", workbench.ActivePanelId);

        // 点已激活项保持（不切换显隐）
        workbench.SelectPanel("plugin-log");
        Assert.Equal("plugin-log", workbench.ActivePanelId);
    }

    [Fact]
    public void SelectPanel_无视图时为null_不抛()
    {
        var workbench = new WorkbenchType();
        Assert.Null(workbench.ActivePanelId);
        workbench.SelectPanel("any");
        Assert.Null(workbench.ActivePanelId);
    }

    [Fact]
    public void Build_底部多视图收敛为单个宿主窗格()
    {
        using var _ = IsolateUserFiles();
        var workbench = new WorkbenchType();
        var ctx = ModuleContext(workbench);
        new LauncherModule().Configure(ctx);
        new FakeModule().Configure(ctx);

        var shell = workbench.Build();
        var dock = PanelFindHelper.FindDocking(shell);

        // 宿主窗格唯一存在，各视图不再是独立窗格
        Assert.Contains(dock.Panes, p => p.Component == "panel-host");
        Assert.DoesNotContain(dock.Panes, p => p.Component == "output");
        Assert.DoesNotContain(dock.Panes, p => p.Component == "todo-output");
    }

    [Fact]
    public void TogglePanel_多次开关_宿主窗格不增殖()
    {
        var workbench = new WorkbenchType();
        var ctx = ModuleContext(workbench);
        new LauncherModule().Configure(ctx);
        new FakeModule().Configure(ctx);

        var shell = workbench.Build();
        var dock = PanelFindHelper.FindDocking(shell);
        for (var i = 0; i < 3; i++)
        {
            workbench.TogglePanel();
            workbench.TogglePanel();
        }

        Assert.Single(dock.Panes, p => p.Component == "panel-host");
    }

    private static ToolModuleContext ModuleContext(WorkbenchType workbench) => new(
        workbench, IntPtr.Zero, null,
        new HotkeyService(), new SettingsService(Path.Combine(Path.GetTempPath(), "panel-test.json")),
        new RecordingOverlay(), workbench.ThemeContext, new SettingsSectionRegistry());

    private sealed class RecordingOverlay : Mew.Workbench.IOverlayService
    {
        public void AddSearchSource(ISearchSource s) { }
    }

    private static IDisposable IsolateUserFiles()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
        var names = new[] { "launcher.json", "layout.json", "presentation.json" };
        var saved = names.ToDictionary(name => name, name => File.Exists(Path.Combine(appData, name))
            ? File.ReadAllBytes(Path.Combine(appData, name))
            : null);
        foreach (var name in names)
        {
            try { File.Delete(Path.Combine(appData, name)); } catch (IOException) { }
        }

        return new Disposable(() =>
        {
            foreach (var (name, bytes) in saved)
            {
                var path = Path.Combine(appData, name);
                try
                {
                    if (bytes is not null)
                    {
                        Directory.CreateDirectory(appData);
                        File.WriteAllBytes(path, bytes);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                }
            }
        });
    }

    private sealed class Disposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private static class PanelFindHelper
    {
        public static Aprillz.MewUI.MewDock.DockingManager FindDocking(UIElement root)
        {
            var q = new Queue<UIElement>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var el = q.Dequeue();
                if (el is Aprillz.MewUI.MewDock.DockingManager dm) return dm;
                foreach (var child in Enumerate(el)) q.Enqueue(child);
            }

            throw new InvalidOperationException("未找到 DockingManager");
        }

        private static IEnumerable<UIElement> Enumerate(UIElement el)
        {
            var t = el.GetType();
            foreach (var p in t.GetProperties())
            {
                if (p.PropertyType == typeof(UIElement) && p.GetValue(el) is UIElement single)
                    yield return single;
                else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string))
                {
                    object? v;
                    try { v = p.GetValue(el); } catch { continue; }
                    if (v is System.Collections.IEnumerable seq)
                        foreach (var item in seq)
                            if (item is UIElement child)
                                yield return child;
                }
            }
        }
    }
}
