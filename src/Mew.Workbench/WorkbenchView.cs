using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using System.Reflection;
using System.Text.Json;

namespace Mew.Workbench;

internal sealed class WorkbenchView
{
    private readonly Workbench _workbench;
    private DockingManager? _docking;
    private WorkbenchThemeContext? _theme;
    private UIElement? _activityBar;
    private UIElement? _statusBar;
    private readonly Dictionary<string, Button> _activityButtons = [];
    private readonly Dictionary<string, Label> _statusLabels = [];
    private bool _applyingChromeVisibility;
    // 每个停靠组件的主题化内容单一实例:布局恢复(ContentFactory)、默认面板与运行时打开(OpenDocument)
    // 必须解析到同一个实例。MewDock 的 SyncContent 在显式内容与 factory 内容实例不一致时会分离旧内容,
    // 而共享子元素(如设置文档的 StackPanel)的 Parent 仍指向已分离的旧包装,导致其无法重新挂接、tab 空白。
    private readonly Dictionary<string, UIElement> _paneContents = [];
    // 已接线「悬浮显示关闭按钮」的 tab 实例:布局变更重扫时去重,避免重复订阅鼠标事件。
    private readonly HashSet<object> _configuredTabClose = [];

    internal WorkbenchView(Workbench workbench) => _workbench = workbench;

    internal DockingManager Docking => _docking ?? throw new InvalidOperationException("Build() 尚未调用");

    internal UIElement Build()
    {
        var docking = new DockingManager();
        _docking = docking;
        var theme = _workbench.ThemeContext;
        _theme = theme;
        var layoutStore = new WorkbenchLayoutStore();

        docking.WithContentFactory(pane => ResolvePaneContent(pane, theme));

        if (layoutStore.TryLoad() is { } savedLayout)
        {
            try
            {
                docking.LoadLayout(savedLayout);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                AddDefaultPanes(docking, theme);
            }
        }
        else
        {
            AddDefaultPanes(docking, theme);
        }

        // 标签栏「最大化/恢复」按钮无实际效果,禁用以隐藏。FlexTabSetView 在构造时按
        // TabSetEnableMaximize 决定是否创建按钮,而模型在首次布局时才创建,故在每次布局变更后
        // 重新断言该 flag(首次启动的 AddDocumentPane 路径也覆盖到)。
        DisableTabSetMaximize(docking);
        ThinDockSplitters(docking); // 侧边栏/编辑器区、编辑器区/底部面板之间的拖动分隔条做到最细

        if (layoutStore.TryLoadPresentation() is { } presentation)
        {
            _workbench.RestorePresentation(presentation);
        }

        docking.Changed += (_, _) =>
        {
            // 布局变更可能新建 tabset 视图:重新断言模型 flag + 视图层直接隐藏按钮 + 接线 tab 关闭按钮悬浮显示
            DisableTabSetMaximize(docking);
            HideMaximizeButtons(docking);
            ConfigureTabCloseHover(docking);
            WireSplitterCursors(docking);
            layoutStore.Save(docking.SaveLayout());
        };
        docking.Changed += (_, _) =>
        {
            if (!_applyingChromeVisibility)
            {
                SynchronizeToolPaneVisibility(docking);
            }
        };
        docking.TabMenuOpening += (_, args) =>
        {
            if (_workbench.CanRevealDocument(args.Pane.Component))
            {
                args.Menu.Item("在侧边栏定位", () => _workbench.RevealDocument(args.Pane.Component!));
            }
        };
        _workbench.PresentationChanged += ApplyChromeVisibility;
        _workbench.PresentationChanged += () => layoutStore.SavePresentation(
            new WorkbenchPresentationState
            {
                ActiveActivityId = _workbench.ActiveActivityId,
                IsActivityBarVisible = _workbench.IsActivityBarVisible,
                IsSideBarVisible = _workbench.IsSideBarVisible,
                IsPanelVisible = _workbench.IsPanelVisible,
                IsStatusBarVisible = _workbench.IsStatusBarVisible,
            });
        var shell = BuildShell(docking);
        ApplyChromeVisibility();
        DisableDockZoneBorders(docking);
        HideMaximizeButtons(docking); // 初始 tabset 视图已就绪,视图层隐藏最大化按钮
        ConfigureTabCloseHover(docking); // 初始 tab 的关闭按钮默认隐藏,悬浮时显示
        WireSplitterCursors(docking); // 拖动分隔条时鼠标样式变为缩放指针
        return shell;
    }

    /// <summary>应用外壳区域显隐:活动栏/状态栏直接控制;侧边栏/底部面板按 id 查找并 Close/重建 tool pane。</summary>
    private void ApplyChromeVisibility()
    {
        _applyingChromeVisibility = true;
        try
        {
            if (_activityBar is not null)
            {
                _activityBar.IsVisible = _workbench.IsActivityBarVisible;
            }

            if (_statusBar is not null)
            {
                _statusBar.IsVisible = _workbench.IsStatusBarVisible;
            }

            ApplySideBarVisibility();
            ApplyActivitySelection();

            foreach (var panel in _workbench.PanelModel.Views)
            {
                ApplyToolPane(panel.Id, panel.Title, panel.Content, DockEdge.Bottom, WorkbenchZone.Panel, _workbench.IsPanelVisible);
            }
        }
        finally
        {
            _applyingChromeVisibility = false;
        }
    }

    private void ApplySideBarVisibility()
    {
        if (!_workbench.IsSideBarVisible)
        {
            foreach (var view in _workbench.SideBarModel.Views)
            {
                _docking!.Panes.FirstOrDefault(pane => pane.Component == view.Id)?.Close();
            }

            return;
        }

        if (_workbench.ActiveSideBarView is not { } side)
        {
            return;
        }

        // 先确保目标 pane 存在,再关闭其他 view 的 pane:保证 Left border 始终至少保留一个 tab,
        // border 不会随 Close 销毁——手动调整的侧边栏宽度得以保留。
        ApplyToolPane(side.Id, side.Title, side.Content, DockEdge.Left, WorkbenchZone.SideBar, true);

        foreach (var view in _workbench.SideBarModel.Views.Where(view => view.Id != side.Id))
        {
            _docking!.Panes.FirstOrDefault(pane => pane.Component == view.Id)?.Close();
        }
    }

    private void ApplyActivitySelection()
    {
        foreach (var (id, button) in _activityButtons)
        {
            button.Background(ActivityButtonBackground(id));
        }
    }

    /// <summary>
    /// MewDock 的工具窗格关闭按钮直接修改停靠树,不会经过 Workbench.Toggle*。
    /// 因此布局变更后按当前存在的窗格回写区域状态,使 View 菜单、持久化状态与实际界面一致。
    /// </summary>
    private void SynchronizeToolPaneVisibility(DockingManager docking)
    {
        bool? sideBarVisible = _workbench.ActiveSideBarView is { } sideBar
            ? docking.Panes.Any(pane => pane.Component == sideBar.Id)
            : null;
        bool? panelVisible = _workbench.PanelModel.Views.Count > 0
            ? _workbench.PanelModel.Views.Any(view => docking.Panes.Any(pane => pane.Component == view.Id))
            : null;

        _workbench.SynchronizeToolPaneVisibility(sideBarVisible, panelVisible);
    }

    /// <summary>活动栏按钮背景:选中项为 accent 与区背景按 2:8 回混的低调选中色,其余为区背景。随主题与选中态重算。</summary>
    private Color ActivityButtonBackground(string id) =>
        id == _workbench.ActiveActivityId ? ActivityBarSelectedBackground : _theme!.ActivityBar.Background;

    /// <summary>活动栏选中背景:accent 与区背景按 2:8 回混的低调选中色(替代整块鲜艳 accent),与启动项列表选中一致。</summary>
    private Color ActivityBarSelectedBackground =>
        _theme!.ActivityBar.Accent.Lerp(_theme.ActivityBar.Background, 0.8);

    /// <summary>显示时按 id 查找(布局持久化路径下 pane 由 factory 创建,不依赖 AddDefaultPanes 缓存);隐藏时 Close。</summary>
    private void ApplyToolPane(string id, string title, UIElement content, DockEdge edge, WorkbenchZone zone, bool visible)
    {
        var pane = _docking!.Panes.FirstOrDefault(p => p.Component == id);
        if (visible)
        {
            // pane 已存在时直接激活,不 Close+Add 重建:重建会新建 border,丢失手动调整的宽度。
            // 布局持久化后 pane 存在却隐藏边框的情形,Activate 同样使其恢复可见。
            if (pane is null)
            {
                pane = _docking.AddToolPane(title, PaneContent(id, content, zone), edge, id);
            }

            pane.Activate();
        }
        else
        {
            pane?.Close();
        }
    }

    /// <summary>
    /// 返回停靠组件的主题化内容单一实例,并在首次访问时缓存。所有内容解析路径(factory 恢复、默认面板、
    /// 运行时打开)共用缓存,保证 MewDock 的显式内容(_explicitContent)与 ContentFactory 解析结果是同一实例,
    /// 避免 SyncContent 因实例不一致而分离内容后无法重新挂接(共享子元素的 Parent 仍指向旧包装)。
    /// </summary>
    private UIElement PaneContent(string id, UIElement content, WorkbenchZone zone)
    {
        if (_paneContents.TryGetValue(id, out var cached))
        {
            return cached;
        }

        return _paneContents[id] = ThemedPane(content, _theme!, zone);
    }

    /// <summary>运行时打开编辑器文档时使用的主题化内容:与布局恢复路径解析到的实例一致。</summary>
    internal UIElement EditorPaneContent(string id)
    {
        var document = _workbench.EditorAreaModel.Documents.FirstOrDefault(d => d.Id == id)
            ?? throw new ArgumentException($"不存在编辑器文档“{id}”。", nameof(id));
        return PaneContent(id, document.Content, WorkbenchZone.EditorArea);
    }

    private static readonly MethodInfo DefineStyleRule = typeof(StyleSheet)
        .GetMethods()
        .FirstOrDefault(m => m.Name == nameof(StyleSheet.Define)
            && m.IsGenericMethodDefinition
            && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(Style))
        ?? throw new MissingMethodException(nameof(StyleSheet), nameof(StyleSheet.Define));

    /// <summary>
    /// MewDock 内置 DockStyles 给 tabset / 侧边栏 / Tab 按钮画边框(默认 ControlBorder,焦点时 ControlBorder→Accent 75% 混合)。
    /// 五个工作台区按设计不显示边框:FlexLayoutView 的 StyleSheet 按类型注册 rule 且 GetByType 从后往前匹配——
    /// 向其中追加覆盖 rule 即可关闭边框。目标控件类型在 MewDock 中是 internal,无法静态引用,故经反射按名解析类型。
    /// </summary>
    private static void DisableDockZoneBorders(DockingManager docking)
    {
        if (docking.Children.FirstOrDefault() is not FrameworkElement { StyleSheet: { } sheet })
        {
            return;
        }

        var assembly = typeof(DockingManager).Assembly;
        OverrideStyle(assembly, sheet, "Aprillz.MewUI.MewDock.Controls.FlexTabSetView", CreateBorderlessTabSetStyle);
        OverrideStyle(assembly, sheet, "Aprillz.MewUI.MewDock.Extended.ExtendedBorderBar", CreateBorderlessBorderBarStyle);
        OverrideStyle(assembly, sheet, "Aprillz.MewUI.MewDock.Controls.FlexTabButton", CreateBorderlessTabButtonStyle);
        OverrideStyle(assembly, sheet, "Aprillz.MewUI.MewDock.Controls.FlexSplitter", CreateThinSplitterStyle);
    }

    /// <summary>
    /// 拖动分隔条(FlexSplitter)做到最细:常驻 grip 线去掉(平时不可见),悬停/拖动时仅显示
    /// 细的 accent 高亮;与 SplitterSize=1 配合,避免细尺寸下 grip 线(长度按宽度-8 计算)溢出。
    /// </summary>
    private static Style CreateThinSplitterStyle(Type type) => new(type)
    {
        Transitions = [Transition.Create(Control.BackgroundProperty, 200, t => t)],
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, Color.Transparent),
            Setter.Create(Control.BorderBrushProperty, Color.Transparent),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.WithAlpha(26))],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Pressed,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.WithAlpha(48))],
            },
        ],
    };

    /// <summary>
    /// 编辑器区/侧边栏/底部面板的 tabset:无边框。BorderThickness 置 0 后 FlexTabSetView 的 body
    /// 只画背景不画边框;圆角一并清零,避免 body 背景与相邻区之间出现缺角。
    /// </summary>
    private static Style CreateBorderlessTabSetStyle(Type type) => new(type)
    {
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
            Setter.Create(Control.BorderBrushProperty, Color.Transparent),
            Setter.Create(Control.CornerRadiusProperty, 0.0),
            Setter.Create(Control.BorderThicknessProperty, 0.0),
        ],
    };

    /// <summary>
    /// 自动隐藏边缘条(ExtendedBorderBar)的折叠面板:其边框在 OnRender 里硬编码取
    /// Theme.Metrics.ControlBorderThickness,无法用 BorderThickness 关闭——把 BorderBrush 设为
    /// 透明即可让 DrawBackgroundAndBorder 跳过边框绘制(背景仍按原样填充)。
    /// </summary>
    private static Style CreateBorderlessBorderBarStyle(Type type) => new(type)
    {
        Setters = [Setter.Create(Control.BorderBrushProperty, Color.Transparent)],
    };

    /// <summary>
    /// Tab 按钮(FlexTabButton):不显示边框。未选中项背景与 tab 栏一致(ContainerBackground),
    /// 仅选中项用编辑器区背景(WindowBackground)区分,并与下方编辑内容连成一体。
    /// </summary>
    private static Style CreateBorderlessTabButtonStyle(Type type) => new(type)
    {
        Transitions = [Transition.Create(Control.BackgroundProperty, 200, t => t)],
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
            Setter.Create(Control.BorderBrushProperty, Color.Transparent),
            Setter.Create(TextElement.ForegroundProperty, t => t.Palette.WindowText),
            Setter.Create(Control.PaddingProperty, new Thickness(8.0, 2.0, 8.0, 2.0)),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, 0.0),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Selected,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.WindowBackground)],
            },
        ],
    };

    private static void OverrideStyle(Assembly assembly, StyleSheet sheet, string typeName, Func<Type, Style> factory)
    {
        if (assembly.GetType(typeName) is not { } type)
        {
            return;
        }

        DefineStyleRule.MakeGenericMethod(type).Invoke(sheet, [factory(type)]);
    }

    private UIElement BuildShell(DockingManager docking)
    {
        _activityBar = BuildActivityBar();
        _statusBar = BuildStatusBar();
        return new Grid()
            .Rows("*,Auto")
            .Columns("Auto,*")
            .Children(
                _activityBar.Row(0).Column(0),
                docking.Row(0).Column(1),
                _statusBar.Row(1).Column(0).ColumnSpan(2)
            );
    }

    private void AddDefaultPanes(DockingManager docking, WorkbenchThemeContext theme)
    {
        foreach (var view in _workbench.SideBarModel.Views)
        {
            docking.AddToolPane(view.Title, PaneContent(view.Id, view.Content, WorkbenchZone.SideBar), DockEdge.Left, view.Id);
        }

        foreach (var document in _workbench.EditorAreaModel.Documents)
        {
            docking.AddDocumentPane(document.Title, PaneContent(document.Id, document.Content, WorkbenchZone.EditorArea), document.Id);
        }

        foreach (var view in _workbench.PanelModel.Views)
        {
            docking.AddToolPane(view.Title, PaneContent(view.Id, view.Content, WorkbenchZone.Panel), DockEdge.Bottom, view.Id);
        }
    }

    private UIElement? ResolvePaneContent(DockPane pane, WorkbenchThemeContext theme)
    {
        if (pane.Component is not { } id)
        {
            return null;
        }

        foreach (var view in _workbench.SideBarModel.Views)
        {
            if (view.Id == id)
            {
                return PaneContent(view.Id, view.Content, WorkbenchZone.SideBar);
            }
        }

        foreach (var document in _workbench.EditorAreaModel.Documents)
        {
            if (document.Id == id)
            {
                return PaneContent(document.Id, document.Content, WorkbenchZone.EditorArea);
            }
        }

        foreach (var view in _workbench.PanelModel.Views)
        {
            if (view.Id == id)
            {
                return PaneContent(view.Id, view.Content, WorkbenchZone.Panel);
            }
        }

        return null;
    }

    private static UIElement ThemedPane(UIElement content, WorkbenchThemeContext theme, WorkbenchZone zone)
    {
        return new Border()
            .Child(content)
            .StretchHorizontal()
            .StretchVertical()
            .WithTheme((_, border) => border.Background(theme.Get(zone).Background));
    }

    private UIElement BuildActivityBar()
    {
        var theme = _workbench.ThemeContext;
        var items = _workbench.ActivityBarModel.Items;

        if (items.Count == 0)
        {
            return new Border()
                .WithTheme((_, border) => border.Background(theme.ActivityBar.Background))
                .Child(new StackPanel().Width(48));
        }

        // VSCode 约定:最后一项(设置/管理类)钉在活动栏底部,其余图标从顶部排列。
        var lastButton = BuildItemButton(items[^1], theme);
        if (items.Count == 1)
        {
            return new Border()
                .WithTheme((_, border) => border.Background(theme.ActivityBar.Background))
                .Child(
                    new StackPanel()
                        .Width(48)
                        .Padding(6, 8)
                        .Spacing(4)
                        .Children([lastButton])
                );
        }

        var mainButtons = items.Take(items.Count - 1).Select(item => BuildItemButton(item, theme)).ToArray();
        return new Border()
            .WithTheme((_, border) => border.Background(theme.ActivityBar.Background))
            .Child(
                new Grid()
                    .Rows("*,Auto")
                    .Children(
                        new StackPanel()
                            .Width(48)
                            .Padding(6, 8)
                            .Spacing(4)
                            .Children(mainButtons)
                            .Row(0),
                        new StackPanel()
                            .Width(48)
                            .Padding(6, 8)
                            .Children(lastButton)
                            .Row(1)
                    )
            );
    }

    private Button BuildItemButton(ActivityBarItem item, WorkbenchThemeContext theme)
    {
        var button = new Button()
            .Size(36, 36)
            .Padding(0) // 清零默认内边距,避免自定义图标(如 ⚙)被内容区裁切
            .BorderThickness(0) // 活动栏按钮不显示默认按钮边框
            .CornerRadius(0)
            .Content(item.CustomGlyph is { } custom
                ? custom
                : new GlyphElement()
                    .Kind(item.Glyph)
                    .GlyphSize(18)
                    .WithTheme((_, glyph) => glyph.Foreground(theme.ActivityBar.Foreground)))
            .ToolTip(item.Title);

        button.OnClick(() => _workbench.SelectActivity(item.Id));
        _activityButtons.Add(item.Id, button);
        // 背景随主题自动重涂:WithTheme 在主题切换时按当前选中态与新区背景重算
        // (选中态切换时由 ApplyActivitySelection 重涂,两者都读当前选中项,互不覆盖失效)。
        button.WithTheme((_, btn) => btn.Background(ActivityButtonBackground(item.Id)));

        return button;
    }

    /// <summary>
    /// 标签栏「最大化/恢复」按钮无实际效果(MewDock 最大化未完整接线),关闭模型级 TabSetEnableMaximize
    /// 使 TabSetNode.IsEnableMaximize / CanMaximize 为假,按钮不再渲染。DockingManager 不公开模型引用,
    /// 故按私有字段 _model 反射获取;与 DisableDockZoneBorders 同属 MewDock 内部适配。
    /// </summary>
    private static void DisableTabSetMaximize(DockingManager docking)
    {
        var model = typeof(DockingManager)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(docking);
        model?.GetType()
            .GetProperty("TabSetEnableMaximize")
            ?.SetValue(model, false);
    }

    /// <summary>
    /// 侧边栏/编辑器区、编辑器区/底部面板之间的拖动分隔条做到最细:把 MewDock 模型的
    /// SplitterSize 设为 3(分隔条仅 3px,平时透明不可见,悬停/拖动时显示细高亮)。
    /// 模型经 DockingManager._model 反射获取,首次布局后的 arrange 即按新值收窄;
    /// 布局持久化会保存新值,后续启动直接生效。与 DisableTabSetMaximize 同类适配。
    /// </summary>
    private static void ThinDockSplitters(DockingManager docking)
    {
        var model = typeof(DockingManager)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(docking);
        model?.GetType()
            .GetProperty("SplitterSize")
            ?.SetValue(model, 3.0);
    }

    /// <summary>
    /// 拖动分隔条时鼠标样式按方向变为缩放指针(仿 VS Code):垂直分隔条(侧边栏/编辑器)→
    /// 左右缩放(SizeWE),水平分隔条(编辑器/底部面板)→ 上下缩放(SizeNS)。设置 UIElement.Cursor
    /// 后悬浮该分隔条即自动生效;新分隔条在布局变更重扫时按 IsColumnAxis 重新接线。
    /// </summary>
    private static void WireSplitterCursors(DockingManager docking)
    {
        var assembly = typeof(DockingManager).Assembly;
        if (assembly.GetType("Aprillz.MewUI.MewDock.Controls.FlexSplitter") is not { } splitterType)
        {
            return;
        }

        var columnAxisProp = splitterType.GetProperty("IsColumnAxis");
        if (columnAxisProp is null || docking.Children.FirstOrDefault() is not UIElement root)
        {
            return;
        }

        VisitDockElements(root, element =>
        {
            if (splitterType.IsInstanceOfType(element) && element is Control splitter)
            {
                splitter.Cursor = columnAxisProp.GetValue(element) is true
                    ? CursorType.SizeNS
                    : CursorType.SizeWE;
            }
        });
    }

    /// <summary>
    /// 视图层隐藏所有 tabset 的「最大化/恢复」按钮。按钮在 FlexTabSetView 构造时按模型 flag 创建,
    /// 而模型在首次布局(AddDocumentPane 路径)时才就绪,flag 时序不可控;直接隐藏视图的
    /// _maximizeButton 字段在所有场景下都可靠。与 DisableDockZoneBorders 同属 MewDock 内部适配。
    /// </summary>
    private static void HideMaximizeButtons(DockingManager docking)
    {
        var assembly = typeof(DockingManager).Assembly;
        if (assembly.GetType("Aprillz.MewUI.MewDock.Controls.FlexTabSetView") is not { } viewType)
        {
            return;
        }

        var buttonField = viewType.GetField("_maximizeButton", BindingFlags.NonPublic | BindingFlags.Instance);
        if (buttonField is null || docking.Children.FirstOrDefault() is not Panel root)
        {
            return;
        }

        var stack = new Stack<Panel>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var panel = stack.Pop();
            foreach (var child in panel.Children)
            {
                if (viewType.IsInstanceOfType(child))
                {
                    if (buttonField.GetValue(child) is FrameworkElement { IsVisible: true } button)
                    {
                        button.IsVisible = false;
                    }
                }
                else if (child is Panel nested)
                {
                    stack.Push(nested);
                }
            }
        }
    }

    /// <summary>
    /// 编辑器 tab 的「×」关闭按钮仅鼠标悬浮时显示,且 tab 宽度不随悬浮/按钮显隐变化。
    /// 关闭按钮始终占据布局空间(IsVisible 保持 true),未悬浮时置透明并关闭命中测试;
    /// 悬浮 tab 时恢复显示。FlexTabButton 与 _closeButton 在 MewDock 中为 internal/private,
    /// 故经反射遍历停靠区视图树接线;运行时新建 tab 时 docking.Changed 会重新执行,
    /// 已接线实例用 _configuredTabClose 去重。仅文档 tab 有 _closeButton,工具 tab 自动跳过。
    /// </summary>
    private void ConfigureTabCloseHover(DockingManager docking)
    {
        var assembly = typeof(DockingManager).Assembly;
        if (assembly.GetType("Aprillz.MewUI.MewDock.Controls.FlexTabButton") is not { } tabType)
        {
            return;
        }

        var closeField = tabType.GetField("_closeButton", BindingFlags.NonPublic | BindingFlags.Instance);
        if (closeField is null || docking.Children.FirstOrDefault() is not UIElement root)
        {
            return;
        }

        VisitDockElements(root, element =>
        {
            if (tabType.IsInstanceOfType(element))
            {
                WireTabCloseHover(element, tabType, closeField);
            }
        });
    }

    /// <summary>深度优先遍历停靠区视图树(tabset 等 Control 仅实现 IVisualTreeHost,不属 Panel)。</summary>
    private static void VisitDockElements(Element element, Action<UIElement> visit)
    {
        if (element is UIElement uiElement)
        {
            visit(uiElement);
        }

        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                VisitDockElements(child, visit);
            }
        }
        else if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                VisitDockElements(child, visit);
                return true;
            });
        }
    }

    private void WireTabCloseHover(UIElement tab, Type tabType, FieldInfo closeField)
    {
        if (!_configuredTabClose.Add(tab))
        {
            return;
        }

        if (closeField.GetValue(tab) is not Button closeButton)
        {
            return;
        }

        // 关闭按钮(16px)始终占据布局空间,保证 tab 宽度不随悬浮/按钮显隐变化;
        // 未悬浮时隐藏「×」内容并关闭命中测试,悬浮 tab 时恢复。
        var glyph = closeButton.Content as UIElement;
        if (glyph is not null)
        {
            glyph.IsVisible = false;
        }
        closeButton.IsHitTestVisible = false;
        tab.MouseEnter += () =>
        {
            if (glyph is not null)
            {
                glyph.IsVisible = true;
            }
            closeButton.IsHitTestVisible = true;
        };
        tab.MouseLeave += () =>
        {
            if (glyph is not null)
            {
                glyph.IsVisible = false;
            }
            closeButton.IsHitTestVisible = false;
        };
    }

    /// <summary>设置状态栏项文本颜色(启动失败红色醒目用);null 恢复区前景色。</summary>
    internal void SetStatusTextColor(string id, Color? color)
    {
        if (_statusLabels.TryGetValue(id, out var label))
        {
            label.Foreground = color ?? _theme!.StatusBar.Foreground;
        }
    }

    private UIElement BuildStatusBar()
    {
        var theme = _workbench.ThemeContext;
        var items = _workbench.StatusBarModel.Items;
        var children = new Element[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var label = new Label()
                .BindText(item.Text)
                .FontSize(12)
                .WithTheme((_, label) => label.Foreground(theme.StatusBar.Foreground));
            _statusLabels[item.Id] = label;
            children[i] = label;
        }

        return new Border()
            .WithTheme((_, border) => border.Background(theme.StatusBar.Background))
            .Child(
                new StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Padding(10, 6)
                    .Spacing(16)
                    .Children(children)
            );
    }
}
