using System.Text.Json.Serialization;

namespace Mew.Launcher;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LauncherSettings))]
internal sealed partial class LauncherSettingsJsonContext : JsonSerializerContext;

/// <summary>
/// Launcher 工具模块的设置节(settings.json 中 "launcher" 节)。
/// 宿主级设置(主题模式、浮层呼出键)在根节,归宿主;本类是模块自有设置。
/// </summary>
internal sealed class LauncherSettings
{
    /// <summary>启动项列表形态:card(卡片,默认)/ list(列表)。</summary>
    public string? ItemsViewMode { get; set; }
}
