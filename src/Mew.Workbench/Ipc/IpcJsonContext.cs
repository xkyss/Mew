using System.Text.Json.Serialization;

namespace Mew.Workbench.Ipc;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false)]
[JsonSerializable(typeof(RegisterMessage))]
[JsonSerializable(typeof(RegisterAckMessage))]
[JsonSerializable(typeof(SearchRequestMessage))]
[JsonSerializable(typeof(SearchResponseMessage))]
[JsonSerializable(typeof(ActivateMessage))]
[JsonSerializable(typeof(PingMessage))]
[JsonSerializable(typeof(PongMessage))]
[JsonSerializable(typeof(ShutdownMessage))]
[JsonSerializable(typeof(LogMessage))]
[JsonSerializable(typeof(HotkeyRegisterMessage))]
[JsonSerializable(typeof(HotkeyRegisterAckMessage))]
[JsonSerializable(typeof(HotkeyTriggeredMessage))]
[JsonSerializable(typeof(SettingsChangedMessage))]
[JsonSerializable(typeof(SearchResultDto))]
[JsonSerializable(typeof(List<SearchResultDto>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class IpcJsonContext : JsonSerializerContext
{
}
