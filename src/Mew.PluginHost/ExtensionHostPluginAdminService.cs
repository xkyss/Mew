using Mew.Workbench.Plugins;

namespace Mew.PluginHost;

/// <summary>启用/禁用管理缝入口（EnablePluginIpc.TryRequest 与测试替身共用）：ack 报文映射已在 IPC 层完成（ADR-000204 决策 3）。</summary>
internal delegate PluginAdminResult EnableTransport(string id, bool enabled, out string? transportError);

/// <summary>
/// 扩展主机侧插件管理 adapter（ADR-000204）：发现结果 + 本地只读启用缓存 + DLL 装载实态的行来源；
/// 写路径走一次性 IPC 请求宿主落盘（宿主唯一写者，ADR-000203 第 7 条），本地缓存仅在确认后内存同步。
/// 行序排序归共享面板，此处按发现序交出。
/// </summary>
internal sealed class ExtensionHostPluginAdminService : IPluginAdminService
{
    // 扩展主机本身为 JIT，DLL 可加载
    private const bool IsJitAvailable = true;

    private readonly IReadOnlyList<PluginDescriptor> _descriptors;
    private readonly PluginEnableStore _enables;
    private readonly Func<IReadOnlyCollection<string>> _loadedIds;
    private readonly EnableTransport _trySet;

    public ExtensionHostPluginAdminService(IReadOnlyList<PluginDescriptor> descriptors, PluginEnableStore enables,
        Func<IReadOnlyCollection<string>> loadedIds, EnableTransport trySet)
    {
        _descriptors = descriptors;
        _enables = enables;
        _loadedIds = loadedIds;
        _trySet = trySet;
    }

    public IReadOnlyList<PluginAdminRow> Snapshot()
    {
        // 扩展主机只管 DLL 行（exe 行 = T3 独立进程，归宿主管理）；特殊容器经 IsExtensionHostManaged 过滤
        var loaded = _loadedIds();
        return _descriptors.Where(PluginDiscovery.IsExtensionHostManaged)
            .Select(desc => new PluginAdminRow(
                desc,
                PluginRowState.Derive(_enables.IsEnabled(desc.Id), desc.Health(IsJitAvailable),
                    crashed: false, stillLoaded: loaded.Contains(desc.Id))))
            .ToList();
    }

    public PluginAdminResult Apply(string id, PluginRowAction action)
    {
        if (action is not (PluginRowAction.Enable or PluginRowAction.Disable))
            return PluginAdminResult.Rejected("该行不提供动作");
        var target = action == PluginRowAction.Enable;
        var result = _trySet(id, target, out _);
        if (result.Outcome == PluginAdminOutcome.Ok)
        {
            // 宿主已确认落盘（确认值即请求目标）：同步本地意图（内存缓存），T2 生效时机 = 下次主界面启动
            _enables.SetEnabled(id, target);
        }
        return result;
    }
}
