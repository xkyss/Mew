using System.Text.Json;

namespace Mew.Workbench.Plugins;

/// <summary>
/// 插件发现：扫描多个根目录下的 `&lt;id&gt;/plugin.json`（递归一层），
/// 容错单清单损坏与 id 冲突，先发现者为准、后发现的重复 id 标记为 Duplicate。
/// 根目录支持两种形态：含 `plugin.json` 的单个插件目录，或含多个 `&lt;id&gt;/` 子目录的根目录。
/// </summary>
public sealed class PluginDiscovery
{
    /// <summary>环境变量：额外插件目录（`Path.PathSeparator` 分隔），用于开发期指向构建输出、免复制联调。</summary>
    public const string ExtraDirsEnvVar = "MEW_PLUGINS_EXTRA";

    /// <summary>读取环境变量中的额外插件目录（不存在/不可访问的条目由扫描阶段忽略）。</summary>
    public static IReadOnlyList<string> GetExtraPluginDirs()
    {
        var raw = Environment.GetEnvironmentVariable(ExtraDirsEnvVar);
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>合并完整的扫描根序列：环境变量在前，其次配置列表（为空/缺省时以用户目录为种子），安装目录恒为末尾。
    /// 去空、去重（大小写不敏感）；重复 id 以靠前的目录为准。</summary>
    public static IReadOnlyList<string> ResolvePluginRoots(IEnumerable<string>? configuredDirs, string userPluginsDir, string installPluginsDir)
    {
        var configured = (configuredDirs ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (configured.Count == 0) configured.Add(userPluginsDir);
        return GetExtraPluginDirs().Concat(configured).Append(installPluginsDir)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>发现并校验所有清单，返回按发现顺序的描述符列表（仅扫描传入的两目录；额外目录由调用方显式传入）。</summary>
    public IReadOnlyList<PluginDescriptor> Discover(string userPluginsDir, string installPluginsDir)
        => Discover([userPluginsDir, installPluginsDir]);

    /// <summary>发现并校验所有清单（多根），顺序即优先级（重复 id 以靠前者为准）。
    /// </summary>
    public IReadOnlyList<PluginDescriptor> Discover(IEnumerable<string> roots)
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

        void Scan(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

            // 单个插件目录：本身即含 plugin.json（如 Mxd 构建输出），直接解析
            var directManifest = Path.Combine(root, "plugin.json");
            if (File.Exists(directManifest))
            {
                AddDescriptor(root, directManifest);
                return;
            }

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

            foreach (var dir in subDirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath)) continue;
                AddDescriptor(dir, manifestPath);
            }
        }

        foreach (var root in roots) Scan(root);

        return all;
    }

    /// <summary>宿主保留身份：扩展主机作为特殊容器永不进入插件列表。</summary>
    public static bool IsReservedHostId(string id) =>
        string.Equals(id, "mew-host", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "mew-plugin-host", StringComparison.OrdinalIgnoreCase);

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
