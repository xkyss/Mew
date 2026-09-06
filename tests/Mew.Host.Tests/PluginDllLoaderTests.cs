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
            var discovered = discovery.Discover(tmp);
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
            var discovered = discovery.Discover(tmp);
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
            var discovered = discovery.Discover(tmp);
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

    [Fact]
    public void Load_入口多实现_按清单id精确匹配()
    {
        // 以测试程序集自身为入口 DLL：内含多个 IMewToolModule 实现，验证按清单 id 确定性选择
        var tmp = Path.Combine(Path.GetTempPath(), "mew-dll-multi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "multi"));
        try
        {
            var self = typeof(PluginDllLoaderTests).Assembly.Location;
            File.Copy(self, Path.Combine(tmp, "multi", "plug.dll"), true);
            WriteManifest(Path.Combine(tmp, "multi", "plugin.json"), "second-fake", "Second", "0.1.0", "dll", "plug.dll");

            var discovered = new PluginDiscovery().Discover(tmp);
            var enableStore = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-en-m-" + Guid.NewGuid() + ".json"));
            enableStore.Load();
            var loader = new Mew.PluginHost.PluginDllLoader();
            var results = loader.Load(discovered, enableStore, () => CreateContext());

            Assert.Single(results);
            Assert.True(results[0].Success);
            Assert.Single(loader.Loaded);
            // 同名类型分属不同 ALC 即不同运行时类型：按 Id 与类型名断言
            Assert.Equal("second-fake", loader.Loaded[0].Module.Id);
            Assert.Contains("SecondFakeModule", loader.Loaded[0].Module.GetType().FullName);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Load_入口多实现_id均不符_报错并列出候选()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mew-dll-nomatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "nomatch"));
        try
        {
            var self = typeof(PluginDllLoaderTests).Assembly.Location;
            File.Copy(self, Path.Combine(tmp, "nomatch", "plug.dll"), true);
            WriteManifest(Path.Combine(tmp, "nomatch", "plugin.json"), "ghost", "Ghost", "0.1.0", "dll", "plug.dll");

            var discovered = new PluginDiscovery().Discover(tmp);
            var enableStore = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-en-g-" + Guid.NewGuid() + ".json"));
            enableStore.Load();
            var loader = new Mew.PluginHost.PluginDllLoader();
            var results = loader.Load(discovered, enableStore, () => CreateContext());

            Assert.Single(results);
            Assert.False(results[0].Success);
            Assert.Contains("均与清单 id 不一致", results[0].Error!);
            Assert.Empty(loader.Loaded);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Load_入口多实现_id重复_报歧义()
    {
        // FakeModule 与 DuplicateIdModule 的 Id 均为 todo
        var tmp = Path.Combine(Path.GetTempPath(), "mew-dll-amb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "amb"));
        try
        {
            var self = typeof(PluginDllLoaderTests).Assembly.Location;
            File.Copy(self, Path.Combine(tmp, "amb", "plug.dll"), true);
            WriteManifest(Path.Combine(tmp, "amb", "plugin.json"), "todo", "Todo", "0.1.0", "dll", "plug.dll");

            var discovered = new PluginDiscovery().Discover(tmp);
            var enableStore = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-en-a-" + Guid.NewGuid() + ".json"));
            enableStore.Load();
            var loader = new Mew.PluginHost.PluginDllLoader();
            var results = loader.Load(discovered, enableStore, () => CreateContext());

            Assert.Single(results);
            Assert.False(results[0].Success);
            Assert.Contains("多个", results[0].Error!);
            Assert.Empty(loader.Loaded);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void ResolveUnmanaged_缺失或损坏_返回零()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mew-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Assert.Equal(IntPtr.Zero, Mew.PluginHost.NativeProbe.ResolveUnmanaged(tmp, "no-such-lib"));
            File.WriteAllText(Path.Combine(tmp, "broken.dll"), "not a native lib");
            Assert.Equal(IntPtr.Zero, Mew.PluginHost.NativeProbe.ResolveUnmanaged(tmp, "broken"));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Load_目录自带Workbench副本_仍用宿主契约_加载成功()
    {
        // 构建输出的真实形状：入口 DLL + 宿主契约副本同目录；契约必须恒走 Default，否则跨边界类型双份导致找不到实现
        var tmp = Path.Combine(Path.GetTempPath(), "mew-dll-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "shared"));
        try
        {
            var self = typeof(PluginDllLoaderTests).Assembly.Location;
            File.Copy(self, Path.Combine(tmp, "shared", "plug.dll"), true);
            var contract = typeof(IMewToolModule).Assembly.Location;
            Assert.True(File.Exists(contract));
            File.Copy(contract, Path.Combine(tmp, "shared", "Mew.Workbench.dll"), true);
            WriteManifest(Path.Combine(tmp, "shared", "plugin.json"), "second-fake", "Second", "0.1.0", "dll", "plug.dll");

            var discovered = new PluginDiscovery().Discover(tmp);
            var enableStore = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-en-s-" + Guid.NewGuid() + ".json"));
            enableStore.Load();
            var loader = new Mew.PluginHost.PluginDllLoader();
            var results = loader.Load(discovered, enableStore, () => CreateContext());

            Assert.Single(results);
            Assert.True(results[0].Success, results[0].Error);
            Assert.Single(loader.Loaded);
            Assert.Equal("second-fake", loader.Loaded[0].Module.Id);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void IsSharedContractAssembly_契约与普通依赖区分()
    {
        Assert.True(Mew.PluginHost.PluginDllLoader.IsSharedContractAssembly("Mew.Workbench"));
        Assert.True(Mew.PluginHost.PluginDllLoader.IsSharedContractAssembly("mew.workbench"));
        Assert.True(Mew.PluginHost.PluginDllLoader.IsSharedContractAssembly("Aprillz.MewUI"));
        Assert.True(Mew.PluginHost.PluginDllLoader.IsSharedContractAssembly("Aprillz.MewUI.MewDock"));
        Assert.False(Mew.PluginHost.PluginDllLoader.IsSharedContractAssembly("Mxd.Core"));
        Assert.False(Mew.PluginHost.PluginDllLoader.IsSharedContractAssembly("System.Text.Json"));
        Assert.False(Mew.PluginHost.PluginDllLoader.IsSharedContractAssembly(null));
    }

    private sealed class SecondFakeModule : IMewToolModule
    {
        public string Id => "second-fake";
        public string DisplayName => "第二假模块";
        public void Configure(ToolModuleContext context) { }
    }

    private sealed class DuplicateIdModule : IMewToolModule
    {
        public string Id => "todo"; // 与 FakeModule 重复，验证歧义报错
        public string DisplayName => "重复假模块";
        public void Configure(ToolModuleContext context) { }
    }

    [Fact]
    public void Load_真实Launcher_作为T2加载成功()
    {
        // Launcher 已摘除 T1 编译进扩展主机（标准 T2 插件）：以构建产物实证整条加载链路
        var tmp = Path.Combine(Path.GetTempPath(), "mew-dll-launcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "launcher"));
        try
        {
            var self = typeof(Mew.Launcher.LauncherModule).Assembly.Location;
            Assert.True(File.Exists(self));
            File.Copy(self, Path.Combine(tmp, "launcher", "Mew.Launcher.dll"), true);
            WriteManifest(Path.Combine(tmp, "launcher", "plugin.json"), "launcher", "启动项", "1.0.0", "dll", "Mew.Launcher.dll");

            var discovered = new PluginDiscovery().Discover(tmp);
            var enableStore = new PluginEnableStore(Path.Combine(Path.GetTempPath(), "mew-en-l-" + Guid.NewGuid() + ".json"));
            enableStore.Load();
            var loader = new Mew.PluginHost.PluginDllLoader();
            var results = loader.Load(discovered, enableStore, () => CreateContext());

            Assert.Single(results);
            Assert.True(results[0].Success, results[0].Error);
            Assert.Single(loader.Loaded);
            Assert.Equal("launcher", loader.Loaded[0].Module.Id);
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
