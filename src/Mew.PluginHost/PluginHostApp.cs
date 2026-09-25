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
    private HostProxyPluginAdminService? _hostProxyAdmin;
    private StackPanel? _pluginPanel;
    private string _pluginNotice = "";
    private StackPanel? _hotkeyPanel;
    private HotkeyCapture _hotkeyCapture = null!;
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
        _hotkeyCapture = new HotkeyCapture(_hotkeys.FindOwner);
        _window = window;
        _context = context;

        settings.Load();

        // 插件发现：单一主目录（ADR-000301），展示在设置→插件列表
        _pluginEnables = new PluginEnableStore();
        _pluginEnables.Load();
        var primaryPluginDir = string.IsNullOrWhiteSpace(_settings.PluginDir)
            ? PluginDiscovery.DefaultUserPluginsDir
            : _settings.PluginDir!;
        // 单一来源：优先宿主快照（缺失或损坏回退本地扫描，保证双击独立可用）
        var fromSnapshot = new PluginSnapshotStore().Load();
        _discoveredPlugins = fromSnapshot.Count > 0
            ? fromSnapshot
            : new PluginDiscovery().Discover(primaryPluginDir);

        PluginHostLog.Write(fromSnapshot.Count > 0
            ? $"插件来源：宿主快照（{fromSnapshot.Count} 项）"
            : $"插件来源：本地扫描（快照缺失/损坏，回退；主目录={primaryPluginDir}）");

        workbench.Theme(tc => tc.SetMode(LoadThemeMode()).SetAccent(Accent.Blue));
        // TEMP-DIAG(活动栏双击排查,用后删除):记录每次呈现变更,定位第一次点击是否调到 SelectActivity
        workbench.PresentationChanged += () => PluginHostLog.Write(
            $"[DIAG] PresentationChanged active={workbench.ActiveActivityId} sidebar={workbench.IsSideBarVisible} tick={Environment.TickCount64}");

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
            window.HideToTray();
            // 扩展主机隐藏而非退出，宿主常驻可重新拉起（HideToTray:Win32 兜底防托盘重开后关不掉）
        };

        window.Loaded += () =>
        {
            // 0.20 迁移:Application.Run 不再自动显示窗口,显式置可见(Show + IsVisible 双保险)
            window.IsVisible = true;
            window.Show(null!);
            PluginHostLog.Write($"窗口 Show 后 IsVisible={window.IsVisible}");
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
        b.Content(new Label().Text(entry.Icon).FontSize(14)
            .TextAlignment(TextAlignment.Center)
            .VerticalTextAlignment(TextAlignment.Center));
        // ToolTip 摘除(原 b.ToolTip(entry.ToolTip)):上游 MewUI #253——tooltip 显示时首次点击
        // 只关闭 tooltip 不触发 Click;上游修复后随 TitleBarBuilder 一并恢复。
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
        // 整页滚动:目录与插件行多时超高内容不被窗口高度截断
        return new ScrollViewer { Content = panel, VerticalScroll = ScrollMode.Auto };
    }

    /// <summary>目录反馈标签（选择/校验失败时显示,其余隐藏不占位）。</summary>
    private Label? _dirFeedback;

    /// <summary>目录节（ADR-000301 单一主目录模型）：当前路径 + 修改（文件夹选择器）+ 反馈 + 精简说明。</summary>
    private void BuildPluginDirsSection(WorkbenchThemeContext theme)
    {
        if (_pluginPanel is null) return;
        var current = string.IsNullOrWhiteSpace(_settings.PluginDir)
            ? PluginDiscovery.DefaultUserPluginsDir
            : _settings.PluginDir!;
        _dirFeedback = BuildPluginDirsSection(_pluginPanel, theme, current, PickPluginDir);
    }

    /// <summary>目录节构造（静态便于无头测试）：返回反馈标签供调用方在失败时点亮。</summary>
    internal static Label BuildPluginDirsSection(StackPanel panel, WorkbenchThemeContext theme, string currentDir, Action pickPluginDir)
    {
        panel.Add(new Label().Text("插件目录").FontSize(14).Bold().WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));

        var currentLabel = new Label().Text(currentDir).FontSize(12).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground));
        var editButton = new Button().Content(new Label().Text("修改")).CanDrag(false).OnClick(pickPluginDir);
        panel.Add(new StackPanel().Orientation(Orientation.Horizontal).Spacing(8).Children(currentLabel, editButton));

        var feedback = new Label().Text("").FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning));
        feedback.IsVisible = false; // 空态不占位
        panel.Add(feedback);

        panel.Add(new Label()
            .Text("修改后重启宿主生效。开发期可把构建输出以链接挂入主目录：mklink /D 需开发者模式（可指 WSL 路径），mklink /J 免特权限本机卷。")
            .FontSize(11).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));
        return feedback;
    }

    /// <summary>修改主目录：托管文件夹选择器 → 校验存在性/重复 → 落盘并刷新（重启宿主后生效）。</summary>
    private void PickPluginDir()
    {
        var picked = FileDialog.SelectFolder(new FolderDialogOptions
        {
            Title = "选择插件主目录",
            InitialDirectory = Directory.Exists(_settings.PluginDir) ? _settings.PluginDir : PluginDiscovery.DefaultUserPluginsDir,
            Owner = _window,
        });
        if (string.IsNullOrWhiteSpace(picked)) return;
        if (string.Equals(picked, _settings.PluginDir, StringComparison.OrdinalIgnoreCase))
        {
            ShowDirFeedback("与当前主目录相同");
            return;
        }
        if (!Directory.Exists(picked))
        {
            ShowDirFeedback("目录不存在，将被忽略");
            return;
        }
        _settings.PluginDir = picked;
        _settings.Save();
        RefreshPluginPanel();
    }

    private void ShowDirFeedback(string message)
    {
        if (_dirFeedback is not { } label) return;
        label.Text = message;
        label.IsVisible = true;
    }

    /// <summary>呼出热键捕获态按键(捕获状态机归约,票据 01 起与启动项每项热键共享 HotkeyCapture):
    /// 未捕获直通工作台,Esc 清提示退出,提示点亮继续捕获,捕获成功走 ApplyOverlayHotkey 落点。</summary>
    private void OnPluginHostPreviewKeyDown(KeyEventArgs e)
    {
        var outcome = _hotkeyCapture.Process(e);
        switch (outcome.Disposition)
        {
            case HotkeyCaptureDisposition.PassThrough:
                _workbench.NotifyWindowKeyDown(e);
                return;
            case HotkeyCaptureDisposition.Cancelled:
                _hotkeyNotice = "";
                RefreshHotkeyPanel();
                return;
            case HotkeyCaptureDisposition.Notice:
                _hotkeyNotice = outcome.Notice!;
                RefreshHotkeyPanel();
                return;
            case HotkeyCaptureDisposition.Captured:
                ApplyOverlayHotkey(outcome.Hotkey!, enabled: true);
                return;
        }
    }

    private void ApplyOverlayHotkey(string? hotkey, bool enabled)
    {
        // 先落盘（宿主未运行时重启后生效），再尽力 IPC 实时应用；IPC 报错则回滚落盘值与旧键一致
        var previousHotkey = _settings.OverlayHotkey;
        var previousEnabled = _settings.OverlayHotkeyEnabled;
        if (enabled && !string.IsNullOrWhiteSpace(hotkey))
            _settings.OverlayHotkey = hotkey;
        _settings.OverlayHotkeyEnabled = enabled;
        _settings.Save();

        var ack = OverlayHotkeyIpc.TrySet(hotkey, enabled, out var transportError);
        _hotkeyNotice = ack switch
        {
            { Ok: true, Enabled: true } => $"已生效：{ack.Hotkey}",
            { Ok: true } => "呼出热键已禁用（托盘仍可呼出浮层）",
            { Error: not null } => Rollback(ack.Error),
            _ => $"宿主未连接，已保存（{transportError}），重启宿主后生效",
        };
        RefreshHotkeyPanel();

        string Rollback(string error)
        {
            _settings.OverlayHotkey = previousHotkey;
            _settings.OverlayHotkeyEnabled = previousEnabled;
            _settings.Save();
            return error;
        }
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
        var toggle = new ToggleSwitch().IsChecked(enabled)
            .OnCheckedChanged(checked_ => ApplyOverlayHotkey(checked_ ? hotkey : _settings.OverlayHotkey, enabled: checked_));
        var change = new Button().Content(new Label().Text(_hotkeyCapture.IsCapturing ? "按组合键…（Esc 取消）" : "更改")).CanDrag(false)
            .IsEnabled(enabled)
            .OnClick(() =>
            {
                _hotkeyCapture.Begin();
                _hotkeyNotice = "请直接按键…（Esc 取消）";
                RefreshHotkeyPanel();
            });
        var rowItems = new List<Element>
        {
            new Label().Text("启用").FontSize(12).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)),
            toggle,
            change,
        };
        if (!string.Equals(hotkey, HotkeyService.DefaultOverlayHotkey, StringComparison.Ordinal))
            rowItems.Add(new Button().Content(new Label().Text("恢复默认")).CanDrag(false)
                .OnClick(() => ApplyOverlayHotkey(HotkeyService.DefaultOverlayHotkey, enabled: true)));
        _hotkeyPanel.Add(new StackPanel().Spacing(2).Children(
            new Label().Text(display).FontSize(14).WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)),
            new StackPanel().Orientation(Orientation.Horizontal).Spacing(8).Children(rowItems.ToArray())));
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

        // 独立插件（T3）子节（ADR-000303）：行权威在宿主（引擎实态 + plugins.json），每次重建经一次性 IPC 现拉
        _pluginPanel.Add(new Label().Text("独立插件（T3，宿主托管）").FontSize(14).Bold().WithTheme((_, l) => l.Foreground(theme.EditorArea.Foreground)));
        var t3RowCount = 0;
        var fetched = HostPluginAdminIpc.TryFetchRows(out var transportError);
        if (fetched is { } rows)
        {
            _hostProxyAdmin ??= new HostProxyPluginAdminService();
            _hostProxyAdmin.SetRows(rows);
            var t3Panel = new PluginAdminPanel(_hostProxyAdmin, _theme,
                "无独立进程插件（entry.type=exe）", emptyStateFontSize: 12,
                footerText: "动作由宿主即时执行：启用即拉起进程、禁用即停止；已崩溃不自动重拉，点行内「重启」恢复",
                onApplied: OnHostProxyApplied);
            t3Panel.Spacing = 12;
            t3Panel.Refresh();
            _pluginPanel.Add(t3Panel);
            t3RowCount = t3Panel.RowCount;
        }
        else
        {
            _pluginPanel.Add(new Label().Text($"宿主不可达，独立插件暂不可管理（{transportError}）")
                .FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning)));
        }

        if (!string.IsNullOrEmpty(_pluginNotice) && rowsPanel.RowCount + t3RowCount > 0)
            _pluginPanel.Add(new Label().Text(_pluginNotice).FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning)));
    }

    /// <summary>T3 行内动作完成后的通知条文案：宿主即时执行语义（ADR-000303），三态诚实呈现。</summary>
    private void OnHostProxyApplied(string id, PluginRowAction action, PluginAdminResult result)
    {
        _ = id;
        _pluginNotice = result.Outcome switch
        {
            PluginAdminOutcome.Ok => action switch
            {
                PluginRowAction.Enable => "已启用（宿主已拉起进程）",
                PluginRowAction.Disable => "已禁用（宿主已停止进程）",
                PluginRowAction.Restart => "已重启",
                _ => "已生效",
            },
            PluginAdminOutcome.Rejected => $"宿主未生效：{result.Error}",
            _ => $"宿主不可达，操作未生效（{result.Error}）",
        };
        RefreshPluginPanel();
    }
}
