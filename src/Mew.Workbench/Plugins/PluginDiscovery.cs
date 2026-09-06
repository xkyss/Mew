using System.Text.Json;

namespace Mew.Workbench.Plugins;

/// <summary>
/// 插件发现：扫描多个根目录下的 `&lt;id&gt;/plugin.json`（递归一层），
/// 容错单清单损坏与 id 冲突，先发现者为准、后发现的重复 id 标记为 Duplicate。
/// 根目录支持两种形态：含 `plugin.json` 的单个插件目录，或含多个 `&lt;id&gt;/` 子目录的根目录。
/// </summary>
public sealed class PluginDiscovery
{
    /// <summary>默认插件主目录:%APPDATA%\Mew\Plugins（未配置 settings.pluginDir 时的唯一扫描根,ADR-000301）。</summary>
    public static string DefaultUserPluginsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "Plugins");

    /// <summary>
    /// 发现单个主目录下的全部插件：根本身含 plugin.json 时直接解析（单个插件目录形态），
    /// 否则扫描一层 <c>&lt;id&gt;/</c> 子目录——**链接子目录（junction/symlink）同样被枚举与穿透**，
    /// 开发期把构建输出以链接挂入主目录即完成联调（ADR-000301）。
    /// 容错单清单损坏与 id 冲突，先发现者为准、后发现的重复 id 标记为 Duplicate（子目录按名称序）。
    /// </summary>
    public IReadOnlyList<PluginDescriptor> Discover(string root)
    {
        var all = new List<PluginDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDescriptor(string dir, string manifestPath)
        {
            PluginManifest? manifest = null;
            IReadOnlyList<string> errors;
            try
            {
                var json = File.ReadAllText(manifestPath);
                manifest = JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginManifest);
                if (manifest == null)
                    errors = ["plugin.json 解析为空"];
                else
                    errors = manifest.Validate();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 损坏文件：构造占位清单以便在列表中标红
                var fallbackId = Path.GetFileName(dir);
                manifest = new PluginManifest { Id = fallbackId, DisplayName = fallbackId, Version = "0.0.0", Entry = new PluginEntry { Type = "exe", Path = "missing.exe" } };
                errors = [$"plugin.json 解析失败：{ex.Message}"];
            }

            var id = manifest!.Id;
            var isDuplicate = !string.IsNullOrWhiteSpace(id) && !seen.Add(id);
            // 重复时追加错误描述，保持 IsValid=false
            var finalErrors = isDuplicate
                ? errors.Concat(["id 重复（已被占用）：" + id]).ToList()
                : errors;

            all.Add(new PluginDescriptor(manifest, manifestPath, finalErrors, isDuplicate));
        }

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return all;

        // 单个插件目录：本身即含 plugin.json（如链接进来的构建输出），直接解析
        var directManifest = Path.Combine(root, "plugin.json");
        if (File.Exists(directManifest))
        {
            AddDescriptor(root, directManifest);
            return all;
        }

        string[] subDirs;
        try { subDirs = Directory.GetDirectories(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return all; }

        foreach (var dir in subDirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;
            AddDescriptor(dir, manifestPath);
        }

        return all;
    }

    /// <summary>宿主保留身份：扩展主机作为特殊容器永不进入插件列表。</summary>
    public static bool IsReservedHostId(string id) =>
        string.Equals(id, "mew-host", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "mew-plugin-host", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否属扩展主机（T2 DLL 插件）管辖：排除保留容器与 exe 行（T3 独立进程由宿主管理，ADR-000202/000203）。
    /// 扩展主机设置→插件列表以此过滤；宿主侧反之只列 exe 行。
    /// </summary>
    public static bool IsExtensionHostManaged(PluginDescriptor desc) =>
        !IsReservedHostId(desc.Id)
        && !string.Equals(desc.Manifest.Entry.Type, "exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>结合启用态，返回最终可加载集合（有效且已启用）。</summary>
    public static IReadOnlyList<PluginDescriptor> FilterLoadable(IReadOnlyList<PluginDescriptor> discovered, PluginEnableStore enableStore)
    {
        return discovered.Where(d => d.IsValid && enableStore.IsEnabled(d.Id)).ToList();
    }

    /// <summary>
    /// 宿主侧可加载集合：仅独立进程插件（entry.type=exe）。
    /// 运行期 DLL 由扩展主机按同一来源代管执行，宿主侧仅置灰提示，不加载。
    /// </summary>
    public static IReadOnlyList<PluginDescriptor> FilterExeLoadable(IReadOnlyList<PluginDescriptor> discovered, PluginEnableStore enableStore)
    {
        return discovered.Where(d => d.IsValid
            && enableStore.IsEnabled(d.Id)
            && !string.Equals(d.Manifest.Entry.Type, "dll", StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
