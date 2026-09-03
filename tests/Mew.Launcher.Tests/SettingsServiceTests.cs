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
    public void Load_模块节类型不符_ReadSection回退null_不抛()
    {
        // 评审修复:模块节字段类型不符(itemsViewMode 为数字)时 ReadSection 静默回退 null
        File.WriteAllText(TempFile("settings.json"), """{ "launcher": { "itemsViewMode": 123 } }""");

        var loaded = new SettingsService(TempFile("settings.json"));
        loaded.Load();

        Assert.Null(loaded.ReadSection<TestModuleSettings>("launcher", TestModuleSettingsContext.Default.TestModuleSettings));
    }
}
