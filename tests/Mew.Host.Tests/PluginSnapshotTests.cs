using System.IO;
using System.Text.Json;
using Mew.Workbench;
using Mew.Workbench.Plugins;
using Xunit;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host.Tests;

/// <summary>
/// 发现快照测试（票据 04）：宿主写唯一来源、扩展主机按单加载；
/// 损坏或缺失回退为空（调用方回退本地扫描），宿主保留身份永不进列表。
/// 纯文件与 JSON 缝，可无头复现。
/// </summary>
public class PluginSnapshotTests
{
    [Fact]
    public void Roundtrip_扫描保存加载_视图一致_错误与重复保留()
    {
        var userDir = Tmp("snap-user-");
        var installDir = Tmp("snap-inst-");
        try
        {
            WriteManifest(Path.Combine(userDir, "alpha", "plugin.json"), "alpha", "Alpha", "0.1.0", "exe", "a.exe");
            WriteManifest(Path.Combine(userDir, "dup-a", "plugin.json"), "dup", "DupA", "0.1.0", "dll", "a.dll");
            WriteManifest(Path.Combine(userDir, "dup-b", "plugin.json"), "dup", "DupB", "0.1.0", "dll", "b.dll");
            Directory.CreateDirectory(Path.Combine(userDir, "bad"));
            File.WriteAllText(Path.Combine(userDir, "bad", "plugin.json"), "{ not json }");

            var discovered = new PluginDiscovery().Discover(userDir, installDir);
            var snapPath = Path.Combine(Path.GetTempPath(), "mew-snap-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                new PluginSnapshotStore(snapPath).Save(discovered);
                var loaded = new PluginSnapshotStore(snapPath).Load();

                Assert.Equal(discovered.Count, loaded.Count);
                foreach (var orig in discovered)
                {
                    var back = loaded.First(l => l.ManifestPath == orig.ManifestPath);
                    Assert.Equal(orig.Id, back.Id);
                    Assert.Equal(orig.IsValid, back.IsValid);
                    Assert.Equal(orig.IsDuplicate, back.IsDuplicate);
                    Assert.Equal(orig.ValidationErrors, back.ValidationErrors);
                }
                // 快照消费等价：exe 子集选择器在快照上结果一致
                var store = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-snap-en-" + Guid.NewGuid() + ".json"));
                store.Load();
                var viaSnapshot = PluginDiscovery.FilterExeLoadable(loaded, store).Select(d => d.Id);
                var direct = PluginDiscovery.FilterExeLoadable(discovered, store).Select(d => d.Id);
                Assert.Equal(direct, viaSnapshot);
            }
            finally { if (File.Exists(snapPath)) File.Delete(snapPath); }
        }
        finally { Directory.Delete(userDir, true); Directory.Delete(installDir, true); }
    }

    [Fact]
    public void Load_缺失或损坏_回退为空()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mew-snap-missing-" + Guid.NewGuid().ToString("N") + ".json");
        Assert.Empty(new PluginSnapshotStore(missing).Load());

        var corrupt = Path.Combine(Path.GetTempPath(), "mew-snap-corrupt-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(corrupt, "{ bad json");
            Assert.Empty(new PluginSnapshotStore(corrupt).Load());
        }
        finally { if (File.Exists(corrupt)) File.Delete(corrupt); }
    }

    [Fact]
    public void Load_宿主保留身份_永不进列表()
    {
        var userDir = Tmp("snap-res-");
        var installDir = Tmp("snap-res-inst-");
        try
        {
            WriteManifest(Path.Combine(userDir, "host-impersonate", "plugin.json"), "mew-plugin-host", "冒名", "0.1.0", "exe", "h.exe");
            WriteManifest(Path.Combine(userDir, "real", "plugin.json"), "real", "Real", "0.1.0", "exe", "r.exe");

            var discovered = new PluginDiscovery().Discover(userDir, installDir);
            Assert.Contains(discovered, d => d.Id == "mew-plugin-host");

            var snapPath = Path.Combine(Path.GetTempPath(), "mew-snap-res-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                new PluginSnapshotStore(snapPath).Save(discovered);
                var loaded = new PluginSnapshotStore(snapPath).Load();
                Assert.DoesNotContain(loaded, d => PluginDiscovery.IsReservedHostId(d.Id));
                Assert.Contains(loaded, d => d.Id == "real");
            }
            finally { if (File.Exists(snapPath)) File.Delete(snapPath); }
        }
        finally { Directory.Delete(userDir, true); Directory.Delete(installDir, true); }
    }

    [Fact]
    public void Loader_按快照名单加载_单项失败隔离()
    {
        var userDir = Tmp("snap-load-");
        var installDir = Tmp("snap-load-inst-");
        try
        {
            WriteManifest(Path.Combine(userDir, "miss", "plugin.json"), "miss", "Miss", "0.1.0", "dll", "missing.dll");
            var discovered = new PluginDiscovery().Discover(userDir, installDir);

            var snapPath = Path.Combine(Path.GetTempPath(), "mew-snap-load-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                new PluginSnapshotStore(snapPath).Save(discovered);
                var loaded = new PluginSnapshotStore(snapPath).Load();

                var enables = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-snap-load-en-" + Guid.NewGuid() + ".json"));
                enables.Load();
                var loader = new Mew.PluginHost.PluginDllLoader();
                var results = loader.Load(loaded, enables, CreateContext);
                Assert.Single(results);
                Assert.False(results[0].Success);
                Assert.Empty(loader.Loaded);
            }
            finally { if (File.Exists(snapPath)) File.Delete(snapPath); }
        }
        finally { Directory.Delete(userDir, true); Directory.Delete(installDir, true); }
    }

    private static string Tmp(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ToolModuleContext CreateContext()
    {
        var wb = new WorkbenchType();
        return new ToolModuleContext(wb, IntPtr.Zero, null, new HotkeyService(),
            new SettingsService(Path.Combine(Path.GetTempPath(), "a.json")), new FakeOverlay(), wb.ThemeContext, new SettingsSectionRegistry());
    }

    private sealed class FakeOverlay : IOverlayService { public void AddSearchSource(ISearchSource s) { } }

    private static void WriteManifest(string path, string id, string display, string version, string type, string entryPath)
    {
        var m = new PluginManifest { Id = id, DisplayName = display, Version = version, Entry = new PluginEntry { Type = type, Path = entryPath }, ProtocolVersion = 1 };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(m, PluginJsonContext.Default.PluginManifest));
    }
}
