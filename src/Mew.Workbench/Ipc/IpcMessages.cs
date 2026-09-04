using System.Text.Json.Serialization;

namespace Mew.Workbench.Ipc;

/// <summary>IPC 协议版本（当前 1）。</summary>
public static class IpcProtocol
{
    public const int CurrentVersion = 1;
    public static string PipeName => $"mew-host-{Environment.UserName}";
}

/// <summary>IPC 消息基类，按 type 区分。</summary>
public abstract record IpcMessage([property: JsonPropertyName("type")] string Type);

public sealed record RegisterMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("capabilities")] PluginCapabilitiesDto? Capabilities,
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion
) : IpcMessage("register");

public sealed record RegisterAckMessage(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error
) : IpcMessage("registerAck");

public sealed record SearchRequestMessage(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("maxResults")] int MaxResults
) : IpcMessage("searchRequest");

public sealed record SearchResponseMessage(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("results")] List<SearchResultDto> Results
) : IpcMessage("searchResponse");

public sealed record ActivateMessage(
    [property: JsonPropertyName("resultId")] string ResultId
) : IpcMessage("activate");

public sealed record PingMessage() : IpcMessage("ping");
public sealed record PongMessage() : IpcMessage("pong");
public sealed record ShutdownMessage() : IpcMessage("shutdown");
public sealed record LogMessage(
    [property: JsonPropertyName("message")] string Message
) : IpcMessage("log");

public sealed record HotkeyRegisterMessage(
    [property: JsonPropertyName("hotkey")] string Hotkey,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("pluginId")] string PluginId
) : IpcMessage("hotkeyRegister");

public sealed record HotkeyRegisterAckMessage(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error
) : IpcMessage("hotkeyRegisterAck");

public sealed record HotkeyTriggeredMessage(
    [property: JsonPropertyName("hotkey")] string Hotkey,
    [property: JsonPropertyName("pluginId")] string PluginId
) : IpcMessage("hotkeyTriggered");

public sealed record OverlayHotkeySetMessage(
    [property: JsonPropertyName("hotkey")] string? Hotkey,
    [property: JsonPropertyName("enabled")] bool Enabled
) : IpcMessage("overlayHotkeySet");

public sealed record OverlayHotkeySetAckMessage(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("hotkey")] string? Hotkey,
    [property: JsonPropertyName("enabled")] bool Enabled
) : IpcMessage("overlayHotkeySetAck");

public sealed record SettingsChangedMessage(
    [property: JsonPropertyName("section")] string Section,
    [property: JsonPropertyName("json")] string Json
) : IpcMessage("settingsChanged");

/// <summary>启用/禁用请求（扩展主机 → 宿主）：插件启用态唯一写者为宿主（ADR-000202/000203），扩展主机不再本地写。</summary>
public sealed record PluginEnableSetMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("enabled")] bool Enabled
) : IpcMessage("pluginEnableSet");

public sealed record PluginEnableSetAckMessage(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("enabled")] bool Enabled
) : IpcMessage("pluginEnableSetAck");

public sealed record PluginCapabilitiesDto(
    [property: JsonPropertyName("search")] SearchCapabilityDto? Search,
    [property: JsonPropertyName("settingsSection")] SettingsSectionDto? SettingsSection,
    [property: JsonPropertyName("hotkeys")] List<HotkeyDto>? Hotkeys
);

public sealed record SearchCapabilityDto(
    [property: JsonPropertyName("providerId")] string ProviderId,
    [property: JsonPropertyName("displayName")] string DisplayName
);

public sealed record SettingsSectionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title
);

public sealed record HotkeyDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("default")] string? Default,
    [property: JsonPropertyName("label")] string? Label
);

public sealed record SearchResultDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("subtitle")] string Subtitle,
    [property: JsonPropertyName("sourceId")] string SourceId,
    [property: JsonPropertyName("sourceDisplayName")] string SourceDisplayName
);
