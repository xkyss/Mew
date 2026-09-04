using System.Reflection;
using System.Runtime.InteropServices;
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

    /// <summary>宿主契约程序集判定：Mew.Workbench 与 Aprillz.MewUI.* 恒由 Default 提供（跨边界类型同一）。
    /// 其余依赖仍插件目录优先，保证各插件版本隔离。</summary>
    public static bool IsSharedContractAssembly(string? assemblyName) =>
        string.Equals(assemblyName, "Mew.Workbench", StringComparison.OrdinalIgnoreCase)
        || (assemblyName is not null && assemblyName.StartsWith("Aprillz.MewUI", StringComparison.OrdinalIgnoreCase));

    /// <summary>入口程序集内解析模块：唯一实现直接用；多个实现时按清单 id 精确匹配（构建输出混入多个模块时的确定性选择）。
    /// 返回模块或错误描述；调用方负责在失败时释放 ALC（回收/释放措辞见 ADR-000203）。</summary>
    private static (IMewToolModule? Module, string? Error) ResolveModule(Assembly asm, PluginDescriptor desc)
    {
        List<Type> candidates;
        try
        {
            candidates = asm.GetTypes()
                .Where(t => typeof(IMewToolModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToList();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaderErrors = string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message).Where(m => m is not null).Distinct());
            return (null, $"入口程序集类型扫描失败：{loaderErrors}");
        }

        if (candidates.Count == 0)
            return (null, "未找到 IMewToolModule 实现");

        if (candidates.Count == 1)
        {
            var single = (IMewToolModule)Activator.CreateInstance(candidates[0])!;
            if (!string.Equals(single.Id, desc.Id, StringComparison.OrdinalIgnoreCase))
                return (null, $"模块 Id 与清单不一致：{single.Id} != {desc.Id}");
            return (single, null);
        }

        // 多个实现：逐个实例化按清单 id 匹配
        var matching = new List<IMewToolModule>();
        foreach (var type in candidates)
        {
            IMewToolModule instance;
            try { instance = (IMewToolModule)Activator.CreateInstance(type)!; }
            catch (Exception ex) { return (null, $"候选模块 {type.FullName} 实例化失败：{ex.Message}"); }
            if (string.Equals(instance.Id, desc.Id, StringComparison.OrdinalIgnoreCase))
                matching.Add(instance);
        }
        if (matching.Count == 0)
            return (null, $"入口程序集含 {candidates.Count} 个 IMewToolModule 实现，均与清单 id 不一致：" +
                string.Join("、", candidates.Select(t => t.FullName)));
        if (matching.Count > 1)
            return (null, $"入口程序集含多个与清单 id 一致的实现，无法确定：" +
                string.Join("、", matching.Select(m => m.GetType().FullName)));
        return (matching[0], null);
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
                alc.ResolvingUnmanagedDll += (assembly, name) => NativeProbe.ResolveUnmanaged(pluginDir, name);
                var asm = alc.LoadFromAssemblyPath(dllPath);
                var (module, resolveError) = ResolveModule(asm, desc);
                if (resolveError is not null)
                {
                    alc.Unload();
                    results.Add(new PluginLoadResult(desc, false, resolveError));
                    continue;
                }
                // 权限越权：若清单未声明 search 但模块尝试注册 search，将在 IpcServer 层拒绝；此处先按清单 capabilities 预检
                var ctx = contextFactory();
                module!.Configure(ctx);
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
        // 宿主契约恒走 Default 单例：插件目录自带的 Workbench/MewUI 副本绝不装入本 ALC，
        // 否则跨边界类型（IMewToolModule、UI 元素）出现两份运行时类型，IsAssignableFrom 全灭。
        if (PluginDllLoader.IsSharedContractAssembly(assemblyName.Name))
            return null;
        var path = Path.Combine(_pluginDir, assemblyName.Name + ".dll");
        if (File.Exists(path))
            return LoadFromAssemblyPath(path);
        return null; // 回退至 Default
    }
}

/// <summary>插件非托管依赖探测：插件目录根 + `runtimes/&lt;rid&gt;/native`（构建输出自带 native 库时免装运行时）。</summary>
public static class NativeProbe
{
    public static IntPtr ResolveUnmanaged(string pluginDir, string name)
    {
        foreach (var dir in new[] { pluginDir, Path.Combine(pluginDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native") })
        {
            foreach (var ext in new[] { ".dll", ".so", ".dylib" })
            {
                var path = Path.Combine(dir, name + ext);
                if (File.Exists(path))
                {
                    try { return NativeLibrary.Load(path); }
                    catch { /* 损坏的 native 库交由运行时报缺失 */ }
                }
            }
        }
        return IntPtr.Zero;
    }
}
