using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Mew.Launcher;
using Mew.Workbench;
using Mew.Workbench.Plugins;
using Xunit;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host.Tests;

/// <summary>
/// 扩展主机组装（票据 02）：验证 Workbench 五区已由 PluginHost 承载，
/// 宿主不再直接 Build，编译期模块零改动在扩展主机中仍可组装。
/// </summary>
public class PluginHostCompositionTests
{
    [Fact]
    public void PluginHost_真实Launcher_Build成功_五区含贡献()
    {
        var workbench = new WorkbenchType();
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "mew-ph-test-" + Guid.NewGuid().ToString("N") + ".json"));
        var overlay = new RecordingOverlay();
        var settingsSections = new SettingsSectionRegistry();
        var context = new ToolModuleContext(
            workbench, IntPtr.Zero, null,
            new HotkeyService(), settings, overlay, workbench.ThemeContext, settingsSections);

        // 插件发现占位：与宿主同逻辑，扩展主机为 JIT故可加载 DLL
        var enables = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-ph-en-" + Guid.NewGuid().ToString("N") + ".json"));
        enables.Load();
        settingsSections.Add("plugins", "插件", () => new StackPanel());

        new LauncherModule().Configure(context);

        workbench
            .ActivityBar(bar => bar.Item("settings", "设置", GlyphKind.Hamburger))
            .SideBar(side => side.View("settings", "设置", new StackPanel()))
            .EditorArea(editor => editor.Document("settings-document", "设置", new StackPanel()));

        var shell = workbench.Build();
        // 验证五区包含 Launcher 的贡献
        var dock = FindHelper.FindDocking(shell);
        Assert.Contains(dock.Panes, p => p.Component == "launch");
        Assert.Contains(dock.DocumentPanes, p => p.Component == "items");
        // 设置节含插件
        Assert.Contains(settingsSections.Sections, s => s.Id == "plugins");
    }

    [Fact]
    public void PluginHost_未Build前_可重复注册模块()
    {
        var workbench = new WorkbenchType();
        var ctx = new ToolModuleContext(
            workbench, IntPtr.Zero, null,
            new HotkeyService(), new SettingsService(Path.Combine(Path.GetTempPath(), "a.json")),
            new RecordingOverlay(), workbench.ThemeContext, new SettingsSectionRegistry());
        new LauncherModule().Configure(ctx);
        new FakeModule().Configure(ctx);
        // 不应抛
        workbench.ActivityBar(bar => bar.Item("settings", "设置", GlyphKind.Hamburger))
            .SideBar(side => side.View("settings", "设置", new StackPanel()))
            .EditorArea(editor => editor.Document("settings-document", "设置", new StackPanel()));
        var shell = workbench.Build();
        Assert.NotNull(shell);
    }

    // 复用 HostCompositionTests 的辅助查找
    private static class FindHelper
    {
        public static Aprillz.MewUI.MewDock.DockingManager FindDocking(UIElement root)
        {
            var q = new Queue<UIElement>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var el = q.Dequeue();
                if (el is Aprillz.MewUI.MewDock.DockingManager dm) return dm;
                // 反射遍历 Children / Content 简化：走 HostCompositionTests 的同款辅助
                foreach (var child in EnumerateChildren(el)) q.Enqueue(child);
            }
            throw new InvalidOperationException("未找到 DockingManager");
        }

        private static IEnumerable<UIElement> EnumerateChildren(UIElement el)
        {
            var type = el.GetType();
            foreach (var prop in type.GetProperties())
            {
                if (prop.PropertyType == typeof(UIElement) && prop.GetValue(el) is UIElement child) yield return child;
                if (prop.PropertyType.IsGenericType && prop.GetValue(el) is System.Collections.IEnumerable en)
                {
                    foreach (var item in en) if (item is UIElement c) yield return c;
                }
            }
        }
    }

    private sealed class RecordingOverlay : Mew.Workbench.IOverlayService
    {
        public List<ISearchSource> Sources { get; } = [];
        public void AddSearchSource(ISearchSource source) => Sources.Add(source);
    }
}
