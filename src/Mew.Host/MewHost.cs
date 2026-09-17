using System.Diagnostics;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Mew.Workbench;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host;

/// <summary>
/// 宿主(Mew.Host.exe, AOT 常驻)：仅承载 Overlay/托盘/全局热键/插件发现与 IPC 路由，不承载 Workbench 五区。
/// 五区由 Mew.PluginHost(JIT) 承载，宿主按需拉起并探活。宿主无可见窗口：message-only 消息窗口只提供
/// 托盘回调与热键所需的 HWND；告警走托盘气球 + 日志（T3 启用态仍由宿主 adapter 经 IPC/快照托管，ADR-000303）。
/// </summary>
internal sealed class MewHost
{
    private const string AppVersion = "v0.3.3";
    private const string OverlayHotkeyLabel = "浮层呼出键";

    private HostMessageWindow? _msgWindow;
    private SettingsService _settings = null!;
    private HotkeyService _hotkeys = null!;
    private OverlayWindow _overlayWindow = null!;
    private PluginEnableStore _pluginEnables = null!;
    private string _overlayHotkey = null!;
    private Process? _pluginHostProcess;
    private TrayIcon? _tray;
    private IpcServer _ipcServer = null!;
    private PluginLifecycleEngine _lifecycleEngine = null!;
    private HostPluginAdminService _pluginAdmin = null!;

    internal void Run()
    {
        var theme = new WorkbenchType().ThemeContext;
        var settings = new SettingsService();
        _settings = settings;
        var hotkeys = new HotkeyService();
        _hotkeys = hotkeys;
        _lifecycleEngine = new PluginLifecycleEngine(LaunchPluginProcess);
        var ipcServer = new IpcServer(hotkeys, settings, OnOverlayHotkeySet, OnPluginEnableSet, FetchPluginRows, OnPluginAdminAction);
        _ipcServer = ipcServer;
        ipcServer.ClientDisconnected += id =>
        {
            // IPC 断连：T3 行归因到引擎（仅引擎托管的启用 exe 插件才标已崩溃），其余仅日志
            _lifecycleEngine.MarkUnexpectedExit(id);
            Log($"插件 {id} IPC 断开");
            // Toast 需在 UI 线程，暂仅日志，避免跨线程集合修改崩溃
        };
        ipcServer.Start();
        // 宿主无可见窗口：浮层 owner 传 null（无 owner 顶层浮层，定位/置顶逻辑不变）
        var overlay = new OverlayWindow(null, theme);
        _overlayWindow = overlay;

        settings.Load();
        _overlayHotkey = string.IsNullOrWhiteSpace(settings.OverlayHotkey) ? HotkeyService.DefaultOverlayHotkey : settings.OverlayHotkey!;

        _pluginEnables = new PluginEnableStore();
        _pluginEnables.Load();
        var discovery = new PluginDiscovery();
        // 单一主插件目录（ADR-000301）：缺省用户目录,settings.pluginDir 可改
        var primaryPluginDir = string.IsNullOrWhiteSpace(settings.PluginDir)
            ? PluginDiscovery.DefaultUserPluginsDir
            : settings.PluginDir!;
        foreach (var removed in settings.MigratedOutPluginDirs)
            Log($"插件目录迁移：原附加目录 {removed} 不再单独扫描，如需继续使用请以链接挂入主目录（mklink /D 需开发者模式，/J 免特权限本机卷）");
        Log($"插件目录：{primaryPluginDir}");
        var full = discovery.Discover(primaryPluginDir);
        // 宿主管理名单：非保留 id（IPC 启用/禁用的 unknown-id 门收进 adapter，ADR-000204）
        _pluginAdmin = new HostPluginAdminService(_lifecycleEngine, _pluginEnables,
            full.Where(d => !PluginDiscovery.IsReservedHostId(d.Id)).Select(d => d.Id), Log);
        // 全量名单写入快照：扩展主机按单加载，不再自扫（快照缺失回退本地扫描）
        new PluginSnapshotStore().Save(full);
        Log($"插件快照已写入：{full.Count} 项");
        // T3（exe 独立进程）生命周期引擎托管：注册全部 exe 项并按启用态自动拉起
        foreach (var desc in full.Where(d => !PluginDiscovery.IsReservedHostId(d.Id)
            && string.Equals(d.Manifest.Entry.Type, "exe", StringComparison.OrdinalIgnoreCase)))
            _lifecycleEngine.Register(desc);
        var enabledExe = full.Where(d => d.IsValid && !PluginDiscovery.IsReservedHostId(d.Id)
            && string.Equals(d.Manifest.Entry.Type, "exe", StringComparison.OrdinalIgnoreCase)
            && _pluginEnables.IsEnabled(d.Id)).ToList();
        _lifecycleEngine.ReconcileStartup(enabledExe);
        var t3Plugins = full.Where(d => !PluginDiscovery.IsReservedHostId(d.Id)
            && string.Equals(d.Manifest.Entry.Type, "exe", StringComparison.OrdinalIgnoreCase)).ToList();
        Log($"宿主托管 T3 插件：{t3Plugins.Count} 项（启用 {enabledExe.Count} 项已拉起）");

        // 宿主无可见窗口：message-only 窗口只提供托盘/热键所需的 HWND，同步创建、无 Loaded 时序。
        _msgWindow = new HostMessageWindow(OnMessageWindowMessage);
        var hwnd = _msgWindow.Handle;
        Log($"宿主消息窗口句柄={hwnd:X}");
        var overlayHotkeyRegistered = settings.OverlayHotkeyEnabled
            && _hotkeys.Register(hwnd, _overlayHotkey, ToggleOverlayFromHotkey, OverlayHotkeyLabel);
        Log(settings.OverlayHotkeyEnabled
            ? (overlayHotkeyRegistered ? $"呼出热键已注册：{_overlayHotkey}" : $"呼出热键注册失败：{_overlayHotkey}（可能被占用或句柄无效）")
            : "呼出热键已禁用（设置→热键可重新启用）");
        _tray = new TrayIcon(hwnd, Quit, EnsurePluginHostRunning, RestartPluginHost, () => _overlayWindow.ToggleOverlay(),
            tip: $"Mew Launcher — {AppVersion}");
        _tray.Add();
        // 宿主启动即拉起主界面（ADR-000202 的常驻干净让位给开箱即用；崩溃仍不自愈，需手动重启）
        EnsurePluginHostRunning();
        if (!overlayHotkeyRegistered)
            Warn($"⚠ 呼出热键 {_overlayHotkey} 注册失败(可能已被其他程序占用)");

        // 无主窗口进泵：只为浮层/托盘/热键服务；退出一律经托盘菜单 Quit → Shutdown。
        Application.Run(() =>
        {
            if (Application.Current is { } app) app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        });
        // 独立生死：宿主退出不再终止扩展主机进程

        void Quit()
        {
            _tray?.Dispose();
            _msgWindow?.Dispose();
            Application.Shutdown();
        }
    }

    /// <summary>消息窗口路由（原窗口 NativeMessage）：热键分发 + 托盘回调，跑在消息泵线程。</summary>
    private void OnMessageWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == HotkeyService.WmHotkey) { Log($"收到热键消息 id={wParam}"); _hotkeys.Dispatch((int)wParam); }
        else if (msg == TrayIcon.WmCallback && _tray is not null) { _tray.HandleCallback((uint)wParam, (uint)lParam); }
    }

    /// <summary>独立插件（T3）行拉取（扩展主机经 IPC 请求，ADR-000303）：宿主 adapter 快照为唯一权威行来源。</summary>
    private PluginRowsAckMessage FetchPluginRows() =>
        new(_pluginAdmin.Snapshot().Select(PluginAdminRowDto.FromRow).ToList(), null);

    /// <summary>独立插件行内动作落点（扩展主机经 IPC 请求，ADR-000303）：与宿主进程内动作共用同一条 adapter 加锁路径。</summary>
    private PluginAdminActionAckMessage OnPluginAdminAction(string id, string action)
    {
        if (!PluginAdminRowDto.TryParseAction(action, out var rowAction))
            return new PluginAdminActionAckMessage(false, $"未知动作：{action}", id, action);
        var result = _pluginAdmin.Apply(id, rowAction);
        return new PluginAdminActionAckMessage(result.Outcome == PluginAdminOutcome.Ok, result.Error, id, action);
    }

    internal bool IsPluginHostRunning => _pluginHostProcess is { HasExited: false };

    /// <summary>打开主界面：活着就唤出窗口（关=隐藏，进程常驻），死了才拉起；宿主重启后遗留的前朝进程认领回来，不开第二个。</summary>
    internal void EnsurePluginHostRunning()
    {
        if (_pluginHostProcess is { } tracked)
        {
            if (!tracked.HasExited)
            {
                if (TryShowProcessWindow(tracked.Id))
                {
                    Log($"主界面已唤出 pid={tracked.Id}");
                }
                else
                {
                    Log($"主界面进程活着但无窗口（启动中？）pid={tracked.Id}，不重复拉起");
                }

                return;
            }

            _pluginHostProcess = null;
        }

        var orphan = Process.GetProcessesByName("Mew.PluginHost").FirstOrDefault();
        if (orphan is not null && !orphan.HasExited)
        {
            TrackPluginHostProcess(orphan);
            if (TryShowProcessWindow(orphan.Id))
            {
                Log($"主界面前朝进程已认领并唤出 pid={orphan.Id}");
            }
            else
            {
                Log($"主界面前朝进程已认领 pid={orphan.Id}，暂无窗口");
            }

            return;
        }

        StartPluginHostProcess();
    }

    private void TrackPluginHostProcess(Process proc)
    {
        proc.EnableRaisingEvents = true;
        proc.Exited += OnPluginHostExited;
        _pluginHostProcess = proc;
    }

    private void StartPluginHostProcess()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Mew.PluginHost.exe");
        // 开发期 fallback：集中输出根上溯 4 级即得（Windows 为 .build，非 Windows 为 .build-linux）
        if (!File.Exists(exe))
        {
            var alt = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mew.PluginHost", "bin", "Debug", "net10.0-windows", "Mew.PluginHost.exe");
            exe = Path.GetFullPath(alt);
        }
        if (!File.Exists(exe))
        {
            Warn($"主界面缺失：{exe}（请先构建整个方案）");
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                TrackPluginHostProcess(proc);
                Log($"主界面已拉起 pid={proc.Id}");
            }
        }
        catch (Exception ex)
        {
            Warn($"主界面拉起失败：{ex.Message}");
        }
    }

    internal void RestartPluginHost()
    {
        // 重启=杀干净再起：跟踪中的先杀；宿主重启后遗留的前朝进程同样杀掉，保证起来的是新的
        var victim = _pluginHostProcess is { HasExited: false } proc ? proc : Process.GetProcessesByName("Mew.PluginHost").FirstOrDefault();
        try { victim?.Kill(); } catch { }
        _pluginHostProcess = null;
        StartPluginHostProcess();
    }

    /// <summary>告警：宿主无可见窗口，提示走托盘气球（托盘未建好时仅日志）。调用线程不限，异常吞掉。</summary>
    private void Warn(string message)
    {
        Log(message);
        try
        {
            _tray?.ShowBalloon("Mew Launcher", message);
        }
        catch { }
    }

    private void OnPluginHostExited(object? sender, EventArgs e)
    {
        var proc = sender as Process;
        Log($"主界面退出 pid={proc?.Id} code={proc?.ExitCode}");
        // 仅标记不自愈：托盘与浮层仍可用，需用户手动重新打开；IPC 断开路径另行标已崩溃
        _pluginHostProcess = null;
    }

    private void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "host.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>唤出指定进程的主窗口（最小化则恢复，隐藏则显示并前台）；找不到窗口返回 false（启动中时）。</summary>
    private static bool TryShowProcessWindow(int pid)
    {
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var id);
            if (id != pid || GetWindow(hwnd, GwOwner) != 0)
            {
                return true; // 非目标进程或被拥有的窗口（对话框等）跳过
            }

            found = hwnd;
            return false;
        }, 0);
        if (found == 0)
        {
            return false;
        }

        if (IsIconic(found))
        {
            ShowWindow(found, SwRestore);
        }
        else
        {
            ShowWindow(found, SwShow);
        }

        AllowSetForegroundWindow(AsfwAny);
        SetForegroundWindow(found);
        return true;
    }

    private void ToggleOverlayFromHotkey()
    {
        Log("呼出热键触发");
        _overlayWindow.ToggleOverlay();
    }

    private OverlayHotkeySetAckMessage OnOverlayHotkeySet(string? hotkey, bool enabled)
    {
        var hwnd = _msgWindow?.Handle ?? IntPtr.Zero;
        var (ok, error, effectiveHotkey, effectiveEnabled) = OverlayHotkeyAdmin.Apply(
            _hotkeys, _overlayHotkey, hwnd, ToggleOverlayFromHotkey, OverlayHotkeyLabel, hotkey, enabled);
        if (!ok)
            return new OverlayHotkeySetAckMessage(false, error, _overlayHotkey, true);
        _overlayHotkey = effectiveHotkey;
        _settings.OverlayHotkey = effectiveHotkey;
        _settings.OverlayHotkeyEnabled = effectiveEnabled;
        _settings.Save();
        Log(effectiveEnabled ? $"呼出热键已改为：{effectiveHotkey}" : "呼出热键已禁用");
        return new OverlayHotkeySetAckMessage(true, null, effectiveHotkey, effectiveEnabled);
    }

    /// <summary>插件启用/禁用落盘入口（扩展主机经 IPC 请求，ADR-000203 单写者）：宿主是唯一写者。
    /// 与宿主 UI 行动作共用 adapter 同一条加锁路径（ADR-000204），unknown-id/保留 id 门在 adapter 内。
    /// T3（exe）由引擎托管立即生效（启用即拉起、禁用即杀）；T2 只落意图，由扩展主机下次装载。</summary>
    private PluginEnableSetAckMessage OnPluginEnableSet(string id, bool enabled)
    {
        var result = _pluginAdmin.Apply(id, enabled ? PluginRowAction.Enable : PluginRowAction.Disable);
        return result.Outcome == PluginAdminOutcome.Ok
            ? new PluginEnableSetAckMessage(true, null, id, enabled)
            : new PluginEnableSetAckMessage(false, result.Error, id, enabled);
    }

    /// <summary>T3 插件进程拉起器（引擎 launcher）：解析清单入口为绝对路径并启动，包成进程句柄交给引擎托管。
    /// 拉不起（入口缺失/启动异常）返回 null，由引擎保持未启动态、宿主日志说明。</summary>
    private IPluginProcessHandle? LaunchPluginProcess(PluginDescriptor desc)
    {
        var dir = Path.GetDirectoryName(desc.ManifestPath);
        if (string.IsNullOrEmpty(dir))
        {
            Warn($"插件 {desc.Id} 清单目录无法解析：{desc.ManifestPath}");
            return null;
        }
        var exe = Path.Combine(dir, desc.Manifest.Entry.Path);
        if (!File.Exists(exe))
        {
            Warn($"插件 {desc.Id} 入口缺失：{exe}");
            return null;
        }
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir };
            if (!string.IsNullOrWhiteSpace(desc.Manifest.Entry.Args))
                psi.Arguments = desc.Manifest.Entry.Args;
            var proc = Process.Start(psi);
            if (proc is null)
            {
                Warn($"插件 {desc.Id} 拉起无进程句柄");
                return null;
            }
            Log($"插件 {desc.Id} 已拉起 pid={proc.Id}");
            return new PluginProcessHandle(proc);
        }
        catch (Exception ex)
        {
            Warn($"插件 {desc.Id} 拉起失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>真实进程句柄适配：把 System.Diagnostics.Process 的 Exited/Kill/HasExited 桥接为引擎契约。</summary>
    private sealed class PluginProcessHandle : IPluginProcessHandle
    {
        private readonly Process _process;

        public PluginProcessHandle(Process process)
        {
            _process = process;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Exited?.Invoke();
        }

        public event Action? Exited;

        public bool IsRunning
        {
            get
            {
                try { return !_process.HasExited; }
                catch { return false; }
            }
        }

        public void Stop()
        {
            try { _process.Kill(); } catch { }
        }
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(NativeEnumWindowsProc callback, int lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint hwnd, int cmdShow);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    private const uint GwOwner = 4;
    private const int SwRestore = 9;
    private const int SwShow = 5;
    private const uint AsfwAny = 0xFFFFFFFF;
    private delegate bool NativeEnumWindowsProc(nint hwnd, int lParam);
}
