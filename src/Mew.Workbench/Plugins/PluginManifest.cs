using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mew.Workbench.Plugins;

/// <summary>
/// 插件清单模型，对应 `plugin.json` 声明式契约。
/// AOT/Trim 友好：通过源生成上下文序列化，不使用反射。
/// </summary>
public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("entry")]
    public PluginEntry Entry { get; set; } = new();

    [JsonPropertyName("capabilities")]
    public PluginCapabilities? Capabilities { get; set; }

    [JsonPropertyName("permissions")]
    public List<string>? Permissions { get; set; }

    [JsonPropertyName("protocolVersion")]
    public int? ProtocolVersion { get; set; }

    /// <summary>清单校验，返回错误描述列表；空列表即合法。</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!PluginIdValidator.IsValid(Id))
            errors.Add($"id 非法（需 kebab-case）：{Id}");

        if (string.IsNullOrWhiteSpace(DisplayName))
            errors.Add("displayName 不能为空");

        if (!PluginVersionValidator.IsValid(Version))
            errors.Add($"version 非法（需 semver）：{Version}");

        if (Entry == null)
            errors.Add("entry 缺失");
        else
        {
            var entryErrors = Entry.Validate();
            foreach (var e in entryErrors) errors.Add(e);
        }

        if (Capabilities != null)
        {
            var capErrors = Capabilities.Validate();
            foreach (var e in capErrors) errors.Add(e);
        }

        if (Permissions != null)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search", "settings", "hotkeys" };
            foreach (var p in Permissions)
            {
                if (!allowed.Contains(p))
                    errors.Add($"permissions 非法：{p}");
            }

            // 权限与能力一致性：声明了权限但未声明对应 capability 视为警告？此处仅校验权限本身合法，越权在 register 阶段拒绝
        }

        if (ProtocolVersion.HasValue && ProtocolVersion.Value != Mew.Workbench.Ipc.IpcProtocol.CurrentVersion)
            errors.Add($"协议版本不匹配（期望 {Mew.Workbench.Ipc.IpcProtocol.CurrentVersion}，实际 {ProtocolVersion}）：请更新插件/宿主");

        return errors;
    }

    /// <summary>是否需要 JIT 扩展主机（DLL 插件）。</summary>
    public bool RequiresJit => string.Equals(Entry?.Type, "dll", StringComparison.OrdinalIgnoreCase);
}

public sealed class PluginEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("args")]
    public string? Args { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!string.Equals(Type, "dll", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Type, "exe", StringComparison.OrdinalIgnoreCase))
            errors.Add($"entry.type 非法（需 dll/exe）：{Type}");

        if (string.IsNullOrWhiteSpace(Path))
            errors.Add("entry.path 不能为空");
        else
        {
            if (string.Equals(Type, "dll", StringComparison.OrdinalIgnoreCase) && !Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                errors.Add($"entry.path 与 type=dll 不匹配：{Path}");
            if (string.Equals(Type, "exe", StringComparison.OrdinalIgnoreCase) && !Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                errors.Add($"entry.path 与 type=exe 不匹配：{Path}");
        }
        return errors;
    }
}

public sealed class PluginCapabilities
{
    [JsonPropertyName("search")]
    public PluginSearchCapability? Search { get; set; }

    [JsonPropertyName("settingsSection")]
    public PluginSettingsSectionCapability? SettingsSection { get; set; }

    [JsonPropertyName("hotkeys")]
    public List<PluginHotkeyCapability>? Hotkeys { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Search != null)
        {
            if (string.IsNullOrWhiteSpace(Search.ProviderId))
                errors.Add("capabilities.search.providerId 不能为空");
            if (string.IsNullOrWhiteSpace(Search.DisplayName))
                errors.Add("capabilities.search.displayName 不能为空");
        }
        if (SettingsSection != null)
        {
            if (string.IsNullOrWhiteSpace(SettingsSection.Id))
                errors.Add("capabilities.settingsSection.id 不能为空");
            if (string.IsNullOrWhiteSpace(SettingsSection.Title))
                errors.Add("capabilities.settingsSection.title 不能为空");
        }
        if (Hotkeys != null)
        {
            foreach (var hk in Hotkeys)
            {
                if (string.IsNullOrWhiteSpace(hk.Id))
                    errors.Add("capabilities.hotkeys[].id 不能为空");
                // default/label 允许空，由后续热键注册阶段校验格式
            }
        }
        return errors;
    }
}

public sealed class PluginSearchCapability
{
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = "";
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";
}

public sealed class PluginSettingsSectionCapability
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
}

public sealed class PluginHotkeyCapability
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("default")]
    public string? Default { get; set; }
    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

internal static class PluginIdValidator
{
    private static readonly Regex Kebab = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    public static bool IsValid(string id) => !string.IsNullOrWhiteSpace(id) && Kebab.IsMatch(id);
}

internal static class PluginVersionValidator
{
    // 简化 semver：MAJOR.MINOR.PATCH 可选 -prerelease / +build
    private static readonly Regex Semver = new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z-\.]+)?(\+[0-9A-Za-z-\.]+)?$", RegexOptions.Compiled);
    public static bool IsValid(string v) => !string.IsNullOrWhiteSpace(v) && Semver.IsMatch(v);
}
