using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Mew.Workbench;

/// <summary>
/// 自绘标题栏窗口壳:基于 DWM 原生帧扩展(ExtendClientAreaTitleBarHeight)替换系统标题栏,
/// 保留 OS 圆角、阴影、缩放与贴边;标题栏左/中/右三区可注入,窗口按钮原生优先、缺失时自绘兜底。
/// 实现参照 MewUI 官方样例 NativeCustomWindow(取舍见 ADR 000102-01)。
/// </summary>
public sealed class NativeChromeWindow : Window
{
    private const double TitleBarHeight = 28;
    private const double ButtonWidth = 32;
    private const double ChromeButtonSize = 4;

    private readonly Border _contentArea;
    private readonly Border _chromeBorder;
    private readonly AlphaTextPanel _titleBar;
    private readonly TextBlock _titleText;
    private readonly StackPanel _controlButtons;
    private readonly StackPanel _leftArea;
    private readonly StackPanel _rightArea;
    private readonly Button _minimizeBtn;
    private readonly Button _maximizeBtn;
    private Theme? _theme;

    public NativeChromeWindow()
    {
        ExtendClientAreaTitleBarHeight = TitleBarHeight;
        base.Padding = new Thickness(0);

        StyleSheet = new StyleSheet();
        StyleSheet.Define("chrome", CreateChromeButtonStyle);
        StyleSheet.Define("close", CreateCloseButtonStyle);

        _titleText = new TextBlock
        {
            IsHitTestVisible = false,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Margin = new Thickness(8, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _titleText.SetBinding(TextBlock.TextProperty, this, TitleProperty);

        _minimizeBtn = CreateChromeButton(GlyphKind.WindowMinimize);
        _minimizeBtn.Click += () => Minimize();
        _minimizeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanMinimizeProperty);

        var maxGlyph = new GlyphElement().Kind(GlyphKind.WindowMaximize).GlyphSize(ChromeButtonSize);
        _maximizeBtn = CreateChromeButton(maxGlyph);
        _maximizeBtn.Click += () =>
        {
            if (WindowState == WindowState.Maximized)
            {
                Restore();
            }
            else
            {
                Maximize();
            }
        };
        _maximizeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanMaximizeProperty);

        var closeBtn = CreateChromeButton(GlyphKind.Cross, isClose: true);
        closeBtn.Click += () => Close();
        closeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanCloseProperty);

        _controlButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _controlButtons.Add(_minimizeBtn);
        _controlButtons.Add(_maximizeBtn);
        _controlButtons.Add(closeBtn);

        _leftArea = new StackPanel { Orientation = Orientation.Horizontal };
        _rightArea = new StackPanel { Orientation = Orientation.Horizontal };

        var titleBarContent = new DockPanel().Children(
            new Border().DockRight().Child(_controlButtons),
            new Border().DockRight().Child(_rightArea),
            new Border().DockLeft().Child(_leftArea),
            _titleText
        );
        _titleBar = new AlphaTextPanel
        {
            MinHeight = TitleBarHeight,
            Content = titleBarContent,
        };
        _titleBar.SetBinding(BackgroundProperty, this, BackgroundProperty);

        _titleBar.MouseDown += e =>
        {
            if (e.Button == MouseButton.Left && e.ClickCount == 1)
            {
                DragMove();
                e.Handled = true;
            }
        };

        _titleBar.MouseDoubleClick += e =>
        {
            if (e.Button == MouseButton.Left && CanMaximize)
            {
                if (e.GetPosition(_titleBar) is Point p && (_leftArea.Bounds.Contains(p) || _rightArea.Bounds.Contains(p)))
                {
                    e.Handled = true;
                    return;
                }

                if (WindowState == WindowState.Maximized)
                {
                    Restore();
                }
                else
                {
                    Maximize();
                }
                e.Handled = true;
            }
        };

        _contentArea = new Border { Padding = new Thickness(0) };

        _chromeBorder = new Border
        {
            BorderThickness = 0,
            Child = new DockPanel().Children(
                _titleBar.DockTop(),
                _contentArea
            ),
        };
        _chromeBorder.SetBinding(Border.BorderBrushProperty, this, BorderBrushProperty);

        _contentArea.Child = new ContentPresenter();
        Template = new DelegateControlTemplate<NativeChromeWindow>((window, _) => window._chromeBorder);

        ClientSizeChanged += _ =>
        {
            OnWindowStateVisualUpdate();
            UpdateChromeButtonVisibility();
        };
        Activated += UpdateChromeAppearance;
        Deactivated += UpdateChromeAppearance;
        Loaded += OnLoaded;
    }

    private void OnAppThemeChanged(Theme oldTheme, Theme newTheme)
    {
        _theme = newTheme;
        UpdateChromeAppearance();

        // 0.19.1 对窗口模板(chrome)内容的主题重绘存在缺陷:运行时切主题后图标/菜单/标题可能不再重绘,
        // 强制整窗失效并整窗重排(PerformLayout)以触发重渲染。
        // OnThemeChanged 已不再调用 base(避免模板被标记 theme-stale),故 PerformLayout 内的
        // ApplyTemplate 不会 DetachTemplateInstance,内容树(含 MewDock FlexTabSetView)保持不动;
        // 全量 measure/arrange 会让各 Control 重解析样式并刷新继承值,使未显式 WithTheme 的默认文本
        // (单选标签、标题栏菜单等)也随主题更新颜色。
        Invalidate();
        InvalidateVisual();
        InvalidateMeasure();
        InvalidateArrange();
        PerformLayout();
    }

    /// <summary>标题栏左区注入点(如图标、菜单栏)。</summary>
    public StackPanel TitleBarLeft => _leftArea;

    /// <summary>标题栏右区注入点(如设置入口)。</summary>
    public StackPanel TitleBarRight => _rightArea;

    /// <summary>内容区边距;窗口级 Padding 重定向到内容区,标题栏不参与。</summary>
    public new Thickness Padding
    {
        get => _contentArea.Padding;
        set => _contentArea.Padding = value;
    }

    private static Style CreateChromeButtonStyle()
        => new(typeof(Button))
        {
            Transitions = [Transition.Create(Control.BackgroundProperty, 120, t => t)],
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace.WithAlpha(0)),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.PaddingProperty, new Thickness(0)),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonPressedBackground)],
                },
            ],
        };

    private static Style CreateCloseButtonStyle()
        => new(typeof(Button))
        {
            Transitions = [Transition.Create(Control.BackgroundProperty, 120, t => t)],
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, Color.FromRgb(232, 17, 35).WithAlpha(0)),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.PaddingProperty, new Thickness(0)),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, Color.FromRgb(232, 17, 35)),
                        // 0.19.1 中 ForegroundProperty 声明在 TextElement(Button 的基类链上),
                        // 而非样例 main 分支的 Control —— 勿改回 Control.ForegroundProperty(无法编译)。
                        Setter.Create(TextElement.ForegroundProperty, Color.White),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, Color.FromRgb(200, 12, 28)),
                        Setter.Create(TextElement.ForegroundProperty, Color.White),
                    ],
                },
            ],
        };

    private void OnLoaded()
    {
        if (BorderBrush.A > 0
            && !ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeBorderColor)
            && !ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeWindowBorder))
        {
            _chromeBorder.BorderThickness = 1;
        }

        // 0.19.1 中 Window 的 OnThemeChanged/ThemeChanged 不随应用主题切换触发(样例 main 分支行为不同),
        // 须监听 Application.ThemeChanged 才能让 chrome 配色跟随亮/暗切换。窗口可能在 Application.Run 之前创建,
        // 故在 Loaded(应用已运行)时补订阅。
        if (Application.IsRunning && Application.Current is { } app)
        {
            app.ThemeChanged -= OnAppThemeChanged;
            app.ThemeChanged += OnAppThemeChanged;
        }
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        if (ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeBorderColor)
            && property.Name == nameof(BorderBrush))
        {
            SetWindowBorderColor(BorderBrush);
        }
    }

    private void UpdateChromeAppearance()
    {
        var palette = CurrentTheme().Palette;
        // 焦点边框用 ControlBorder 与 Accent 低比例混合:保留激活/失焦区分,避免纯强调色边框过于醒目
        var accentBorder = IsActive
            ? palette.ControlBorder.Lerp(palette.Accent, 0.3)
            : palette.ControlBorder;

        // 不调用 base.OnThemeChanged(避免模板重建摘掉内容树)时,窗口自身的主题化属性不会自动更新,
        // 需手动同步:标题栏背景绑定窗口 Background;菜单等 chrome 文本继承窗口 Foreground,
        // 故背景与前景都按主题显式设置。
        Background = palette.WindowBackground;
        Foreground = palette.WindowText;
        BorderBrush = accentBorder;
        _titleText.Foreground = IsActive ? palette.WindowText : palette.DisabledText;
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        // 0.19.1 的 Control.OnThemeChanged 会把窗口模板标记为 theme-stale,下一次布局时 ApplyTemplate()
        // 会 DetachTemplateInstance() 重建模板根——整个内容树(含宿主 Content 里的 MewDock FlexTabSetView)
        // 被摘下,tabset 的 _content 经 ReleaseContent() 置空后不再重新解析,导致已打开的文档内容区
        // (如设置页)在切主题后空白(需切走/切回 tab 才恢复)。故此处不调用 base,模板保持不重建;
        // 子元素各自的 OnThemeChanged 仍会重解析样式;PerformLayout 做全量重排以刷新继承颜色。
        _theme = newTheme;
        UpdateChromeAppearance();

        Invalidate();
        InvalidateVisual();
        InvalidateMeasure();
        InvalidateArrange();
        PerformLayout();
    }

    private void UpdateChromeButtonVisibility()
    {
        bool hasExtend = ChromeCapabilities.HasFlag(WindowChromeCapabilities.ExtendClientArea);
        _titleBar.IsVisible = hasExtend;
        _controlButtons.IsVisible = !HasNativeChromeButtons;
        _titleBar.Padding = NativeChromeButtonInset;
    }

    private void OnWindowStateVisualUpdate()
    {
        if (_maximizeBtn.Content is GlyphElement glyph)
        {
            glyph.Kind = WindowState == WindowState.Maximized ? GlyphKind.WindowRestore : GlyphKind.WindowMaximize;
        }
    }

    private Theme CurrentTheme() =>
        _theme
        ?? (Application.IsRunning && Application.Current is { } app ? app.Theme : WorkbenchThemeContext.CreateFallbackTheme());

    private static Button CreateChromeButton(GlyphKind kind, bool isClose = false)
    {
        var glyph = new GlyphElement().Kind(kind).GlyphSize(ChromeButtonSize);
        return CreateChromeButton(glyph, isClose);
    }

    private static Button CreateChromeButton(Element content, bool isClose = false)
        => new()
        {
            Content = content,
            MinWidth = ButtonWidth,
            MinHeight = TitleBarHeight,
            StyleName = isClose ? "close" : "chrome",
        };

    /// <summary>
    /// 在 DWM 扩展标题栏区域启用 alpha 正确的文字渲染(GDI 后端需要)。
    /// </summary>
    internal sealed class AlphaTextPanel : ContentControl
    {
        protected override void RenderSubtree(IGraphicsContext context)
        {
            context.EnableAlphaTextHint = true;
            try
            {
                base.RenderSubtree(context);
            }
            finally
            {
                context.EnableAlphaTextHint = false;
            }
        }
    }
}
