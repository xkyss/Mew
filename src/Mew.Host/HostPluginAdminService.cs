using Mew.Workbench.Plugins;

namespace Mew.Host;

/// <summary>
/// 宿主侧插件管理 adapter（ADR-000204）：生命周期引擎 + plugins.json 的进程内实现。
/// 引擎锁与启用锁全部收进本类：Snapshot 沿引擎既有纪律取锁（引擎锁 → 启用锁单向）；
/// Apply 的落盘在启用锁内完成、引擎动作在锁外执行（启用锁内不碰引擎，杜绝反向死锁）。
/// IPC 一次性请求与宿主 UI 行动作共用 Apply 同一条加锁路径——plugins.json 无锁写就此绝迹。
/// </summary>
internal sealed class HostPluginAdminService : IPluginAdminService
{
    private readonly PluginLifecycleEngine _engine;
    private readonly PluginEnableStore _enables;
    private readonly HashSet<string> _knownPluginIds;
    private readonly object _enableSync = new();
    private readonly Action<string>? _log;

    public HostPluginAdminService(PluginLifecycleEngine engine, PluginEnableStore enables,
        IEnumerable<string> knownPluginIds, Action<string>? log = null)
    {
        _engine = engine;
        _enables = enables;
        _knownPluginIds = new HashSet<string>(knownPluginIds, StringComparer.OrdinalIgnoreCase);
        _log = log;
    }

    public IReadOnlyList<PluginAdminRow> Snapshot()
    {
        // 引擎 Snapshot 持引擎锁回调 IsEnabledLocked（引擎锁 → 启用锁单向取锁，避免反向死锁）
        var states = _engine.Snapshot(IsEnabledLocked);
        return states.Select(state => new PluginAdminRow(
            state.Descriptor,
            PluginRowState.Derive(state.Enabled, state.Descriptor.Health(isJitAvailable: true), state.Crashed, stillLoaded: false))).ToList();
    }

    public PluginAdminResult Apply(string id, PluginRowAction action)
    {
        if (PluginDiscovery.IsReservedHostId(id) || !_knownPluginIds.Contains(id))
            return PluginAdminResult.Rejected($"未知插件：{id}");
        switch (action)
        {
            // T3（exe）由引擎托管立即生效（启用即拉起、禁用即杀）；未注册引擎的 id（T2 意图）只落盘，Start/Stop 幂等空转
            case PluginRowAction.Enable:
                Persist(id, enabled: true);
                _engine.Start(id, out _);
                _log?.Invoke($"插件已启用（宿主落盘）：{id}");
                return PluginAdminResult.Ok();
            case PluginRowAction.Disable:
                Persist(id, enabled: false);
                _engine.Stop(id);
                _log?.Invoke($"插件已禁用（宿主落盘）：{id}");
                return PluginAdminResult.Ok();
            case PluginRowAction.Restart:
                _engine.Restart(id, out _);
                _log?.Invoke($"独立插件已重启：{id}");
                return PluginAdminResult.Ok();
            default:
                return PluginAdminResult.Rejected("该行不提供动作");
        }
    }

    private void Persist(string id, bool enabled)
    {
        lock (_enableSync)
        {
            _enables.SetEnabled(id, enabled);
            _enables.Save();
        }
    }

    private bool IsEnabledLocked(string id)
    {
        lock (_enableSync) return _enables.IsEnabled(id);
    }
}
