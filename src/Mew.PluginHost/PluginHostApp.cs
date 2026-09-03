using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Mew.Launcher;
using Mew.Workbench;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;
using Icon = System.Drawing.Icon;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.PluginHost;

/// <summary>
/// 扩展主机（JIT）：承载 Workbench 五区与工具模块，崩溃不影响宿主常驻能力。
/// 五区本身视为第一个内部插件，与外部插件同等注册路径。
/// </summary>
internal sealed class PluginHostApp
{
    private const string SettingsDocumentId = "settings-document";
    private const string SettingsAppearance = "appearance";

    private WorkbenchType _workbench = null!;
    private WorkbenchThemeContext _theme = null!;
    private SettingsService _settings = null!;
    private HotkeyService _hotkeys = null!;
    private ToolModuleContext _context = null!;
    private Window _window = null!;
    private Button? _titleThemeButton;
    private Icon? _windowIcon;
    private IntPtr _windowLargeIcon;
    private IntPtr _windowSmallIcon;
    private List<RadioButton>? _themeRadios;
    private readonly (ThemeVariant Mode, string Label, string Icon, string ToolTip)[] _themeModes =
    [
        (ThemeVariant.System, "跟随系统", "🌓", "跟随系统 · 点击切换"),
        (ThemeVariant.Light, "亮色", "☀", "浅色 · 点击切换"),
        (ThemeVariant.Dark, "暗色", "☾", "暗色 · 点击切换"),
    ];
    private string _settingsNav = SettingsAppearance;
    private StackPanel? _settingsContent;
    private PluginEnableStore _pluginEnables = null!;
    private IReadOnlyList<PluginDescriptor> _discoveredPlugins = [];
    private StackPanel? _pluginPanel;
    private IpcClient? _ipcClient;
    private PluginDllLoader? _dllLoader;
    private readonly List<IpcClient> _dllIpcClients = [];
    private IReadOnlyList<PluginLoadResult> _dllLoadResults = [];

    internal void Run()
    {
        var window = new NativeChromeWindow()
            .Title("Mew Launcher")
            .Resizable(1080, 720);

        var workbench = new WorkbenchType();
        var theme = workbench.ThemeContext;
        var settings = new SettingsService();
        var settingsSections = new SettingsSectionRegistry();
        var hotkeys = new HotkeyService();
        // 扩展主机内的浮层占位：实际聚合在宿主，扩展主机仅为兼容预留空 Overlay（或后续经 IPC 代理）
        var overlay = new OverlayWindow(window, theme);
        var context = new ToolModuleContext(workbench, window.Handle, window, hotkeys, settings, overlay, theme, settingsSections);

        _workbench = workbench;
        _theme = theme;
        _settings = settings;
        _hotkeys = hotkeys;
        _window = window;
        _context = context;

        settings.Load();

        // 插件发现：与宿主同目录扫描，展示在设置→插件列表
        _pluginEnables = new PluginEnableStore();
        _pluginEnables.Load();
        var discovery = new PluginDiscovery();
        var userPluginsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "Plugins");
        var installPluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        _discoveredPlugins = discovery.Discover(userPluginsDir, installPluginsDir);

        workbench.Theme(tc => tc.SetMode(LoadThemeMode()).SetAccent(Accent.Blue));

        // 宿主设置节：插件列表（发现结果）先于模块节注册，保证顺序 外观/插件/模块节
        settingsSections.Add("plugins", "插件", BuildPluginPanel);

        // 组合根：编译期模块（T1）
        AddModule(new LauncherModule());

        // T2 DLL 运行时加载（按目录 ALC 隔离）
        _dllLoader = new PluginDllLoader();
        _dllLoadResults = _dllLoader.Load(_discoveredPlugins, _pluginEnables, () =>
        {
            // 为 DLL 插件创建捕获式 overlay，记录其注册的搜索源以便经 IPC 代理至宿主
            var capturingOverlay = new CapturingOverlay(overlay);
            return new ToolModuleContext(workbench, window.Handle, window, hotkeys, settings, capturingOverlay, theme, settingsSections);
        });
        // 将捕获的源通过管道注册到宿主（内存直连模式下 _ipcServer 为空则走管道）
        foreach (var src in CapturingOverlay.Captured.ToList())
        {
            var cap = _discoveredPlugins.FirstOrDefault(d => d.Id == src.Id)?.Manifest.Capabilities;
            var dto = cap?.Search != null ? new PluginCapabilitiesDto(new SearchCapabilityDto(cap.Search.ProviderId, cap.Search.DisplayName), null, null) : new PluginCapabilitiesDto(null, null, null);
            var client = new IpcClient(src, src.Id, src.DisplayName, 1, dto);
            if (client.Connect(out _)) _dllIpcClients.Add(client);
        }
        CapturingOverlay.Captured.Clear();

        // IPC：向宿主注册编译期 Launcher 搜索源（管道模式，宿主为 server）
        // 取 Launcher 的搜索源：通过 overlay 间接获取，简化为新建一个可查询的源占位
        var launcherSource = new IpcSearchSourceAdapter("launcher", "启动项", query =>
        {
            // 委托给本地 LauncherSearch 逻辑：此处复用 LauncherSearchSource 的查询（需 items）
            // 为简化，返回空由真实 LauncherSearchSource 在扩展主机内的 overlay 中已注册，
            // 此处创建的适配器仅为演示 IPC 链路，实际应转发至同一数据源
            return [];
        });
        _ipcClient = new IpcClient(launcherSource, "launcher", "启动项", 1, new PluginCapabilitiesDto(new SearchCapabilityDto("launcher", "启动项"), null, null));
        // 尝试管道连接，失败不影响本地 Workbench 启动（单进程回退）
        _ipcClient.Connect(out _);

        // 内部插件：五区本身视为首个内部插件的占位描述（与外部插件同等可见）
        // 实际五区贡献已由各模块完成，此处仅为清单语义保留

        // 宿主贡献：设置上下文（活动栏/侧边栏/编辑器文档），设置节 = 外观 + 模块节
        workbench
            .ActivityBar(bar => bar.Item("settings", "设置", ShellIcons.ActivityGlyph(_theme, ShellIcons.SettingsIconData)))
            .SideBar(side => side.View("settings", "设置", BuildSettingsSideBar()))
            .EditorArea(editor => editor.Document(SettingsDocumentId, "设置", BuildSettingsDocument()));

        _titleThemeButton = TitleBarBuilder.BuildTitleBar(window, Quit, OpenSettings, CycleTheme, workbench, ShowAbout);
        UpdateThemeButton();

        window.PreviewKeyDown += e => workbench.NotifyWindowKeyDown(e);
        window.Content = workbench.Build();

        window.Closing += e =>
        {
            e.Cancel = true;
            window.Hide();
            // 扩展主机隐藏而非退出，宿主常驻可重新拉起
        };

        window.Loaded += () =>
        {
            workbench.RefreshPresentation();
            ApplyWindowIcon(window);
            if (Application.Current is { } app)
            {
                app.ThemeModeChanged += PersistThemeMode;
                app.ThemeModeChanged += SyncThemeRadios;
                app.ThemeModeChanged += UpdateThemeButton;
            }
        };

        window.NativeMessage += args =>
        {
            if (args is Win32NativeMessageEventArgs e && e.Msg == HotkeyService.WmHotkey)
            {
                _hotkeys.Dispatch((int)e.WParam);
                args.Handled = true;
            }
        };

        Application.Run(window);
        _windowIcon?.Dispose();
        DestroyWindowIcons();

        void Quit() => Application.Quit();
    }

    private void AddModule(IMewToolModule module) => module.Configure(_context);

    private void CycleTheme()
    {
        var idx = Array.FindIndex(_themeModes, e => e.Mode == _theme.Mode);
        _theme.SetMode(_themeModes[(idx + 1) % _themeModes.Length].Mode);
    }

    private void UpdateThemeButton()
    {
        if (_titleThemeButton is not { } b) return;
        var entry = Array.Find(_themeModes, c => c.Mode == _theme.Mode);
        b.Content(new Label().Text(entry.Icon).FontSize(14));
        b.ToolTip(entry.ToolTip);
    }

    private static void ShowAbout(NativeChromeWindow window)
    {
        MessageBox.Notify("Mew Launcher\n基于 MewUI 与 Workbench 的应用启动管理器。", PromptIconKind.Info, "关于 Mew Launcher", window);
    }

    private ThemeVariant LoadThemeMode()
    {
        var stored = _settings.ThemeMode;
        return Enum.TryParse<ThemeVariant>(stored, out var m) ? m : ThemeVariant.System;
    }

    private void PersistThemeMode()
    {
        _settings.ThemeMode = _theme.Mode.ToString();
        _settings.Save();
    }

    private void SyncThemeRadios()
    {
        if (_themeRadios is not { } radios) return;
        foreach (var (r, m) in radios.Zip(_themeModes)) r.IsChecked = _theme.Mode == m.Mode;
    }

    private void OpenSettings()
    {
        _workbench.SelectActivity("settings");
        _workbench.OpenDocument(SettingsDocumentId);
    }

    private void ApplyWindowIcon(Window window)
    {
        var path = Environment.ProcessPath!;
        if (ExtractIconEx(path, 0, out var large, out var small, 1) > 0)
        {
            _windowLargeIcon = large;
            _windowSmallIcon = small;
            SendMessage(window.Handle, WmSetIcon, IconSmall, small);
            SendMessage(window.Handle, WmSetIcon, IconBig, large);
            return;
        }
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
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string f, int idx, out IntPtr large, out IntPtr small, uint n);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

    private UIElement BuildSettingsDocument()
    {
        var c = new StackPanel();
        _settingsContent = c;
        ShowSettingsNav(_settingsNav);
        return c;
    }

    private UIElement BuildSettingsSideBar() => new StackPanel().Padding(12).Spacing(4).Children(
        new[] { SettingsNavButton("外观", SettingsAppearance) }
            .Concat(_context.SettingsSections.Sections.Select(s => SettingsNavButton(s.Label, s.Id)))
            .ToArray());

    private UIElement SettingsNavButton(string label, string id) => new Button()
        .Content(new Label().Text(label).WithTheme((_, l) => l.Foreground(_theme.SideBar.Foreground)))
        .OnClick(() => ShowSettingsNav(id)).CanDrag(false)
        .WithTheme((_, b) => b.Background(_theme.SideBar.Background));

    private void ShowSettingsNav(string id)
    {
        _settingsNav = id;
        _workbench.OpenDocument(SettingsDocumentId);
        if (_settingsContent is not { } c) return;
        c.Clear();
        c.Add(id == SettingsAppearance ? BuildAppearancePanel() : _context.SettingsSections.Build(id));
    }

    private UIElement BuildAppearancePanel()
    {
        var theme = _theme;
        var radios = _themeModes.Select(m => new RadioButton().GroupName("theme").IsChecked(theme.Mode == m.Mode).Content(new Label().Text(m.Label))).ToList();
        _themeRadios = radios;
        foreach (var (r, m) in radios.Zip(_themeModes)) r.OnCheckedChanged(checked_ => { if (checked_) theme.SetMode(m.Mode); });
        return new StackPanel().Padding(24).Spacing(12).Children(
            new Label().Text("外观").FontSize(20).Bold().WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)),
            new Label().Text("主题").FontSize(14).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)),
            new StackPanel().Spacing(6).Children(radios.Cast<Element>().ToArray()));
    }

    private UIElement BuildPluginPanel()
    {
        var panel = new StackPanel().Padding(24).Spacing(12);
        _pluginPanel = panel;
        RefreshPluginPanel();
        return panel;
    }

    private sealed class IpcSearchSourceAdapter : ISearchSource
    {
        private readonly Func<string, IReadOnlyList<SearchResult>> _fn;
        public IpcSearchSourceAdapter(string id, string displayName, Func<string, IReadOnlyList<SearchResult>> fn) { Id = id; DisplayName = displayName; _fn = fn; }
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<SearchResult> Search(string query, int maxResults) => _fn(query).Take(maxResults).ToList();
    }

    private sealed class CapturingOverlay : Mew.Workbench.IOverlayService
    {
        public static readonly List<ISearchSource> Captured = [];
        private readonly Mew.Workbench.IOverlayService _inner;
        public CapturingOverlay(Mew.Workbench.IOverlayService inner) { _inner = inner; }
        public void AddSearchSource(ISearchSource source) { _inner.AddSearchSource(source); Captured.Add(source); }
    }

    private void RefreshPluginPanel()
    {
        if (_pluginPanel is null) return;
        var theme = _theme;
        _pluginPanel.Clear();
        _pluginPanel.Add(new Label().Text("插件").FontSize(20).Bold().WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));
        var hasDll = _discoveredPlugins.Any(d => string.Equals(d.Manifest.Entry.Type, "dll", StringComparison.OrdinalIgnoreCase));
        if (hasDll)
        {
            _pluginPanel.Add(new StackPanel().Orientation(Orientation.Horizontal).Spacing(8).Children(
                new Label().Text("DLL 插件变更需重启扩展主机").FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning)),
                new Button().Content(new Label().Text("重启扩展主机")).CanDrag(false).OnClick(() => { _window.Close(); Environment.Exit(0); })
            ));
        }
        const bool isJitAvailable = true; // 扩展主机本身为 JIT，DLL 可加载
        if (_discoveredPlugins.Count == 0)
        {
            _pluginPanel.Add(new Label().Text("未发现插件（将 plugin.json 置于 %APPDATA%/Mew/Plugins/<id>/）").FontSize(12).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));
            return;
        }
        foreach (var desc in _discoveredPlugins)
        {
            var health = desc.Health(isJitAvailable);
            var enabled = _pluginEnables.IsEnabled(desc.Id);
            var healthText = health switch
            {
                PluginHealth.Healthy => enabled ? "已启用" : "已禁用",
                PluginHealth.InvalidManifest => "清单错误",
                PluginHealth.DuplicateId => "ID 重复",
                PluginHealth.NeedsJit => "需 JIT 扩展主机",
                _ => health.ToString()
            };
            var title = new Label().Text($"{desc.Manifest.DisplayName} ({desc.Id}) v{desc.Manifest.Version}").WithTheme((_, l) => l.Foreground(health == PluginHealth.InvalidManifest || health == PluginHealth.DuplicateId ? ShellIcons.HotkeyWarning : theme.EditorArea.Foreground));
            var healthLabel = new Label().Text(healthText).FontSize(11).WithTheme((_, l) => l.Foreground(health == PluginHealth.Healthy ? theme.EditorArea.Foreground : ShellIcons.HotkeyWarning));
            var toggle = new Button().Content(new Label().Text(enabled ? "禁用" : "启用")).CanDrag(false).OnClick(() => { _pluginEnables.SetEnabled(desc.Id, !enabled); _pluginEnables.Save(); RefreshPluginPanel(); });
            if (health == PluginHealth.InvalidManifest || health == PluginHealth.DuplicateId)
                toggle.Content(new Label().Text(healthText));
            if (desc.ValidationErrors.Count > 0)
            {
                var err = new Label().Text(string.Join("; ", desc.ValidationErrors)).FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning));
                _pluginPanel.Add(new StackPanel().Spacing(2).Children(title, healthLabel, err, toggle));
            }
            else
                _pluginPanel.Add(new StackPanel().Spacing(2).Children(title, healthLabel, toggle));
        }
    }
}
