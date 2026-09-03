using System.Text.Json;

namespace Mew.Workbench.Plugins;

/// <summary>
/// 插件发现：扫描用户目录与安装目录下的 `Plugins/&lt;id&gt;/plugin.json`（递归一层），
/// 容错单清单损坏与 id 冲突，后发现的重复 id 标记为 Duplicate。
/// </summary>
public sealed class PluginDiscovery
{
    /// <summary>发现并校验所有清单，返回按发现顺序的描述符列表。</summary>
    public IReadOnlyList<PluginDescriptor> Discover(string userPluginsDir, string installPluginsDir)
    {
        var all = new List<PluginDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

            foreach (var dir in subDirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath)) continue;

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
        }

        Scan(userPluginsDir);
        Scan(installPluginsDir);

        return all;
    }

    /// <summary>结合启用态，返回最终可加载集合（有效且已启用）。</summary>
    public static IReadOnlyList<PluginDescriptor> FilterLoadable(IReadOnlyList<PluginDescriptor> discovered, PluginEnableStore enableStore)
    {
        return discovered.Where(d => d.IsValid && enableStore.IsEnabled(d.Id)).ToList();
    }
}
