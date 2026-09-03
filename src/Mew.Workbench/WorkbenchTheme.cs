using Aprillz.MewUI;

namespace Mew.Workbench;

/// <summary>
/// Identifies one of the five Workbench zones.
/// </summary>
public enum WorkbenchZone
{
    ActivityBar,
    SideBar,
    EditorArea,
    Panel,
    StatusBar,
}

/// <summary>
/// Zone-specific colors read by Workbench zones and tool views.
/// </summary>
public sealed record ZonePalette(Color Background, Color Foreground, Color Accent)
{
    internal static ZonePalette Create(Theme theme, WorkbenchZone zone)
    {
        var palette = theme.Palette;

        return zone switch
        {
            WorkbenchZone.ActivityBar => new ZonePalette(palette.ControlBackground, palette.WindowText, palette.Accent),
            WorkbenchZone.SideBar => new ZonePalette(palette.ContainerBackground, palette.WindowText, palette.Accent),
            WorkbenchZone.EditorArea => new ZonePalette(palette.WindowBackground, palette.WindowText, palette.Accent),
            WorkbenchZone.Panel => new ZonePalette(palette.ContainerBackground, palette.WindowText, palette.Accent),
            WorkbenchZone.StatusBar => new ZonePalette(palette.ControlBackground, palette.WindowText, palette.Accent),
            _ => throw new ArgumentOutOfRangeException(nameof(zone)),
        };
    }
}

/// <summary>
/// Theme context that maps the active MewUI theme to Workbench zone palettes.
/// 实现 <see cref="IThemeService"/>:模式读取/切换 + <see cref="ThemeModeChanged"/> 变更通知。
/// </summary>
public sealed class WorkbenchThemeContext : IThemeService
{
    // 五个区各自的背景色覆盖:未设置的区取主题色板默认背景(亮暗主题自动适配)。
    private readonly Dictionary<WorkbenchZone, Color> _zoneBackgrounds = [];

    internal WorkbenchThemeContext()
    {
    }

    /// <summary>主题模式实际变更后触发(标题栏按钮、状态栏等同步)。</summary>
    public event Action? ThemeModeChanged;

    public ThemeVariant Mode => Application.IsRunning ? Application.Current!.ThemeMode : ThemeManager.Default;

    public bool IsDark => CurrentTheme.IsDark;

    public Color Accent => CurrentTheme.Palette.Accent;

    public ZonePalette ActivityBar => Get(WorkbenchZone.ActivityBar);

    public ZonePalette SideBar => Get(WorkbenchZone.SideBar);

    public ZonePalette EditorArea => Get(WorkbenchZone.EditorArea);

    public ZonePalette Panel => Get(WorkbenchZone.Panel);

    public ZonePalette StatusBar => Get(WorkbenchZone.StatusBar);

    public ZonePalette Get(WorkbenchZone zone)
    {
        var palette = ZonePalette.Create(CurrentTheme, zone);
        return _zoneBackgrounds.TryGetValue(zone, out var background)
            ? palette with { Background = background }
            : palette;
    }

    /// <summary>覆盖指定工作台区的背景色;未设置时取主题色板的区默认背景,亮暗主题自动适配。</summary>
    public WorkbenchThemeContext SetZoneBackground(WorkbenchZone zone, Color background)
    {
        _zoneBackgrounds[zone] = background;
        return this;
    }

    public WorkbenchThemeContext SetMode(ThemeVariant mode)
    {
        if (Mode == mode)
        {
            return this; // 同模式幂等,不触发变更事件
        }

        if (Application.IsRunning)
        {
            Application.Current!.SetThemeMode(mode);
        }
        else
        {
            ThemeManager.Default = mode;
        }

        ThemeModeChanged?.Invoke();
        return this;
    }

    void IThemeService.SetMode(ThemeVariant mode) => SetMode(mode);

    public WorkbenchThemeContext SetAccent(Accent accent)
    {
        if (Application.IsRunning)
        {
            Application.Current!.SetAccent(accent);
        }
        else
        {
            ThemeManager.DefaultAccent = accent;
        }

        return this;
    }

    public WorkbenchThemeContext SetAccent(Color accent)
    {
        if (Application.IsRunning)
        {
            Application.Current!.SetAccent(accent);
        }
        else
        {
            ThemeManager.DefaultAccentColor = accent;
        }

        return this;
    }

    private static Theme CurrentTheme => Application.IsRunning ? Application.Current!.Theme : CreateFallbackTheme();

    internal static Theme CreateFallbackTheme()
    {
        var isDark = ThemeManager.Default == ThemeVariant.Dark;
        var seed = isDark ? ThemeManager.DefaultDarkSeed : ThemeManager.DefaultLightSeed;
        var accent = ThemeManager.DefaultAccentColor ?? ThemeManager.DefaultAccent.GetAccentColor(isDark);

        return new Theme
        {
            Name = isDark ? "Dark" : "Light",
            Palette = new Palette(seed, accent),
            Metrics = ThemeManager.DefaultMetrics,
        };
    }
}
