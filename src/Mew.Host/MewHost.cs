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
    private const string AppVersion = "v0.2.2";
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
    private readonly HashSet<string> _crashedPlugins = new(StringComparer.OrdinalIgnoreCase);

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
        var ipcServer = new IpcServer(hotkeys, settings, OnOverlayHotkeySet);
        _ipcServer = ipcServer;
        ipcServer.ClientDisconnected += id =>
        {
            _crashedPlugins.Add(id);
            Log($"插件 {id} 已崩溃/断开");
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
        // 宿主侧只消费独立进程插件（exe）；运行期 DLL 由扩展主机按同一来源代管，宿主侧置灰
        _discoveredPlugins = PluginDiscovery.FilterExeLoadable(full, _pluginEnables);
        // 全量名单写入快照：扩展主机按单加载，不再自扫（快照缺失回退本地扫描）
        new PluginSnapshotStore().Save(full);
        Log($"插件快照已写入：{full.Count} 项");

        // 注册前句柄基线：若 show 前后句柄变化，热键必须绑 show 后的句柄才有效
        var preShowHandle = window.Handle;

        // 托盘与浮层为常驻能力，必须可用
        window.Content = BuildHostPlaceholder();
        window.Closing += e => { e.Cancel = true; window.Hide(); };

        window.Loaded += () =>
        {
            ApplyWindowIcon(window);
            Log($"热键句柄比对：show前={preShowHandle:X}，当前={window.Handle:X}");
            var overlayHotkeyRegistered = settings.OverlayHotkeyEnabled
                && _hotkeys.Register(window.Handle, _overlayHotkey, ToggleOverlayFromHotkey, OverlayHotkeyLabel);
            Log(settings.OverlayHotkeyEnabled
                ? (overlayHotkeyRegistered ? $"呼出热键已注册：{_overlayHotkey}" : $"呼出热键注册失败：{_overlayHotkey}（可能被占用或句柄无效）")
                : "呼出热键已禁用（设置→热键可重新启用）");
            _tray = new TrayIcon(window.Handle, Quit, EnsurePluginHostRunning, RestartPluginHost, () => _overlayWindow.ToggleOverlay());
            _tray.Add();
            if (!overlayHotkeyRegistered)
            {
                // 有告警时亮出主窗口，否则首启直接隐藏（提示在隐藏窗口上不可见）
                window.ShowToast($"⚠ 呼出热键 {_overlayHotkey} 注册失败(可能已被其他程序占用)");
                window.Show(null!);
                window.Activate();
            }
            else window.Hide();
        };

        window.NativeMessage += args =>
        {
            if (args is not Win32NativeMessageEventArgs e) return;
            if (e.Msg == HotkeyService.WmHotkey) { Log($"收到热键消息 id={e.WParam}"); _hotkeys.Dispatch((int)e.WParam); args.Handled = true; }
            else if (e.Msg == TrayIcon.WmCallback && _tray is not null) { _tray.HandleCallback((uint)e.WParam, (uint)e.LParam); args.Handled = true; }
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
        return new StackPanel().Padding(24).Spacing(12).Children(
            new Label().Text("Mew Host (AOT 常驻)").FontSize(16).Bold().WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)),
            new Label().Text($"版本 {AppVersion}  — 托盘与呼出浮层由宿主常驻，五区未启动，可手动打开主界面。").FontSize(12).WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)),
            new Button().Content(new Label().Text("打开主界面")).CanDrag(false).OnClick(() => EnsurePluginHostRunning()),
            new Button().Content(new Label().Text("重启主界面")).CanDrag(false).OnClick(() => RestartPluginHost())
        );
    }

    internal bool IsPluginHostRunning => _pluginHostProcess is { HasExited: false };

    internal void EnsurePluginHostRunning()
    {
        if (IsPluginHostRunning) return;
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
                proc.EnableRaisingEvents = true;
                proc.Exited += OnPluginHostExited;
                _pluginHostProcess = proc;
                Log($"主界面已拉起 pid={proc.Id}");
            }
        }
        catch (Exception ex)
        {
            Warn($"主界面拉起失败：{ex.Message}");
        }
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

    internal void RestartPluginHost()
    {
        try { _pluginHostProcess?.Kill(); } catch { }
        _pluginHostProcess = null;
        EnsurePluginHostRunning();
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
    private static readonly IntPtr IconSmall = IntPtr.Zero;
    private static readonly IntPtr IconBig = new(1);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string f, int idx, out IntPtr large, out IntPtr small, uint n);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
