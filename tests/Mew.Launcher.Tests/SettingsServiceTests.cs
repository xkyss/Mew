using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mew.Workbench;
using Xunit;

namespace Mew.Launcher.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TestModuleSettings))]
internal sealed partial class TestModuleSettingsContext : JsonSerializerContext;

internal sealed class TestModuleSettings
{
    public string? ItemsViewMode { get; set; }
}

/// <summary>
/// 分节设置服务:根节(主题/呼出热键)与模块节(按模块 Id)分节读写,旧扁平结构自动迁移。
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mew-settings-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private string TempFile(string name) => Path.Combine(_dir, name);

    [Fact]
    public void SaveThenLoad_根节与模块节往返()
    {
        var service = new SettingsService(TempFile("settings.json"));
        service.ThemeMode = "Dark";
        service.OverlayHotkey = "Ctrl+Shift+Space";
        service.WriteSection("launcher", new TestModuleSettings { ItemsViewMode = "list" }, TestModuleSettingsContext.Default.TestModuleSettings);
        service.Save();

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        Assert.Equal("Dark", loaded.ThemeMode);
        Assert.Equal("Ctrl+Shift+Space", loaded.OverlayHotkey);
        Assert.Equal("list", loaded.ReadSection<TestModuleSettings>("launcher", TestModuleSettingsContext.Default.TestModuleSettings)?.ItemsViewMode);
    }

    [Fact]
    public void Load_无文件_返回缺省()
    {
        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        Assert.Null(loaded.ThemeMode);
        Assert.Null(loaded.ReadSection<TestModuleSettings>("launcher", TestModuleSettingsContext.Default.TestModuleSettings));
    }

    [Fact]
    public void Load_旧扁平结构_自动迁移到模块节()
    {
        File.WriteAllText(TempFile("settings.json"), """{ "themeMode": "Dark", "overlayHotkey": "Ctrl+Alt+Space", "itemsViewMode": "card" }""");

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        Assert.Equal("Dark", loaded.ThemeMode);
        Assert.Equal("Ctrl+Alt+Space", loaded.OverlayHotkey);
        Assert.Equal("card", loaded.ReadSection<TestModuleSettings>("launcher", TestModuleSettingsContext.Default.TestModuleSettings)?.ItemsViewMode);

        // 迁移后回写:新结构(launcher 节)落盘,根节不再有 itemsViewMode
        var rewritten = File.ReadAllText(TempFile("settings.json"));
        Assert.Contains("launcher", rewritten);
        Assert.DoesNotContain("\"itemsViewMode\": \"card\"", rewritten.Split("launcher")[0]);
    }

    [Fact]
    public void Load_手写JSON_未知字段忽略()
    {
        File.WriteAllText(TempFile("settings.json"), """{ "itemsViewMode": "card", "future": true }""");

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        // 未知根字段 future 不影响;旧 itemsViewMode 按迁移规则归 launcher 节
        Assert.Equal("card", loaded.ReadSection<TestModuleSettings>("launcher", TestModuleSettingsContext.Default.TestModuleSettings)?.ItemsViewMode);
    }

    [Fact]
    public void Load_损坏JSON_回退缺省()
    {
        File.WriteAllText(TempFile("settings.json"), "{ not json");

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        Assert.Null(loaded.ThemeMode);
        Assert.Null(loaded.OverlayHotkey);
    }

    [Fact]
    public void Load_根值类型不符_静默回退缺省_不抛()
    {
        // 评审修复:根值类型与字符串不符(如 "overlayHotkey": 123)不再抛 InvalidOperationException 阻塞启动
        File.WriteAllText(TempFile("settings.json"), """{ "themeMode": 123, "overlayHotkey": 456 }""");

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        Assert.Null(loaded.ThemeMode);
        Assert.Null(loaded.OverlayHotkey);
    }

    [Fact]
    public void PluginDir_读写往返_置空移除键()
    {
        var service = new SettingsService(TempFile("settings.json"));
        Assert.Null(service.PluginDir);

        service.PluginDir = @"D:\mew-plugins";
        service.Save();

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();
        Assert.Equal(@"D:\mew-plugins", loaded.PluginDir);

        loaded.PluginDir = null;
        loaded.Save();
        var reloaded = new SettingsService(TempFile("settings.json"));
        reloaded.Load();
        Assert.Null(reloaded.PluginDir);
    }

    [Fact]
    public void Load_旧pluginDirs列表_迁移为单值_余项记入MigratedOut()
    {
        File.WriteAllText(TempFile("settings.json"),
            """{ "pluginDirs": ["C:\\Users\\x\\AppData\\Roaming\\Mew\\Plugins", "\\\\wsl.localhost\\dev\\out"] }""");

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        // 首项成为主目录;其余为旧附加目录,提示人工以链接挂入（ADR-000301）
        Assert.Equal("C:\\Users\\x\\AppData\\Roaming\\Mew\\Plugins", loaded.PluginDir);
        Assert.Equal(["\\\\wsl.localhost\\dev\\out"], loaded.MigratedOutPluginDirs);
        // 旧键移除且落盘(重读仍为迁移后形态)
        var reloaded = new SettingsService(TempFile("settings.json"));
        reloaded.Load();
        Assert.Equal(loaded.PluginDir, reloaded.PluginDir);
        Assert.Empty(reloaded.MigratedOutPluginDirs);
    }

    [Fact]
    public void Load_旧pluginDirs列表_元素类型不符_整列放弃_回退默认()
    {
        File.WriteAllText(TempFile("settings.json"), """{ "pluginDirs": ["ok", 123] }""");

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        Assert.Null(loaded.PluginDir);
        Assert.Empty(loaded.MigratedOutPluginDirs);
    }

    [Fact]
    public void OverlayHotkeyEnabled_缺省启用_读写往返_类型不符回退()
    {
        var service = new SettingsService(TempFile("settings.json"));
        Assert.True(service.OverlayHotkeyEnabled); // 缺省启用

        service.OverlayHotkeyEnabled = false;
        service.Save();
        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();
        Assert.False(loaded.OverlayHotkeyEnabled);

        File.WriteAllText(TempFile("settings.json"), """{ "overlayHotkeyEnabled": "x" }""");
        var broken = new SettingsService(TempFile("settings.json"));
        broken.Load();
        Assert.True(broken.OverlayHotkeyEnabled);
    }

    [Fact]
    public void Load_模块节类型不符_ReadSection回退null_不抛()
    {
        // 评审修复:模块节字段类型不符(itemsViewMode 为数字)时 ReadSection 静默回退 null
        File.WriteAllText(TempFile("settings.json"), """{ "launcher": { "itemsViewMode": 123 } }""");

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        Assert.Null(loaded.ReadSection<TestModuleSettings>("launcher", TestModuleSettingsContext.Default.TestModuleSettings));
    }
}
