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
    private const string AppVersion = "v0.2.1";
    private const string DefaultOverlayHotkey = "Ctrl+Alt+Space";

    private Window _window = null!;
    private WorkbenchThemeContext _theme = null!;
    private WorkbenchType _workbenchForTheme = null!;
    private SettingsService _settings = null!;
    private HotkeyService _hotkeys = null!;
    private OverlayWindow _overlayWindow = null!;
    private PluginEnableStore _pluginEnables = null!;
    private IReadOnlyList<PluginDescriptor> _discoveredPlugins = [];
    private string _overlayHotkey = null!;
    private bool _capturingHotkey;
    private Button? _hotkeyChangeButton;
    private Label? _hotkeyDisplay;
    private Label? _hotkeyHint;
    private Icon? _windowIcon;
    private IntPtr _windowLargeIcon;
    private IntPtr _windowSmallIcon;
    private Process? _pluginHostProcess;
    private TrayIcon? _tray;
    private IpcServer _ipcServer = null!;
    private readonly HashSet<string> _crashedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private bool _autoRestarting;

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
        var ipcServer = new IpcServer(hotkeys, settings);
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
        _overlayHotkey = string.IsNullOrWhiteSpace(settings.OverlayHotkey) ? DefaultOverlayHotkey : settings.OverlayHotkey!;

        _pluginEnables = new PluginEnableStore();
        _pluginEnables.Load();
        var discovery = new PluginDiscovery();
        var userPluginsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "Plugins");
        var installPluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        _discoveredPlugins = discovery.Discover(userPluginsDir, installPluginsDir);

        var overlayHotkeyRegistered = _hotkeys.Register(window.Handle, _overlayHotkey, _overlayWindow.ShowOverlay, "浮层呼出键");

        // 托盘与浮层为常驻能力，必须可用
        window.Content = BuildHostPlaceholder();
        window.Closing += e => { e.Cancel = true; window.Hide(); };
        window.PreviewKeyDown += OnHostPreviewKeyDown;

        window.Loaded += () =>
        {
            ApplyWindowIcon(window);
            if (!overlayHotkeyRegistered)
                window.ShowToast($"⚠ 呼出热键 {_overlayHotkey} 注册失败(可能已被其他程序占用)");
            _tray = new TrayIcon(window.Handle, ShowMain, Quit);
            _tray.Add();
            // 按需拉起扩展主机（首启即拉起，保证 Launcher 可见），宿主窗口随后隐藏至托盘，Workbench 防守线在扩展主机窗口中展示
            EnsurePluginHostRunning();
            if (IsPluginHostRunning)
                window.Hide();
        };

        window.NativeMessage += args =>
        {
            if (args is not Win32NativeMessageEventArgs e) return;
            if (e.Msg == HotkeyService.WmHotkey) { _hotkeys.Dispatch((int)e.WParam); args.Handled = true; }
            else if (e.Msg == TrayIcon.WmCallback && _tray is not null) { _tray.HandleCallback((uint)e.WParam, (uint)e.LParam); args.Handled = true; }
        };

        Application.Run(window);
        _windowIcon?.Dispose();
        DestroyWindowIcons();
        try { _pluginHostProcess?.Kill(); } catch { }

        void ShowMain()
        {
            window.Show(null!);
            window.Activate();
            EnsurePluginHostRunning();
        }

        void Quit()
        {
            _tray?.Dispose();
            try { _pluginHostProcess?.Kill(); } catch { }
            Application.Quit();
        }
    }

    private UIElement BuildHostPlaceholder()
    {
        return new StackPanel().Padding(24).Spacing(12).Children(
            new Label().Text("Mew Host (AOT 常驻)").FontSize(16).Bold().WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)),
            new Label().Text($"版本 {AppVersion}  — 托盘与呼出浮层由宿主常驻，Workbench 由扩展主机承载。").FontSize(12).WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)),
            new Button().Content(new Label().Text("打开扩展主机")).CanDrag(false).OnClick(() => EnsurePluginHostRunning()),
            new Button().Content(new Label().Text("重启扩展主机")).CanDrag(false).OnClick(() => RestartPluginHost())
        );
    }

    internal bool IsPluginHostRunning => _pluginHostProcess is { HasExited: false };

    internal void EnsurePluginHostRunning()
    {
        if (IsPluginHostRunning) return;
        var exe = Path.Combine(AppContext.BaseDirectory, "Mew.PluginHost.exe");
        // 开发期 fallback：.build 输出路径（Host Base 为 .../.build/Mew.Host/bin/Debug/net10.0-windows）
        if (!File.Exists(exe))
        {
            var alt = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mew.PluginHost", "bin", "Debug", "net10.0-windows", "Mew.PluginHost.exe");
            exe = Path.GetFullPath(alt);
        }
        if (!File.Exists(exe)) return;
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += OnPluginHostExited;
                _pluginHostProcess = proc;
                _autoRestarting = false;
                Log($"扩展主机已拉起 pid={proc.Id}");
            }
        }
        catch (Exception ex) { Log($"扩展主机拉起失败：{ex.Message}"); }
    }

    private void OnPluginHostExited(object? sender, EventArgs e)
    {
        var proc = sender as Process;
        Log($"扩展主机退出 pid={proc?.Id} code={proc?.ExitCode}");
        if (_autoRestarting) return;
        _autoRestarting = true;
        // 托盘与浮层仍可用，自动拉起（Toast 需 UI 线程，暂仅日志）
        Log("扩展主机将自动重启");
        Task.Delay(1000).ContinueWith(_ => EnsurePluginHostRunning());
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

    private void OnHostPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { _capturingHotkey = false; _hotkeyChangeButton!.Content(new Label().Text("更改")); _hotkeyHint!.Text = ""; return; }
        var parts = new List<string>();
        if (e.ControlKey) parts.Add("Ctrl");
        if (e.AltKey) parts.Add("Alt");
        if (e.ShiftKey) parts.Add("Shift");
        if (e.MetaKey) parts.Add("Win");
        var name = HotkeyKeys.NameOf(e.Key);
        if (name.Length == 0) { _hotkeyHint!.Text = "请按字母/数字/功能键组合"; return; }
        if (parts.Count == 0) { _hotkeyHint!.Text = "需要至少一个修饰键"; return; }
        parts.Add(name);
        var hotkey = string.Join("+", parts);
        if (!HotkeyParser.TryParse(hotkey, out _, out _)) { _hotkeyHint!.Text = "不支持的组合"; return; }
        if (!string.Equals(hotkey, _overlayHotkey, StringComparison.OrdinalIgnoreCase) && _hotkeys.IsRegistered(hotkey))
        { var o = _hotkeys.FindOwner(hotkey); _hotkeyHint!.Text = o is null ? "与已注册热键冲突" : $"与{o}的已注册热键冲突"; return; }
        _hotkeys.Unregister(_overlayHotkey);
        if (!_hotkeys.Register(_window.Handle, hotkey, _overlayWindow.ShowOverlay)) { _hotkeys.Register(_window.Handle, _overlayHotkey, _overlayWindow.ShowOverlay); _hotkeyHint!.Text = "注册失败"; return; }
        _overlayHotkey = hotkey;
        if (_hotkeyDisplay != null) _hotkeyDisplay.Text = hotkey;
        _settings.OverlayHotkey = hotkey;
        _settings.Save();
        _capturingHotkey = false;
        _hotkeyChangeButton!.Content(new Label().Text("更改"));
        _hotkeyHint!.Text = $"已生效:{hotkey}";
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
