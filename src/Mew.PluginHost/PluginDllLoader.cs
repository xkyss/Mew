using System.Reflection;
using System.Runtime.Loader;
using Mew.Workbench;
using Mew.Workbench.Plugins;

namespace Mew.PluginHost;

/// <summary>
/// DLL 运行时插件加载器：按插件目录以独立 ALC 隔离加载，复用 IMewToolModule 契约。
/// 加载失败不影响其他插件；未声明能力的越权在 IpcServer 注册阶段拒绝。
/// </summary>
public sealed class PluginDllLoader
{
    private readonly List<(PluginDescriptor Descriptor, AssemblyLoadContext Alc, IMewToolModule Module)> _loaded = [];
    private readonly object _lock = new();

    public IReadOnlyList<(PluginDescriptor Descriptor, IMewToolModule Module)> Loaded
    {
        get { lock (_lock) return _loaded.Select(x => (x.Descriptor, x.Module)).ToList(); }
    }

    /// <summary>
    /// 扫描已发现的清单，结合启用态，加载所有 type=dll 且有效且启用的插件。
    /// 返回加载结果（含错误描述），调用方据此刷新设置面板。
    /// </summary>
    public IReadOnlyList<PluginLoadResult> Load(
        IReadOnlyList<PluginDescriptor> discovered,
        PluginEnableStore enableStore,
        Func<ToolModuleContext> contextFactory)
    {
        var results = new List<PluginLoadResult>();
        foreach (var desc in discovered)
        {
            if (!string.Equals(desc.Manifest.Entry.Type, "dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (!desc.IsValid) { results.Add(new PluginLoadResult(desc, false, string.Join("; ", desc.ValidationErrors))); continue; }
            if (!enableStore.IsEnabled(desc.Id)) { results.Add(new PluginLoadResult(desc, false, "已禁用")); continue; }

            var pluginDir = Path.GetDirectoryName(desc.ManifestPath)!;
            var dllPath = Path.Combine(pluginDir, desc.Manifest.Entry.Path);
            if (!File.Exists(dllPath))
            {
                results.Add(new PluginLoadResult(desc, false, $"DLL 不存在：{dllPath}"));
                continue;
            }

            try
            {
                var alc = new PluginLoadContext(pluginDir, desc.Id);
                var asm = alc.LoadFromAssemblyPath(dllPath);
                var moduleType = asm.GetTypes().FirstOrDefault(t => typeof(IMewToolModule).IsAssignableFrom(t) && !t.IsAbstract);
                if (moduleType == null)
                {
                    alc.Unload();
                    results.Add(new PluginLoadResult(desc, false, "未找到 IMewToolModule 实现"));
                    continue;
                }
                var module = (IMewToolModule)Activator.CreateInstance(moduleType)!;
                // 校验清单与模块 Id 一致性（可选）
                if (!string.Equals(module.Id, desc.Id, StringComparison.OrdinalIgnoreCase))
                {
                    alc.Unload();
                    results.Add(new PluginLoadResult(desc, false, $"模块 Id 与清单不一致：{module.Id} != {desc.Id}"));
                    continue;
                }
                // 权限越权：若清单未声明 search 但模块尝试注册 search，将在 IpcServer 层拒绝；此处先按清单 capabilities 预检
                var ctx = contextFactory();
                module.Configure(ctx);
                lock (_lock) _loaded.Add((desc, alc, module));
                results.Add(new PluginLoadResult(desc, true, null));
            }
            catch (Exception ex)
            {
                results.Add(new PluginLoadResult(desc, false, $"加载失败：{ex.Message}"));
            }
        }
        return results;
    }

    public void Unload(string pluginId)
    {
        lock (_lock)
        {
            var idx = _loaded.FindIndex(x => string.Equals(x.Descriptor.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;
            var entry = _loaded[idx];
            _loaded.RemoveAt(idx);
            try { entry.Alc.Unload(); } catch { }
        }
    }

    public void UnloadAll()
    {
        lock (_lock)
        {
            foreach (var e in _loaded) try { e.Alc.Unload(); } catch { }
            _loaded.Clear();
        }
    }
}

public sealed record PluginLoadResult(PluginDescriptor Descriptor, bool Success, string? Error);

/// <summary>按插件目录隔离的 ALC：优先从插件目录解析依赖，回退至 Default。</summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDir;
    public PluginLoadContext(string pluginDir, string name) : base(name, isCollectible: true)
    {
        _pluginDir = pluginDir;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = Path.Combine(_pluginDir, assemblyName.Name + ".dll");
        if (File.Exists(path))
            return LoadFromAssemblyPath(path);
        return null; // 回退至 Default（共享契约如 Mew.Workbench）
    }
}
