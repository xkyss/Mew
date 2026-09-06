using System.Text.Json.Serialization;
using Mew.Workbench.Plugins;

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

/// <summary>独立插件（T3）行拉取请求（扩展主机 → 宿主，ADR-000303）：行来源权威在宿主（引擎实态 + plugins.json）。</summary>
public sealed record PluginRowsRequestMessage() : IpcMessage("pluginRowsRequest");

/// <summary>行拉取应答：Error 非 null 表示宿主侧无法提供（无处理器等），Rows 仍为空集合。</summary>
public sealed record PluginRowsAckMessage(
    [property: JsonPropertyName("rows")] List<PluginAdminRowDto> Rows,
    [property: JsonPropertyName("error")] string? Error
) : IpcMessage("pluginRowsAck");

/// <summary>独立插件行内动作请求（扩展主机 → 宿主）：enable/disable/restart 由宿主 adapter 统一路由。</summary>
public sealed record PluginAdminActionMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("action")] string Action
) : IpcMessage("pluginAdminAction");

public sealed record PluginAdminActionAckMessage(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("action")] string Action
) : IpcMessage("pluginAdminActionAck");

/// <summary>
/// 插件管理行经 IPC 的可序列化形态：描述符复用宿主快照条目（清单 + 校验状态），
/// 行状态按 ADR-000203 契约以枚举名传输；双向映射只在此处，两侧 adapter 不感知报文形状。
/// </summary>
public sealed record PluginAdminRowDto(
    [property: JsonPropertyName("entry")] PluginSnapshotEntry Entry,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("hint")] string? Hint
)
{
    public PluginAdminRow ToRow() => new(Entry.ToDescriptor(), new PluginRowState(
        Enum.TryParse<PluginRowStatus>(Status, out var status) ? status : PluginRowStatus.Disabled,
        Enum.TryParse<PluginRowAction>(Action, out var action) ? action : PluginRowAction.None,
        Hint));

    public static PluginAdminRowDto FromRow(PluginAdminRow row) => new(
        PluginSnapshotEntry.FromDescriptor(row.Descriptor),
        row.State.Status.ToString(),
        row.State.Action.ToString(),
        row.State.Hint);

    public static bool TryParseAction(string action, out PluginRowAction parsed) =>
        Enum.TryParse(action, ignoreCase: true, out parsed) && parsed != PluginRowAction.None;
}

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
