using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
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
    private ExtensionHostPluginAdminService _pluginAdmin = null!;
    private StackPanel? _pluginPanel;
    private string _pluginNotice = "";
    private StackPanel? _hotkeyPanel;
    private bool _capturingHotkey;
    private string _hotkeyNotice = "";
    private PluginDllLoader? _dllLoader;
    private readonly List<IpcClient> _dllIpcClients = [];
    private IReadOnlyList<PluginLoadResult> _dllLoadResults = [];

    internal void Run()
    {
        MewDockShell.Localize(); // MewDock 适配（ADR-000205）：字符串表设置住 adapter,须在首个 DockingManager 构造前
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
        var userPluginsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "Plugins");
        var installPluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        // 单一来源：优先宿主快照（缺失或损坏回退本地扫描，保证双击独立可用）
        var fromSnapshot = new PluginSnapshotStore().Load();
        _discoveredPlugins = fromSnapshot.Count > 0
            ? fromSnapshot
            : new PluginDiscovery().Discover(PluginDiscovery.ResolvePluginRoots(_settings.PluginDirs, userPluginsDir, installPluginsDir));

        PluginHostLog.Write(fromSnapshot.Count > 0
            ? $"插件来源：宿主快照（{fromSnapshot.Count} 项）"
            : $"插件来源：本地扫描（快照缺失/损坏，回退；根={string.Join("；", PluginDiscovery.ResolvePluginRoots(_settings.PluginDirs, userPluginsDir, installPluginsDir))})");

        workbench.Theme(tc => tc.SetMode(LoadThemeMode()).SetAccent(Accent.Blue));

        // 宿主设置节：插件列表（发现结果）先于模块节注册，保证顺序 外观/插件/热键/模块节
        settingsSections.Add("plugins", "插件", BuildPluginPanel);
        settingsSections.Add("hotkeys", "热键", BuildHotkeyPanel);

        // T1 已摘除：Launcher 转为标准 T2 插件（见 src/Mew.Launcher/plugin.json），不再编译进扩展主机。

        // T2 DLL 运行时加载（按目录 ALC 隔离）
        _dllLoader = new PluginDllLoader();
        _dllLoadResults = _dllLoader.Load(_discoveredPlugins, _pluginEnables, () =>
        {
            // 为 DLL 插件创建捕获式 overlay，记录其注册的搜索源以便经 IPC 代理至宿主
            var capturingOverlay = new CapturingOverlay(overlay);
            return new ToolModuleContext(workbench, window.Handle, window, hotkeys, settings, capturingOverlay, theme, settingsSections);
        });
        LogPluginLifecycles();
        // 插件管理 adapter（ADR-000204）：行来源 = 发现结果 + 本地只读缓存 + 实载集合，写走一次性 IPC
        _pluginAdmin = new ExtensionHostPluginAdminService(
            _discoveredPlugins, _pluginEnables,
            () => _dllLoader?.Loaded.Select(x => x.Descriptor.Id).ToArray() ?? [],
            EnablePluginIpc.TryRequest);
        // 将捕获的源通过管道注册到宿主（内存直连模式下 _ipcServer 为空则走管道）
        foreach (var src in CapturingOverlay.Captured.ToList())
        {
            var cap = _discoveredPlugins.FirstOrDefault(d => d.Id == src.Id)?.Manifest.Capabilities;
            var dto = cap?.Search != null ? new PluginCapabilitiesDto(new SearchCapabilityDto(cap.Search.ProviderId, cap.Search.DisplayName), null, null) : new PluginCapabilitiesDto(null, null, null);
            var client = new IpcClient(src, src.Id, src.DisplayName, 1, dto);
            if (client.Connect(out _)) _dllIpcClients.Add(client);
        }
        CapturingOverlay.Captured.Clear();

        // 内部插件：五区本身视为首个内部插件的占位描述（与外部插件同等可见）
        // 实际五区贡献已由各模块完成，此处仅为清单语义保留

        // 底部面板「插件日志」：内存环中的启动期生命周期（与 plugin-host.log 同源）
        workbench.Panel(panel => panel.View("plugin-log", "插件日志", BuildLogPanel()));

        // 宿主贡献：设置上下文（活动栏/侧边栏/编辑器文档），设置节 = 外观 + 模块节
        workbench
            .ActivityBar(bar => bar.Item("settings", "设置", ShellIcons.ActivityGlyph(_theme, ShellIcons.SettingsIconData)))
            .SideBar(side => side.View("settings", "设置", BuildSettingsSideBar()))
            .EditorArea(editor => editor.Document(SettingsDocumentId, "设置", BuildSettingsDocument()));

        _titleThemeButton = TitleBarBuilder.BuildTitleBar(window, Quit, OpenSettings, CycleTheme, workbench, ShowAbout);
        UpdateThemeButton();

        window.PreviewKeyDown += OnPluginHostPreviewKeyDown;
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

        void Quit() => Application.Shutdown();
    }

    /// <summary>底部面板「插件日志」视图：与 plugin-host.log 同源的启动期生命周期快照；整块只读多行文本，拖选复制。</summary>
    private UIElement BuildLogPanel()
    {
        var text = string.Join("\n", PluginHostLog.SnapshotLines().Select(line => $"{line.Time:HH:mm:ss}  {line.Message}"));
        return new MultiLineTextBox { Text = text, CanDrag = false, IsReadOnly = true, BorderThickness = 0, Wrap = true }
            .FontSize(12)
            .WithTheme((theme, box) => box.Foreground(theme.Palette.WindowText));
    }

    /// <summary>插件生命周期日志：每个发现项的去向（加载成功/跳过/失败原因），成功项附 DLL 路径、大小、写入时间与模块类型，
    /// 便于确认实际加载的是哪一次构建的产物。</summary>
    private void LogPluginLifecycles()
    {
        foreach (var desc in _discoveredPlugins.Where(d => !PluginDiscovery.IsReservedHostId(d.Id)))
        {
            var result = _dllLoadResults.FirstOrDefault(r => string.Equals(r.Descriptor.Id, desc.Id, StringComparison.OrdinalIgnoreCase));
            if (result is null)
            {
                PluginHostLog.Write($"插件 {desc.Id}：跳过（独立进程插件，由宿主管理）");
                continue;
            }
            if (!result.Success)
            {
                PluginHostLog.Write($"插件 {desc.Id}：未加载（{result.Error}）");
                continue;
            }
            var loaded = _dllLoader?.Loaded.FirstOrDefault(x => string.Equals(x.Descriptor.Id, desc.Id, StringComparison.OrdinalIgnoreCase));
            var dllInfo = DescribeDll(desc);
            PluginHostLog.Write($"插件 {desc.Id}：加载成功（{dllInfo} → {loaded?.Module.GetType().FullName}，Configure 成功）");
        }
    }

    private static string DescribeDll(PluginDescriptor desc)
    {
        try
        {
            var pluginDir = Path.GetDirectoryName(desc.ManifestPath);
            if (pluginDir is null) return $"清单 {desc.ManifestPath}（目录未知）";
            var dllPath = Path.Combine(pluginDir, desc.Manifest.Entry.Path);
            var info = new FileInfo(dllPath);
            if (!info.Exists) return $"{dllPath}（不存在）";
            return $"{dllPath}（{info.Length} 字节，写入 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}）";
        }
        catch (Exception ex)
        {
            return $"{desc.ManifestPath}（不可访问：{ex.Message}）";
        }
    }

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

    private string DefaultSeedDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "Plugins");
    private string InstallPluginsDir => Path.Combine(AppContext.BaseDirectory, "Plugins");

    /// <summary>可编辑的目录列表：未配置时以用户目录为种子；第一项为默认目录。</summary>
    private List<string> EditablePluginDirs() => new(_settings.PluginDirs is { Count: > 0 } ? _settings.PluginDirs : [DefaultSeedDir]);

    private void SavePluginDirs(List<string> dirs)
    {
        _settings.PluginDirs = dirs;
        _settings.Save();
        RefreshPluginPanel();
    }

    private void BuildPluginDirsSection(WorkbenchThemeContext theme)
    {
        if (_pluginPanel is null) return;
        _pluginPanel.Add(new Label().Text("插件目录").FontSize(14).Bold().WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));
        var dirs = EditablePluginDirs();
        for (var i = 0; i < dirs.Count; i++)
            AddEditableDirRow(dirs, i);
        AddLockedDirRow(InstallPluginsDir, "安装目录（随包内置）");

        var feedback = new Label().Text("").FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning));
        var input = new TextBox { Placeholder = @"新增目录，如 D:\dev-plugins", CanDrag = false }.Width(380);
        var addButton = new Button().Content(new Label().Text("添加")).CanDrag(false).OnClick(() =>
        {
            var dir = input.Text?.Trim();
            if (string.IsNullOrEmpty(dir))
            {
                feedback.Text = "请输入目录路径";
                return;
            }
            var current = EditablePluginDirs();
            if (current.Contains(dir, StringComparer.OrdinalIgnoreCase)
                || string.Equals(dir, InstallPluginsDir, StringComparison.OrdinalIgnoreCase))
            {
                feedback.Text = "目录已在列表中";
                return;
            }
            current.Add(dir);
            SavePluginDirs(current);
        });
        _pluginPanel.Add(new StackPanel().Orientation(Orientation.Horizontal).Spacing(8).Children(input, addButton));
        _pluginPanel.Add(feedback);
        _pluginPanel.Add(new Label().Text("至少保留一项，第一项为默认目录，重复 id 以靠前的目录为准；增减后需重启宿主（刷新快照）与主界面（加载 DLL）；不存在的目录会被忽略").FontSize(11).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));

        void AddEditableDirRow(List<string> list, int index)
        {
            var dir = list[index];
            var exists = Directory.Exists(dir);
            var tag = index == 0 ? "默认" : $"#{index + 1}";
            var pathLabel = new Label().Text(dir).FontSize(12).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground));
            var tagLabel = new Label().Text(exists ? tag : $"{tag}（不存在，将被忽略）").FontSize(11)
                .WithTheme((_, l) => l.Foreground(exists ? theme.EditorArea.Foreground : ShellIcons.HotkeyWarning));
            var row = new StackPanel().Spacing(2).Children(pathLabel, tagLabel);
            var buttons = new StackPanel().Orientation(Orientation.Horizontal).Spacing(8).Children(row);
            if (index > 0)
            {
                var defaultButton = new Button().Content(new Label().Text("设为默认")).CanDrag(false).OnClick(() =>
                {
                    var current = EditablePluginDirs();
                    var target = current.FirstOrDefault(d => string.Equals(d, dir, StringComparison.OrdinalIgnoreCase));
                    if (target is null) return;
                    current.Remove(target);
                    current.Insert(0, target);
                    SavePluginDirs(current);
                });
                buttons.Add(defaultButton);
            }
            if (list.Count > 1)
            {
                var removeButton = new Button().Content(new Label().Text("删除")).CanDrag(false).OnClick(() =>
                {
                    var current = EditablePluginDirs();
                    current.RemoveAll(d => string.Equals(d, dir, StringComparison.OrdinalIgnoreCase));
                    SavePluginDirs(current);
                });
                buttons.Add(removeButton);
            }
            _pluginPanel!.Add(buttons);
        }

        void AddLockedDirRow(string dir, string tag)
        {
            var exists = Directory.Exists(dir);
            var pathLabel = new Label().Text(dir).FontSize(12).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground));
            var tagLabel = new Label().Text(exists ? tag : $"{tag}（不存在，将被忽略）").FontSize(11)
                .WithTheme((_, l) => l.Foreground(exists ? theme.EditorArea.Foreground : ShellIcons.HotkeyWarning));
            _pluginPanel!.Add(new StackPanel().Spacing(2).Children(pathLabel, tagLabel));
        }
    }

    private void OnPluginHostPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturingHotkey)
        {
            _workbench.NotifyWindowKeyDown(e);
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            _capturingHotkey = false;
            _hotkeyNotice = "";
            RefreshHotkeyPanel();
            return;
        }

        var parts = new List<string>();
        if (e.ControlKey) parts.Add("Ctrl");
        if (e.AltKey) parts.Add("Alt");
        if (e.ShiftKey) parts.Add("Shift");
        if (e.MetaKey) parts.Add("Win");
        var name = HotkeyKeys.NameOf(e.Key);
        if (name.Length == 0)
        {
            _hotkeyNotice = "请按字母/数字/功能键组合";
            RefreshHotkeyPanel();
            return;
        }

        if (parts.Count == 0)
        {
            _hotkeyNotice = "需要至少一个修饰键";
            RefreshHotkeyPanel();
            return;
        }

        parts.Add(name);
        var hotkey = string.Join("+", parts);
        if (!HotkeyParser.TryParse(hotkey, out _, out _))
        {
            _hotkeyNotice = "不支持的组合";
            RefreshHotkeyPanel();
            return;
        }

        ApplyOverlayHotkey(hotkey, enabled: true);
    }

    private void ApplyOverlayHotkey(string? hotkey, bool enabled)
    {
        // 先落盘（宿主未运行时重启后生效），再尽力 IPC 实时应用
        if (enabled && !string.IsNullOrWhiteSpace(hotkey))
            _settings.OverlayHotkey = hotkey;
        _settings.OverlayHotkeyEnabled = enabled;
        _settings.Save();
        _capturingHotkey = false;

        var ack = OverlayHotkeyIpc.TrySet(hotkey, enabled, out var transportError);
        _hotkeyNotice = ack switch
        {
            { Ok: true, Enabled: true } => $"已生效：{ack.Hotkey}",
            { Ok: true } => "呼出热键已禁用（托盘仍可呼出浮层）",
            { Error: not null } => ack.Error,
            _ => $"宿主未连接，已保存（{transportError}），重启宿主后生效",
        };
        RefreshHotkeyPanel();
    }

    private UIElement BuildHotkeyPanel()
    {
        var panel = new StackPanel().Padding(24).Spacing(12);
        _hotkeyPanel = panel;
        RefreshHotkeyPanel();
        return panel;
    }

    private void RefreshHotkeyPanel()
    {
        if (_hotkeyPanel is null) return;
        var theme = _theme;
        _hotkeyPanel.Clear();
        _hotkeyPanel.Add(new Label().Text("呼出热键").FontSize(20).Bold().WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));

        var enabled = _settings.OverlayHotkeyEnabled;
        var hotkey = string.IsNullOrWhiteSpace(_settings.OverlayHotkey) ? HotkeyService.DefaultOverlayHotkey : _settings.OverlayHotkey;
        var display = enabled ? hotkey : $"{hotkey}（已禁用）";
        var toggle = new Button().Content(new Label().Text(enabled ? "禁用" : "启用")).CanDrag(false)
            .OnClick(() => ApplyOverlayHotkey(enabled ? hotkey : _settings.OverlayHotkey, enabled: !enabled));
        var change = new Button().Content(new Label().Text(_capturingHotkey ? "按组合键…（Esc 取消）" : "更改")).CanDrag(false)
            .OnClick(() =>
            {
                _capturingHotkey = true;
                _hotkeyNotice = "请直接按键…（Esc 取消）";
                RefreshHotkeyPanel();
            });
        _hotkeyPanel.Add(new StackPanel().Spacing(2).Children(
            new Label().Text(display).FontSize(14).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)),
            new StackPanel().Orientation(Orientation.Horizontal).Spacing(8).Children(toggle, change)));
        if (!string.IsNullOrEmpty(_hotkeyNotice))
            _hotkeyPanel.Add(new Label().Text(_hotkeyNotice).FontSize(11).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));
        _hotkeyPanel.Add(new Label().Text("修改/禁用实时经 IPC 生效；宿主未运行时仅保存，重启宿主后生效").FontSize(11).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));
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
        BuildPluginDirsSection(theme);
        var hasDll = _discoveredPlugins.Any(PluginDiscovery.IsExtensionHostManaged);
        if (hasDll)
        {
            _pluginPanel.Add(new StackPanel().Orientation(Orientation.Horizontal).Spacing(8).Children(
                new Label().Text("DLL 插件变更需重启主界面（先退出进程，再经托盘手动打开）").FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning)),
                new Button().Content(new Label().Text("退出主界面进程")).CanDrag(false).OnClick(() => { _window.Close(); Environment.Exit(0); })
            ));
        }
        const string emptyStateText = "未发现插件（将 plugin.json 置于 %APPDATA%/Mew/Plugins/<id>/）";
        // 行区 = 共享插件管理面板（ADR-000204）：行渲染/动作路由只此一份；通知条仍归本进程（面板级内容不进共享模块）
        var rowsPanel = new PluginAdminPanel(_pluginAdmin, _theme, emptyStateText, emptyStateFontSize: 12,
            onApplied: (_, action, result) =>
            {
                _pluginNotice = result.Outcome switch
                {
                    PluginAdminOutcome.Ok => action == PluginRowAction.Enable ? "已启用（重启扩展主机后装载）" : "已禁用（重启扩展主机后生效）",
                    PluginAdminOutcome.Rejected => $"宿主未生效：{result.Error}",
                    _ => $"宿主未连接，操作未生效（{result.Error}）",
                };
                // 通知条在共享面板之外，整体重建让通知可见（面板自身的行刷新随后被本次重建覆盖，行为等价旧 TogglePluginEnabled）
                RefreshPluginPanel();
            });
        rowsPanel.Spacing = 12;
        rowsPanel.Refresh();
        _pluginPanel.Add(rowsPanel);
        if (!string.IsNullOrEmpty(_pluginNotice) && rowsPanel.RowCount > 0)
            _pluginPanel.Add(new Label().Text(_pluginNotice).FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning)));
    }
}
