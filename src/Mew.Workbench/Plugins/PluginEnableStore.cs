using System.Text.Json;

namespace Mew.Workbench.Plugins;

/// <summary>
/// 启用态持久化：`plugins.json`（`{[id]: enabled}`），与 `settings.json` 分离。
/// 损坏文件静默回退为空（全部视为启用），与 SettingsService 损坏回退一致。
/// </summary>
public sealed class PluginEnableStore
{
    private Dictionary<string, bool> _map = new(StringComparer.OrdinalIgnoreCase);

    public PluginEnableStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "plugins.json");
    }

    public string FilePath { get; }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _map = new(StringComparer.OrdinalIgnoreCase);
                return;
            }
            var json = File.ReadAllText(FilePath);
            var dict = JsonSerializer.Deserialize(json, PluginJsonContext.Default.DictionaryStringBoolean);
            _map = dict != null ? new Dictionary<string, bool>(dict, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _map = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(_map, PluginJsonContext.Default.DictionaryStringBoolean);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 静默：与 SettingsService 保存失败一致
        }
    }

    /// <summary>是否启用；未记录的 id 视为启用（默认启用）。</summary>
    public bool IsEnabled(string id) => !_map.TryGetValue(id, out var enabled) || enabled;

    public void SetEnabled(string id, bool enabled)
    {
        _map[id] = enabled;
    }

    public IReadOnlyDictionary<string, bool> Snapshot => _map;
}
