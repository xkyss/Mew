using System.Diagnostics;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Mew.Workbench;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;
using WorkbenchType = Mew.Workbench.Workbench;
using Icon = System.Drawing.Icon;

namespace Mew.Host;

/// <summary>
/// 宿主(Mew.Host.exe, AOT 常驻)：仅承载 Overlay/托盘/全局热键/设置根节/插件发现与 IPC 路由，不承载 Workbench 五区。
/// 五区由 Mew.PluginHost(JIT) 承载，宿主按需拉起并探活。
/// </summary>
internal sealed class MewHost
{
    private const string AppVersion = "v0.2.5";
    private const string OverlayHotkeyLabel = "浮层呼出键";

    private Window _window = null!;
    private WorkbenchThemeContext _theme = null!;
    private WorkbenchType _workbenchForTheme = null!;
    private SettingsService _settings = null!;
    private HotkeyService _hotkeys = null!;
    private OverlayWindow _overlayWindow = null!;
    private PluginEnableStore _pluginEnables = null!;
    private IReadOnlyList<PluginDescriptor> _discoveredPlugins = [];
    private string _overlayHotkey = null!;
    private Icon? _windowIcon;
    private IntPtr _windowLargeIcon;
    private IntPtr _windowSmallIcon;
    private Process? _pluginHostProcess;
    private TrayIcon? _tray;
    private IpcServer _ipcServer = null!;
    private PluginLifecycleEngine _lifecycleEngine = null!;
    private HostPluginAdminService _pluginAdmin = null!;
    private PluginAdminPanel? _hostPluginPanel;

    internal void Run()
    {
        var window = new NativeChromeWindow()
            .Title($"Mew Launcher — {AppVersion}")
            .Resizable(400, 300);

        _window = window;
        _workbenchForTheme = new WorkbenchType();
        var theme = _workbenchForTheme.ThemeContext;
        _theme = theme;
        var settings = new SettingsService();
        _settings = settings;
        var hotkeys = new HotkeyService();
        _hotkeys = hotkeys;
        _lifecycleEngine = new PluginLifecycleEngine(LaunchPluginProcess);
        // 引擎状态变化可能来自后台线程（进程退出/IPC 断连），经自窗口消息转到 UI 线程刷新插件行
        _lifecycleEngine.PluginStateChanged += _ => PostMessage(window.Handle, WmRefreshPluginRows, 0, 0);
        var ipcServer = new IpcServer(hotkeys, settings, OnOverlayHotkeySet, OnPluginEnableSet);
        _ipcServer = ipcServer;
        ipcServer.ClientDisconnected += id =>
        {
            // IPC 断连：T3 行归因到引擎（仅引擎托管的启用 exe 插件才标已崩溃），其余仅日志
            _lifecycleEngine.MarkUnexpectedExit(id);
            Log($"插件 {id} IPC 断开");
            // Toast 需在 UI 线程，暂仅日志，避免跨线程集合修改崩溃
        };
        ipcServer.Start();
        var overlay = new OverlayWindow(window, theme);
        _overlayWindow = overlay;

        settings.Load();
        _overlayHotkey = string.IsNullOrWhiteSpace(settings.OverlayHotkey) ? HotkeyService.DefaultOverlayHotkey : settings.OverlayHotkey!;

        _pluginEnables = new PluginEnableStore();
        _pluginEnables.Load();
        var discovery = new PluginDiscovery();
        var userPluginsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "Plugins");
        var installPluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        var roots = PluginDiscovery.ResolvePluginRoots(settings.PluginDirs, userPluginsDir, installPluginsDir);
        Log($"插件目录：{string.Join("；", roots)}");
        var full = discovery.Discover(roots);
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
        // 宿主侧只消费独立进程插件（exe）用于窗口展示；运行期 DLL 由扩展主机代管
        _discoveredPlugins = full.Where(d => !PluginDiscovery.IsReservedHostId(d.Id)
            && string.Equals(d.Manifest.Entry.Type, "exe", StringComparison.OrdinalIgnoreCase)).ToList();
        Log($"宿主托管 T3 插件：{_discoveredPlugins.Count} 项（启用 {enabledExe.Count} 项已拉起）");

        // 首启直接隐藏到托盘：以 0 透明度进入 Run，窗口创建并 show 但全程不可见（消除"一闪而过"）。
        // 关键：Loaded 里所有初始化（图标/热键/托盘）都必须在仍为 0 透明度时做完，先 Hide 再恢复不透明——
        // 若先恢复不透明再做初始化，窗口会以可见态跨过若干合成帧才被 Hide，仍是可见闪烁。句柄基线仍取 show 前的句柄。
        window.Opacity = 0;
        var preShowHandle = window.Handle;

        // 托盘与浮层为常驻能力，必须可用
        window.Content = BuildHostPlaceholder();
        window.Closing += e => { e.Cancel = true; window.Hide(); };

        window.Loaded += () =>
        {
            // 0 透明度期间完成全部初始化：窗口不可见，DWM 不会合成任何一帧
            ApplyWindowIcon(window);
            Log($"热键句柄比对：show前={preShowHandle:X}，当前={window.Handle:X}");
            var overlayHotkeyRegistered = settings.OverlayHotkeyEnabled
                && _hotkeys.Register(window.Handle, _overlayHotkey, ToggleOverlayFromHotkey, OverlayHotkeyLabel);
            Log(settings.OverlayHotkeyEnabled
                ? (overlayHotkeyRegistered ? $"呼出热键已注册：{_overlayHotkey}" : $"呼出热键注册失败：{_overlayHotkey}（可能被占用或句柄无效）")
                : "呼出热键已禁用（设置→热键可重新启用）");
            _tray = new TrayIcon(window.Handle, Quit, EnsurePluginHostRunning, RestartPluginHost, ShowHostPluginManager, () => _overlayWindow.ToggleOverlay());
            _tray.Add();
            // 宿主启动即拉起主界面（ADR-000202 的常驻干净让位给开箱即用；崩溃仍不自愈，需手动重启）
            EnsurePluginHostRunning();
            if (!overlayHotkeyRegistered)
            {
                // 告警路径：先恢复不透明再亮出（提示在隐藏窗口上不可见）
                window.Opacity = 1;
                window.ShowToast($"⚠ 呼出热键 {_overlayHotkey} 注册失败(可能已被其他程序占用)");
                window.Show(null!);
                window.Activate();
            }
            else
            {
                // 正常路径：先 Hide（此时仍 0 透明度，全程无可见帧），再恢复不透明供后续亮出（托盘/告警）
                window.Hide();
                window.Opacity = 1;
            }
        };

        window.NativeMessage += args =>
        {
            if (args is not Win32NativeMessageEventArgs e) return;
            if (e.Msg == HotkeyService.WmHotkey) { Log($"收到热键消息 id={e.WParam}"); _hotkeys.Dispatch((int)e.WParam); args.Handled = true; }
            else if (e.Msg == TrayIcon.WmCallback && _tray is not null) { _tray.HandleCallback((uint)e.WParam, (uint)e.LParam); args.Handled = true; }
            else if (e.Msg == WmRefreshPluginRows) { RefreshHostPluginRows(); args.Handled = true; }
        };

        Application.Run(window);
        _windowIcon?.Dispose();
        DestroyWindowIcons();
        // 独立生死：宿主退出不再终止扩展主机进程

        void Quit()
        {
            _tray?.Dispose();
            Application.Quit();
        }
    }

    private UIElement BuildHostPlaceholder()
    {
        // 行区 = 共享插件管理面板（ADR-000204）：渲染/路由只此一份；页脚说明随行存在而渲染
        var pluginPanel = new PluginAdminPanel(_pluginAdmin, _theme,
            "无独立进程插件（entry.type=exe）；仅 DLL 插件时由扩展主机（主界面）管理",
            footerText: "已崩溃不自动重拉，点行内「重启」或重启宿主恢复；崩溃标记不落盘");
        _hostPluginPanel = pluginPanel;
        var content = new StackPanel().Padding(24).Spacing(12).Children(
            new Label().Text("Mew Host (AOT 常驻)").FontSize(16).Bold().WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)),
            new Label().Text($"版本 {AppVersion}  — 托盘与呼出浮层由宿主常驻，五区未启动，可手动打开主界面。").FontSize(12).WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)),
            new Button().Content(new Label().Text("打开主界面")).CanDrag(false).OnClick(() => EnsurePluginHostRunning()),
            new Button().Content(new Label().Text("重启主界面")).CanDrag(false).OnClick(() => RestartPluginHost()),
            new Label().Text("独立插件（T3，进程隔离，由宿主托管）").FontSize(14).Bold().WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)),
            pluginPanel
        );
        RefreshHostPluginRows();
        return content;
    }

    /// <summary>托盘「插件管理」入口：亮出宿主窗口（含独立插件行的管理区），不依赖扩展主机活着。</summary>
    private void ShowHostPluginManager()
    {
        RefreshHostPluginRows();
        try { _window.Show(null!); _window.Activate(); } catch { }
    }

    /// <summary>重建宿主侧独立插件（T3）行：状态词/动作由共享面板经 adapter 快照渲染（ADR-000204）。仅 UI 线程调用。</summary>
    private void RefreshHostPluginRows()
    {
        _hostPluginPanel?.Refresh();
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

    /// <summary>告警并亮出主窗口（常驻隐藏态下保证提示可见）。仅 UI 线程调用。</summary>
    private void Warn(string message)
    {
        Log(message);
        try { _window.ShowToast(message); _window.Show(null!); _window.Activate(); }
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
        var (ok, error, effectiveHotkey, effectiveEnabled) = OverlayHotkeyAdmin.Apply(
            _hotkeys, _overlayHotkey, _window.Handle, ToggleOverlayFromHotkey, OverlayHotkeyLabel, hotkey, enabled);
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

    private void ApplyWindowIcon(Window window)
    {
        var path = Environment.ProcessPath!;
        if (ExtractIconEx(path, 0, out var large, out var small, 1) > 0) { _windowLargeIcon = large; _windowSmallIcon = small; SendMessage(window.Handle, WmSetIcon, IconSmall, small); SendMessage(window.Handle, WmSetIcon, IconBig, large); return; }
        _windowIcon = Icon.ExtractAssociatedIcon(path);
        if (_windowIcon is null) return;
        SendMessage(window.Handle, WmSetIcon, IconSmall, _windowIcon.Handle);
        SendMessage(window.Handle, WmSetIcon, IconBig, _windowIcon.Handle);
    }

    private void DestroyWindowIcons()
    {
        if (_windowLargeIcon != IntPtr.Zero) { DestroyIcon(_windowLargeIcon); _windowLargeIcon = IntPtr.Zero; }
        if (_windowSmallIcon != IntPtr.Zero) { DestroyIcon(_windowSmallIcon); _windowSmallIcon = IntPtr.Zero; }
    }

    private const uint WmSetIcon = 0x0080;
    /// <summary>自窗口刷新插件行消息（WM_APP+2）：引擎状态变化可能来自后台线程，经 PostMessage 转到 UI 线程处理。</summary>
    private const uint WmRefreshPluginRows = 0x8002;
    private static readonly IntPtr IconSmall = IntPtr.Zero;
    private static readonly IntPtr IconBig = new(1);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
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
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string f, int idx, out IntPtr large, out IntPtr small, uint n);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
