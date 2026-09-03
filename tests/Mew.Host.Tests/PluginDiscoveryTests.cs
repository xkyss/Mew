using System.IO;
using System.Text.Json;
using Mew.Workbench.Plugins;
using Xunit;

namespace Mew.Host.Tests;

public class PluginDiscoveryTests
{
    [Fact]
    public void Validate_合法清单_通过()
    {
        var m = new PluginManifest
        {
            Id = "clipboard-history",
            DisplayName = "剪贴板历史",
            Version = "0.1.0",
            Entry = new PluginEntry { Type = "exe", Path = "Clipboard.exe", Args = "--mew-plugin" },
            Capabilities = new PluginCapabilities
            {
                Search = new PluginSearchCapability { ProviderId = "clipboard", DisplayName = "剪贴板" },
                SettingsSection = new PluginSettingsSectionCapability { Id = "clipboard", Title = "剪贴板" },
                Hotkeys = new() { new PluginHotkeyCapability { Id = "quick-paste", Default = "Ctrl+Shift+V", Label = "快速粘贴" } }
            },
            Permissions = new() { "search", "settings", "hotkeys" },
            ProtocolVersion = 1
        };
        Assert.Empty(m.Validate());
        Assert.False(m.RequiresJit);
    }

    [Fact]
    public void Validate_dll类型_RequiresJit为真()
    {
        var m = new PluginManifest
        {
            Id = "todo-dll",
            DisplayName = "待办",
            Version = "1.0.0",
            Entry = new PluginEntry { Type = "dll", Path = "Todo.dll" },
        };
        Assert.Empty(m.Validate());
        Assert.True(m.RequiresJit);
    }

    [Fact]
    public void Validate_非法id与版本_返回错误()
    {
        var m = new PluginManifest
        {
            Id = "Bad_ID",
            DisplayName = "",
            Version = "bad",
            Entry = new PluginEntry { Type = "dll", Path = "bad.exe" }, // 故意与 type 不匹配
        };
        var errors = m.Validate();
        Assert.Contains(errors, e => e.Contains("id 非法"));
        Assert.Contains(errors, e => e.Contains("displayName"));
        Assert.Contains(errors, e => e.Contains("version"));
        Assert.Contains(errors, e => e.Contains("entry.path"));
    }

    [Fact]
    public void Validate_非法permissions_返回错误()
    {
        var m = new PluginManifest
        {
            Id = "test-plugin",
            DisplayName = "测试",
            Version = "0.1.0",
            Entry = new PluginEntry { Type = "exe", Path = "a.exe" },
            Permissions = new() { "search", "illegal" }
        };
        var errors = m.Validate();
        Assert.Contains(errors, e => e.Contains("permissions"));
    }

    [Fact]
    public void Discover_扫描两目录_发现合法清单()
    {
        var userDir = Path.Combine(Path.GetTempPath(), "mew-test-disc-" + Guid.NewGuid().ToString("N"));
        var installDir = Path.Combine(Path.GetTempPath(), "mew-test-inst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(userDir, "alpha"));
        Directory.CreateDirectory(Path.Combine(installDir, "beta"));
        try
        {
            WriteManifest(Path.Combine(userDir, "alpha", "plugin.json"), "alpha", "Alpha", "0.1.0", "exe", "a.exe");
            WriteManifest(Path.Combine(installDir, "beta", "plugin.json"), "beta", "Beta", "0.2.0", "exe", "b.exe");

            var discovery = new PluginDiscovery();
            var result = discovery.Discover(userDir, installDir);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r.Id == "alpha" && r.IsValid);
            Assert.Contains(result, r => r.Id == "beta" && r.IsValid);
        }
        finally
        {
            Directory.Delete(userDir, true);
            Directory.Delete(installDir, true);
        }
    }

    [Fact]
    public void Discover_损坏文件与重复id_后者标为重复_其余仍列出()
    {
        var userDir = Path.Combine(Path.GetTempPath(), "mew-test-dup-" + Guid.NewGuid().ToString("N"));
        var installDir = Path.Combine(Path.GetTempPath(), "mew-test-dup2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(userDir, "dup-a"));
        Directory.CreateDirectory(Path.Combine(userDir, "dup-b"));
        Directory.CreateDirectory(Path.Combine(userDir, "bad"));
        try
        {
            WriteManifest(Path.Combine(userDir, "dup-a", "plugin.json"), "dup", "DupA", "0.1.0", "exe", "a.exe");
            WriteManifest(Path.Combine(userDir, "dup-b", "plugin.json"), "dup", "DupB", "0.1.0", "exe", "b.exe");
            File.WriteAllText(Path.Combine(userDir, "bad", "plugin.json"), "{ not json }");

            var discovery = new PluginDiscovery();
            var result = discovery.Discover(userDir, installDir);

            Assert.Equal(3, result.Count);
            var firstDup = result.First(r => r.ManifestPath.Contains("dup-a"));
            var secondDup = result.First(r => r.ManifestPath.Contains("dup-b"));
            var bad = result.First(r => r.ManifestPath.Contains("bad"));

            Assert.True(firstDup.IsValid);
            Assert.True(secondDup.IsDuplicate);
            Assert.False(secondDup.IsValid);
            Assert.Contains(secondDup.ValidationErrors, e => e.Contains("id 重复"));
            Assert.False(bad.IsValid);
            Assert.Contains(bad.ValidationErrors, e => e.Contains("解析失败"));
        }
        finally
        {
            Directory.Delete(userDir, true);
            if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
        }
    }

    [Fact]
    public void EnableStore_读写往返_未记录视为启用()
    {
        var path = Path.Combine(Path.GetTempPath(), "mew-plugins-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new PluginEnableStore(path);
            store.Load();
            Assert.True(store.IsEnabled("unknown")); // 默认启用

            store.SetEnabled("alpha", false);
            store.SetEnabled("beta", true);
            store.Save();

            var store2 = new PluginEnableStore(path);
            store2.Load();
            Assert.False(store2.IsEnabled("alpha"));
            Assert.True(store2.IsEnabled("beta"));
            Assert.True(store2.IsEnabled("gamma")); // 未记录仍启用
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void EnableStore_损坏文件_静默回退为空()
    {
        var path = Path.Combine(Path.GetTempPath(), "mew-plugins-bad-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ bad json");
            var store = new PluginEnableStore(path);
            store.Load();
            Assert.True(store.IsEnabled("any")); // 回退为空，视为启用
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FilterLoadable_仅有效且启用项可加载()
    {
        var manifests = new[]
        {
            new PluginDescriptor(new PluginManifest{ Id="a", DisplayName="A", Version="0.1.0", Entry=new PluginEntry{ Type="exe", Path="a.exe"}}, "p/a", []),
            new PluginDescriptor(new PluginManifest{ Id="b", DisplayName="B", Version="0.1.0", Entry=new PluginEntry{ Type="exe", Path="b.exe"}}, "p/b", ["bad"]),
            new PluginDescriptor(new PluginManifest{ Id="c", DisplayName="C", Version="0.1.0", Entry=new PluginEntry{ Type="exe", Path="c.exe"}}, "p/c", [], true), // duplicate
        };
        var store = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-tmp-" + Guid.NewGuid() + ".json"));
        store.Load();
        store.SetEnabled("a", true);
        store.SetEnabled("c", true); // c 虽启用但 duplicate 仍不可加载

        var loadable = PluginDiscovery.FilterLoadable(manifests, store);
        Assert.Single(loadable);
        Assert.Equal("a", loadable[0].Id);
    }

    [Fact]
    public void Health_NeedsJit_在AOT下置灰()
    {
        var desc = new PluginDescriptor(new PluginManifest{ Id="dll-plug", DisplayName="DLL", Version="0.1.0", Entry=new PluginEntry{ Type="dll", Path="x.dll"}}, "p/x", []);
        Assert.Equal(PluginHealth.NeedsJit, desc.Health(isJitAvailable: false));
        Assert.Equal(PluginHealth.Healthy, desc.Health(isJitAvailable: true));
        var invalid = new PluginDescriptor(new PluginManifest{ Id="bad", DisplayName="Bad", Version="0.1.0", Entry=new PluginEntry{ Type="exe", Path="a.exe"}}, "p/bad", ["err"]);
        Assert.Equal(PluginHealth.InvalidManifest, invalid.Health(true));
    }

    private static void WriteManifest(string path, string id, string display, string version, string type, string entryPath)
    {
        var m = new PluginManifest
        {
            Id = id,
            DisplayName = display,
            Version = version,
            Entry = new PluginEntry { Type = type, Path = entryPath },
            ProtocolVersion = 1
        };
        var json = JsonSerializer.Serialize(m, PluginJsonContext.Default.PluginManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
