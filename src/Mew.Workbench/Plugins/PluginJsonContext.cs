using System.Text.Json.Serialization;

namespace Mew.Workbench.Plugins;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
public partial class PluginJsonContext : JsonSerializerContext
{
}
