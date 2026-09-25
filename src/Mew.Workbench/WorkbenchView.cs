using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using System.Text.Json;

namespace Mew.Workbench;

internal sealed class WorkbenchView
{
    private readonly Workbench _workbench;
    private DockingManager? _docking;
    private MewDockShell? _shell;
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

    internal WorkbenchView(Workbench workbench) => _workbench = workbench;

    internal DockingManager Docking => _docking ?? throw new InvalidOperationException("Build() 尚未调用");

    internal UIElement Build()
    {
        var docking = new DockingManager();
        _docking = docking;
        _shell = new MewDockShell(docking);
        var theme = _workbench.ThemeContext;
        _theme = theme;
        var layoutStore = new WorkbenchLayoutStore();

        docking.WithContentFactory(pane => ResolvePaneContent(pane, theme));

        if (layoutStore.TryLoad() is { } savedLayout)
        {
            try
            {
                docking.LoadLayout(savedLayout);
                // 收敛历史残留：关闭已无对应视图的窗格（如单宿主窗格时代的 panel-host）；
                // 缺失的当前视图由后续 ApplyChromeVisibility 按显隐状态补回。
                CloseUnknownPanes(docking);
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

        // 模型级调参须先于首次布局(最大化按钮按构造期 flag 创建、分隔条尺寸按 arrange 收窄),故此处先 Tune 一次
        _shell.Tune();

        if (layoutStore.TryLoadPresentation() is { } presentation)
        {
            _workbench.RestorePresentation(presentation);
        }

        docking.Changed += (_, _) =>
        {
            // 布局变更可能新建 tabset 视图:重申全部 MewDock 行为调参(幂等,见 MewDockShell.Tune)
            _shell.Tune();
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
                // 0.20 菜单模型:命令注册进事件自带的 CommandScope,菜单项引用命令
                var reveal = MewCommands.Register(args.Commands, "mew.revealDocument", "在侧边栏定位",
                    () => _workbench.RevealDocument(args.Pane.Component!));
                args.Menu.AddItem(reveal);
            }
        };
        // 底部组标题栏已有 pin/× 独立按钮，分组菜单里去掉同义的自动隐藏/关闭，只留浮动等入口。
        docking.GroupMenuOpening += (_, args) =>
        {
            if (args.Group.Edge == DockEdge.Bottom)
            {
                MewDockShell.PruneGroupMenu(args.Menu);
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
        _shell.Tune(); // 初始 tabset 视图已就绪:边框覆盖样式注册(仅首次) + 最大化按钮隐藏 + tab 关闭悬浮 + 分隔条光标
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

    /// <summary>关闭恢复出来但已无对应视图的窗格，避免空白残留；Component 为空的不碰。</summary>
    private void CloseUnknownPanes(DockingManager docking)
    {
        var known = new HashSet<string>(
            _workbench.SideBarModel.Views.Select(view => view.Id)
                .Concat(_workbench.EditorAreaModel.Documents.Select(document => document.Id))
                .Concat(_workbench.PanelModel.Views.Select(view => view.Id)));
        foreach (var pane in docking.Panes.Where(pane => pane.Component is not null && !known.Contains(pane.Component)).ToList())
        {
            pane.Close();
        }
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
                    .WithTheme((_, glyph) => glyph.Foreground(theme.ActivityBar.Foreground)));
        // ToolTip 摘除(原 .ToolTip(item.Title)):上游 MewUI #253——tooltip 显示时首次点击
        // 只关闭 tooltip 不触发 Click,高频点击路径宁缺提示;上游修复后恢复。

        button.OnClick(() => _workbench.SelectActivity(item.Id));
        _activityButtons.Add(item.Id, button);
        // 背景随主题自动重涂:WithTheme 在主题切换时按当前选中态与新区背景重算
        // (选中态切换时由 ApplyActivitySelection 重涂,两者都读当前选中项,互不覆盖失效)。
        button.WithTheme((_, btn) => btn.Background(ActivityButtonBackground(item.Id)));

        return button;
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
