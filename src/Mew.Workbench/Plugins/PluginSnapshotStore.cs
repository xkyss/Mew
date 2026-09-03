using System.Text.Json;

namespace Mew.Workbench.Plugins;

/// <summary>
/// 插件快照条目：清单 + 来源路径 + 校验状态的可序列化形态（宿主写、扩展主机读）。
/// </summary>
public sealed class PluginSnapshotEntry
{
    public PluginManifest Manifest { get; set; } = new();
    public string ManifestPath { get; set; } = "";
    public List<string> ValidationErrors { get; set; } = [];
    public bool IsDuplicate { get; set; }

    public PluginDescriptor ToDescriptor() => new(Manifest, ManifestPath, ValidationErrors, IsDuplicate);

    public static PluginSnapshotEntry FromDescriptor(PluginDescriptor descriptor) => new()
    {
        Manifest = descriptor.Manifest,
        ManifestPath = descriptor.ManifestPath,
        ValidationErrors = [.. descriptor.ValidationErrors],
        IsDuplicate = descriptor.IsDuplicate,
    };
}

/// <summary>
/// 发现快照持久化：宿主扫描后写入唯一来源，扩展主机优先按快照加载；
/// 快照缺失或损坏回退本地扫描，保证扩展主机双击独立可用。
/// 宿主保留身份永不进入消费端列表。损坏文件静默回退为空。
/// </summary>
public sealed class PluginSnapshotStore
{
    public PluginSnapshotStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultPath;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "plugin-snapshot.json");

    public string FilePath { get; }

    public void Save(IReadOnlyList<PluginDescriptor> descriptors)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var entries = descriptors.Select(PluginSnapshotEntry.FromDescriptor).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, PluginJsonContext.Default.ListPluginSnapshotEntry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 静默：快照写失败不影响宿主常驻，扩展主机回退本地扫描
        }
    }

    public IReadOnlyList<PluginDescriptor> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var json = File.ReadAllText(FilePath);
            var entries = JsonSerializer.Deserialize(json, PluginJsonContext.Default.ListPluginSnapshotEntry);
            if (entries == null) return [];
            return entries
                .Where(e => !PluginDiscovery.IsReservedHostId(e.Manifest.Id))
                .Select(e => e.ToDescriptor())
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
