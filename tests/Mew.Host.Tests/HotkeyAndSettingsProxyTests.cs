using Mew.Workbench;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;
using Xunit;

namespace Mew.Host.Tests;

public partial class HotkeyAndSettingsProxyTests
{
    [Fact]
    public void HotkeyProxy_跨插件冲突_集中检测_FindOwner点名()
    {
        var hotkeys = new HotkeyService();
        var server = new IpcServer(hotkeys, null);
        var capA = new PluginCapabilitiesDto(null, null, new List<HotkeyDto> { new("quick-paste", "Ctrl+Shift+V", "快速粘贴") });
        var capB = new PluginCapabilitiesDto(null, null, new List<HotkeyDto> { new("other", "Ctrl+Shift+V", "其他") });
        var proxyA = new HotkeyProxy(server, "plug-a", capA);
        var proxyB = new HotkeyProxy(server, "plug-b", capB);

        Assert.True(proxyA.Register(IntPtr.Zero, "Ctrl+Shift+V", () => { }, "插件『剪贴板』快速粘贴"));
        Assert.True(proxyA.IsRegistered("Ctrl+Shift+V"));
        Assert.Equal("插件『剪贴板』快速粘贴", proxyA.FindOwner("Ctrl+Shift+V"));

        // 跨插件冲突：B 再注册同组合失败
        Assert.False(proxyB.Register(IntPtr.Zero, "Ctrl+Shift+V", () => { }, "插件『其他』"));
        Assert.Equal("插件『剪贴板』快速粘贴", proxyB.FindOwner("Ctrl+Shift+V"));
    }

    [Fact]
    public void HotkeyProxy_未声明能力_拒绝注册()
    {
        var hotkeys = new HotkeyService();
        var server = new IpcServer(hotkeys, null);
        var noCap = new PluginCapabilitiesDto(null, null, null);
        var proxy = new HotkeyProxy(server, "plug-x", noCap);
        Assert.False(proxy.Register(IntPtr.Zero, "Ctrl+Shift+X", () => { }, "X"));
    }

    [Fact]
    public void Settings_按插件Id分节隔离()
    {
        var path = Path.Combine(Path.GetTempPath(), "mew-settings-proxy-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new SettingsService(path);
            settings.Load();
            var a = new TestSection { Value = "A1" };
            var b = new TestSection { Value = "B1" };
            settings.WriteSection("plug-a", a, TestSectionContext.Default.TestSection);
            settings.WriteSection("plug-b", b, TestSectionContext.Default.TestSection);
            settings.Save();

            var settings2 = new SettingsService(path);
            settings2.Load();
            var ra = settings2.ReadSection("plug-a", TestSectionContext.Default.TestSection);
            var rb = settings2.ReadSection("plug-b", TestSectionContext.Default.TestSection);
            Assert.Equal("A1", ra!.Value);
            Assert.Equal("B1", rb!.Value);
            // 互不踩键
            Assert.NotEqual(ra.Value, rb.Value);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SettingsSection_未声明能力_拒绝()
    {
        var server = new IpcServer(null, null);
        var noCap = new PluginCapabilitiesDto(null, null, null);
        Assert.False(server.CanRegisterSettingsSection("plug-x", noCap, out var err));
        Assert.Contains("未声明", err!);
        var okCap = new PluginCapabilitiesDto(null, new SettingsSectionDto("plug-x", "插件X"), null);
        Assert.True(server.CanRegisterSettingsSection("plug-x", okCap, out err));
        Assert.Null(err);
    }

    [Fact]
    public void SettingsSection_统一注册表_含外观插件与模块节()
    {
        var registry = new SettingsSectionRegistry();
        registry.Add("plugins", "插件", () => new Aprillz.MewUI.Controls.StackPanel());
        registry.Add("data", "数据", () => new Aprillz.MewUI.Controls.StackPanel());
        Assert.Contains(registry.Sections, s => s.Id == "plugins");
        Assert.Contains(registry.Sections, s => s.Id == "data");
    }

    private sealed class TestSection { public string Value { get; set; } = ""; }
    [System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
    [System.Text.Json.Serialization.JsonSerializable(typeof(TestSection))]
    private sealed partial class TestSectionContext : System.Text.Json.Serialization.JsonSerializerContext { }
}
