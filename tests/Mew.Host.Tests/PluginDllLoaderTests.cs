using System.IO;
using System.Text.Json;
using Mew.Workbench;
using Mew.Workbench.Plugins;
using Xunit;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host.Tests;

public class PluginDllLoaderTests
{
    [Fact]
    public void Load_无启用或类型不匹配_不加载()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mew-dll-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "plug-a"));
        Directory.CreateDirectory(Path.Combine(tmp, "plug-b"));
        try
        {
            // plug-a: exe 类型，不应被 DLL loader 加载
            WriteManifest(Path.Combine(tmp, "plug-a", "plugin.json"), "plug-a", "A", "0.1.0", "exe", "a.exe");
            // plug-b: dll 类型但禁用
            WriteManifest(Path.Combine(tmp, "plug-b", "plugin.json"), "plug-b", "B", "0.1.0", "dll", "b.dll");
            File.WriteAllText(Path.Combine(tmp, "plug-b", "b.dll"), "dummy");

            var discovery = new PluginDiscovery();
            var discovered = discovery.Discover(tmp, Path.Combine(tmp, "_empty"));
            var enableStore = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-en-" + Guid.NewGuid() + ".json"));
            enableStore.Load();
            enableStore.SetEnabled("plug-b", false);
            var loader = new Mew.PluginHost.PluginDllLoader();
            var results = loader.Load(discovered, enableStore, () => CreateContext());
            // plug-a 被跳过（type!=dll），plug-b 因禁用跳过，results 应包含 plug-b 的禁用记录
            Assert.Contains(results, r => r.Descriptor.Id == "plug-b" && !r.Success);
            Assert.Empty(loader.Loaded);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Load_Dll不存在_返回错误_不崩()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mew-dll-miss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "miss"));
        try
        {
            WriteManifest(Path.Combine(tmp, "miss", "plugin.json"), "miss", "Miss", "0.1.0", "dll", "missing.dll");
            var discovery = new PluginDiscovery();
            var discovered = discovery.Discover(tmp, Path.Combine(tmp, "_empty"));
            var enableStore = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-en2-" + Guid.NewGuid() + ".json"));
            enableStore.Load();
            var loader = new Mew.PluginHost.PluginDllLoader();
            var results = loader.Load(discovered, enableStore, () => CreateContext());
            Assert.Single(results);
            Assert.False(results[0].Success);
            Assert.Contains("不存在", results[0].Error!);
            Assert.Empty(loader.Loaded);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Load_清单无效_返回错误()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mew-dll-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "bad"));
        try
        {
            WriteManifest(Path.Combine(tmp, "bad", "plugin.json"), "Bad_ID", "Bad", "bad", "dll", "bad.dll");
            var discovery = new PluginDiscovery();
            var discovered = discovery.Discover(tmp, Path.Combine(tmp, "_empty"));
            var enableStore = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-en3-" + Guid.NewGuid() + ".json"));
            enableStore.Load();
            var loader = new Mew.PluginHost.PluginDllLoader();
            var results = loader.Load(discovered, enableStore, () => CreateContext());
            Assert.Single(results);
            Assert.False(results[0].Success);
            Assert.Contains("id 非法", results[0].Error!);
        }
        finally { Directory.Delete(tmp, true); }
    }

    private static ToolModuleContext CreateContext()
    {
        var wb = new WorkbenchType();
        return new ToolModuleContext(wb, IntPtr.Zero, null, new HotkeyService(), new SettingsService(Path.Combine(Path.GetTempPath(), "a.json")), new FakeOverlay(), wb.ThemeContext, new SettingsSectionRegistry());
    }

    private sealed class FakeOverlay : IOverlayService { public void AddSearchSource(ISearchSource s) { } }

    private static void WriteManifest(string path, string id, string display, string version, string type, string entryPath)
    {
        var m = new PluginManifest { Id = id, DisplayName = display, Version = version, Entry = new PluginEntry { Type = type, Path = entryPath }, ProtocolVersion = 1 };
        var json = JsonSerializer.Serialize(m, PluginJsonContext.Default.PluginManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
