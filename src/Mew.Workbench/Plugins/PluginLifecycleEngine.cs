namespace Mew.Workbench.Plugins;

/// <summary>
/// 独立进程插件（T3 exe）的进程句柄契约：宿主以真实 Process 实现，测试以假实现驱动。
/// 引擎不直接持有 System.Diagnostics.Process，保证生命周期逻辑可无进程单测。
/// </summary>
public interface IPluginProcessHandle
{
    /// <summary>进程退出/被杀后触发（宿主在 Process.Exited 中转发）。</summary>
    event Action? Exited;

    /// <summary>请求进程退出（宿主实现 Kill）；不抛即可。</summary>
    void Stop();

    /// <summary>进程是否仍存活（宿主实现 HasExited 取反）。</summary>
    bool IsRunning { get; }
}

/// <summary>
/// T3 独立插件生命周期引擎（ADR-000203）：按启用态拉起 exe 插件、禁用即杀、按崩溃归因标已崩溃。
/// 崩溃态只在运行期推导、不落盘；宿主重启后按启用态重新拉起（新实例状态清零）。
/// 防循环：同一插件启动后短时连续崩溃达阈值 → 引擎不再自动拉起（行标已崩溃待手动重启）；
/// 手动「重启」是唯一恢复路径，新一轮运行成功后计数复位。
/// 引擎只做状态编排，进程真实生死由调用方经 launcher 注入，因此可无子进程单测。
/// </summary>
public sealed class PluginLifecycleEngine
{
    private readonly Func<PluginDescriptor, IPluginProcessHandle?> _launcher;
    private readonly Dictionary<string, PluginRuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>连续崩溃阈值：达到后本次运行不再自动拉起（防循环）。</summary>
    public int CrashLoopThreshold { get; }

    /// <summary>崩溃循环判定窗口：启动后短时（&lt;= 窗口）退出视为同一次循环；间隔超过窗口视为稳定运行，计数复位。</summary>
    public TimeSpan CrashWindow { get; }

    /// <summary>插件运行态变化（拉起/停止/崩溃/防循环锁）后触发，宿主据此刷新插件管理面板。</summary>
    public event Action<string>? PluginStateChanged;

    public PluginLifecycleEngine(Func<PluginDescriptor, IPluginProcessHandle?> launcher, int crashLoopThreshold = 3, TimeSpan? crashWindow = null)
    {
        _launcher = launcher;
        CrashLoopThreshold = crashLoopThreshold;
        CrashWindow = crashWindow ?? TimeSpan.FromSeconds(30);
    }

    private sealed class PluginRuntimeState
    {
        public PluginDescriptor Descriptor = null!;
        public IPluginProcessHandle? Handle;
        public bool Crashed;
        public int ConsecutiveCrashes;
        public DateTimeOffset LastCrashAt;
    }

    /// <summary>运行期推导的每插件状态快照（供 UI/查询；Enabled 由调用方按启用存储现算）。</summary>
    public sealed record PluginState(PluginDescriptor Descriptor, bool Enabled, bool Running, bool Crashed, bool LoopGuarded)
    {
        public string Id => Descriptor.Id;
    }

    /// <summary>查询单插件状态；未纳入引擎管理的 id 返回 null。</summary>
    public PluginState? GetState(string id, Func<string, bool> isEnabled)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(id, out var s)) return null;
            return ToState(s, isEnabled(s.Descriptor.Id));
        }
    }

    public IReadOnlyList<PluginState> Snapshot(Func<string, bool> isEnabled)
    {
        lock (_lock) return _states.Values.Select(s => ToState(s, isEnabled(s.Descriptor.Id))).ToList();
    }

    private PluginState ToState(PluginRuntimeState s, bool enabled)
    {
        var running = !s.Crashed && s.Handle is { IsRunning: true };
        return new PluginState(s.Descriptor, enabled, running, s.Crashed, s.ConsecutiveCrashes >= CrashLoopThreshold);
    }

    /// <summary>注册待托管插件（宿主发现后调用，供 GetState/Snapshot 覆盖全部 exe 项）。</summary>
    public void Register(PluginDescriptor desc)
    {
        lock (_lock)
        {
            if (!_states.ContainsKey(desc.Id))
                _states[desc.Id] = new PluginRuntimeState { Descriptor = desc };
        }
    }

    /// <summary>启动期对齐（宿主启动后调用一次）：自动拉起启用且未处于防循环的 exe 插件。</summary>
    public void ReconcileStartup(IEnumerable<PluginDescriptor> enabledExePlugins)
    {
        foreach (var desc in enabledExePlugins) StartInternal(desc, out _, auto: true);
    }

    /// <summary>手动启动/重新启用：清崩溃标记并按清单重新拉起。手动路径不设防循环门（用户显式意图）。</summary>
    public bool Start(string id, out string? error)
    {
        PluginDescriptor? desc;
        lock (_lock)
        {
            if (!_states.TryGetValue(id, out var st))
            {
                error = $"插件 {id} 不在引擎管理范围";
                return false;
            }
            desc = st.Descriptor;
        }
        return StartInternal(desc, out error, auto: false);
    }

    /// <summary>手动重启（已崩溃行的「重启」动作），语义与 Start 相同。</summary>
    public bool Restart(string id, out string? error) => Start(id, out error);

    /// <summary>禁用/主动停：清崩溃标记并杀进程；进程退出回调不再标崩溃。幂等。</summary>
    public void Stop(string id)
    {
        IPluginProcessHandle? victim;
        lock (_lock)
        {
            if (!_states.TryGetValue(id, out var st)) return;
            st.Crashed = false;
            victim = st.Handle;
            st.Handle = null;
        }
        try { victim?.Stop(); } catch { }
        PluginStateChanged?.Invoke(id);
    }

    /// <summary>
    /// 标记插件进程意外退出（宿主在 Process.Exited / IPC 断连时调用）：主动停不在此路径，
    /// 意外退出才标已崩溃并累计防循环计数。仅当该插件确在托管启动态时才标记。
    /// </summary>
    public void MarkUnexpectedExit(string id)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(id, out var st)) return;
            if (st.Handle is null)
            {
                st.Crashed = false;
                return; // 已主动停/未启动，忽略陈旧退出信号
            }
            st.Crashed = true;
            st.Handle = null;
            var now = DateTimeOffset.UtcNow;
            // 连续崩溃：距上次崩溃仍在窗口内则累计，否则重新起算
            st.ConsecutiveCrashes = st.LastCrashAt != default && now - st.LastCrashAt <= CrashWindow ? st.ConsecutiveCrashes + 1 : 1;
            st.LastCrashAt = now;
        }
        PluginStateChanged?.Invoke(id);
    }

    /// <summary>从托管表移除（插件目录被删除/发现刷新时）。</summary>
    public void Remove(string id)
    {
        IPluginProcessHandle? victim;
        lock (_lock)
        {
            if (!_states.Remove(id, out var st)) return;
            victim = st.Handle;
        }
        try { victim?.Stop(); } catch { }
        PluginStateChanged?.Invoke(id);
    }

    private bool StartInternal(PluginDescriptor desc, out string? error, bool auto)
    {
        error = null;
        lock (_lock)
        {
            if (!_states.TryGetValue(desc.Id, out var st)) return false;
            // 幂等：已在运行则不重复拉起
            if (!st.Crashed && st.Handle is { IsRunning: true })
                return true;
            // 距上次崩溃已超过窗口：视为稳定运行/人为处置后重启，连续计数复位
            if (st.LastCrashAt != default && DateTimeOffset.UtcNow - st.LastCrashAt > CrashWindow)
            {
                st.ConsecutiveCrashes = 0;
                st.LastCrashAt = default;
            }
            // 防循环门仅挡自动拉起（启动对齐）：达阈值且仍在崩溃窗口内 → 本次运行不再自动拉起
            if (auto && st.ConsecutiveCrashes >= CrashLoopThreshold)
            {
                st.Crashed = true;
                error = $"插件 {desc.Id} 连续崩溃已达 {CrashLoopThreshold} 次，本次运行不再自动拉起；可手动重启或重启宿主";
                return false;
            }
        }

        IPluginProcessHandle? handle;
        try { handle = _launcher(desc); }
        catch { handle = null; }

        lock (_lock)
        {
            if (!_states.TryGetValue(desc.Id, out var st))
            {
                st = new PluginRuntimeState { Descriptor = desc };
                _states[desc.Id] = st;
            }
            if (handle is null)
            {
                // 拉起失败（exe 缺失等）：不视为崩溃，保持未启动态由宿主日志说明
                st.Crashed = false;
                return false;
            }

            // 新进程接管：旧句柄（崩溃残留）先停掉；计数复位留到进程确已稳定存活
            // （见 OnStableRun：达到本次 launch 的稳定时长判定，由退出时序自然完成）
            var oldHandle = st.Handle;
            st.Handle = handle;
            st.Crashed = false;
            handle.Exited += () =>
            {
                bool shouldMark;
                lock (_lock)
                {
                    shouldMark = _states.TryGetValue(desc.Id, out var cur) && ReferenceEquals(cur.Handle, handle);
                }
                if (shouldMark) MarkUnexpectedExit(desc.Id);
            };
            if (oldHandle is not null && !ReferenceEquals(oldHandle, handle))
            {
                try { oldHandle.Stop(); } catch { }
            }
        }
        PluginStateChanged?.Invoke(desc.Id);
        return true;
    }
}
