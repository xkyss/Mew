using System.ComponentModel;
using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Mew.Workbench;
using OverlayServiceContract = Mew.Workbench.IOverlayService;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Launcher;

/// <summary>
/// Launcher 工具模块(由 exe 应用本体转 Library):实现 <see cref="IMewToolModule"/>,经 ToolModuleContext
/// 贡献五区(启动上下文)、浮层搜索源与设置节;窗口/托盘/标题栏/主题/设置文档等宿主职责
/// 在票据 06 移交 Mew.Host(启动入口为 Mew.Host.exe,本模块为 Mew.Launcher.dll)。
/// </summary>
public sealed class LauncherModule : IMewToolModule
{
    public string Id => "launcher";

    public string DisplayName => "启动项";

    private const string RevealDetailShortcut = "Ctrl+Alt+R"; // 窗口内快捷键(非全局热键):在侧边栏定位当前启动项详情
    private const string DetailDocumentId = "detail";
    private const string ItemsDocumentId = "items";

    private readonly LauncherStore _store = new();
    private ISettingsService _settings = null!;
    private Window? _window;
    private IntPtr _windowHandle;
    private readonly LauncherRunner _runner = new();
    private readonly LaunchDebouncer _launchDebouncer = new(TimeSpan.FromMilliseconds(500));
    private readonly IconResolver _icons = new();
    private readonly ObservableValue<string> _launchStatus = new("就绪");
    private List<LauncherItem> _items = null!;
    private ItemHotkeys _itemHotkeys = null!;
    private WorkbenchType _workbench = null!;
    private WorkbenchThemeContext _theme = null!;
    private OverlayServiceContract _overlay = null!;
    private readonly StackPanel _listPanel = new();
    private readonly SelectionModel _listSelection = new();
    private readonly List<Button> _listButtons = [];
    private List<LauncherItem> _listItems = [];
    private int _styledSelection = -1;
    private int _hoveredIndex = -1;
    private ScrollViewer? _listScrollViewer;
    private WrapPanel? _cardPanel;
    private readonly StackPanel _detailPanel = new();
    private readonly StackPanel _logPanel = new();
    private string _navId = LauncherData.AllNavId; // 当前导航节点:「全部」/ 分类 id /「未分类」
    private LauncherItem? _current;
    private bool _loading;
    private TreeView? _tree;
    private TextBox? _itemsSearchBox;
    private TextBox? _categorySearchBox;
    private StackPanel? _categoryTreePanel;
    private string _viewMode = "card"; // 启动项列表形态:card(卡片,默认)/ list(列表),持久化于 settings.json
    private string _query = "";
    private LauncherData.ItemKind? _kindFilter; // 列表类型过滤:null = 全部
    private ComboBox? _kindCombo;
    private Button? _modeToggleButton;
    private readonly Dictionary<string, string> _sideBarFilters = [];
    private TreeItemsView<CategoryTreeNode>? _treeItems;
    private const string SettingsData = "data";
    private const string LauncherSectionId = "launcher"; // settings.json 中 Launcher 模块设置节

    public LauncherModule()
    {
    }

    /// <summary>
    /// 模块贡献入口:绑定宿主上下文(Workbench/设置/浮层/窗口),加载启动项数据与设置,
    /// 贡献五区(分类树、列表/卡片、详情表单、输出、状态栏)、浮层搜索源与设置节(数据);
    /// 宿主在全部模块贡献完后统一 Build()。窗口/托盘/标题栏/主题/设置文档/浮层热键等宿主职责归 Mew.Host。
    /// </summary>
    public void Configure(ToolModuleContext context)
    {
        _workbench = context.Workbench;
        _theme = context.Workbench.ThemeContext;
        _settings = context.Settings; // 仅按契约读写 launcher 节(ReadSection/WriteSection/Save);根节(themeMode/overlayHotkey)归宿主
        _overlay = context.Overlay;
        _window = context.Window;
        _windowHandle = context.WindowHandle;

        _items = _store.Load();
        _itemHotkeys = new ItemHotkeys(_items, LaunchItem, Feedback, context.Hotkeys);
        _viewMode = string.IsNullOrWhiteSpace(LauncherItemsViewMode) ? "card" : LauncherItemsViewMode!;

        _workbench
            .ActivityBar(bar =>
            {
                bar.Item("launch", "启动", ShellIcons.ActivityGlyph(_theme, RocketIconData));
            })
            .SideBar(side => side
                .View("launch", "启动", BuildCategoryTree()))
            .EditorArea(editor => editor
                .Document(ItemsDocumentId, "启动项", BuildItemsDocument())
                .Document(DetailDocumentId, "启动项详情", _detailPanel))
            .Panel(panel => panel.View("output", "输出", BuildOutputPanel()))
            .StatusBar(status => status
                .Item("launch", _launchStatus));

        ShowNav(_navId);
        ShowEmptyDetail();

        // 设置节:数据(数据文件路径);「热键」节票据 07 起归宿主中央热键服务
        context.SettingsSections.Add(SettingsData, "数据", BuildDataPanel);

        _overlay.AddSearchSource(new LauncherSearchSource(_items, _runner, _icons));

        // 宿主转发的事件:主窗口按键(键盘导航 + 详情定位快捷键)
        _workbench.WindowKeyDown += OnWindowKeyDown;

        // 主题模式变更(宿主标题栏/设置页切换)后重涂列表选中/悬停态
        context.Theme.ThemeModeChanged += ReapplyListSelection;

        // 每项热键经宿主中央热键服务注册(票据 07;浮层呼出键归宿主注册)
        _itemHotkeys.Attach(_windowHandle);
        _itemHotkeys.RegisterAll();
    }

    /// <summary>Launcher 模块设置节中的列表形态(settings.json "launcher" 节)。</summary>
    private string? LauncherItemsViewMode =>
        _settings.ReadSection<LauncherSettings>(LauncherSectionId, LauncherSettingsJsonContext.Default.LauncherSettings)?.ItemsViewMode;

    /// <summary>列表/卡片悬浮「编辑」按钮的单色铅笔图标。</summary>
    private const string EditIconData =
        "M3,17.25 L3,21 L6.75,21 L17.81,9.94 L14.06,6.19 L3,17.25 Z M20.71,7.04 C21.1,6.65 21.1,6.02 20.71,5.63 L18.37,3.29 C17.98,2.9 17.35,2.9 16.96,3.29 L15.13,5.12 L18.88,8.87 L20.71,7.04 Z";

    /// <summary>窗口内快捷键:定位当前启动项详情所属的侧边栏分类;启动项列表激活时提供键盘导航。</summary>
    private void OnWindowKeyDown(KeyEventArgs e)
    {
        if (e.ControlKey && e.AltKey && e.Key == Key.R)
        {
            if (_workbench.RevealDocument(DetailDocumentId))
            {
                Feedback($"已执行:在侧边栏定位 ({RevealDetailShortcut})");
            }

            return;
        }

        // 列表键盘导航:仅当启动项列表文档激活且焦点不在文本输入内时生效(搜索/表单输入不劫持)
        if (_workbench.ActiveDocumentId == ItemsDocumentId && !IsTextInputFocused())
        {
            // 「/」聚焦搜索:MewUI Key 枚举无标点键,经平台虚拟键码 VK_OEM_2(0xBF)识别
            if (e.PlatformKey == 0xBF)
            {
                FocusItemsSearch();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Up:
                    MoveListSelection(IsCardMode ? -CardColumns : -1); // 卡片:按行上下跳
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveListSelection(IsCardMode ? CardColumns : 1);
                    e.Handled = true;
                    break;
                case Key.Left when IsCardMode:
                    MoveListSelection(-1); // 卡片:左右横向移动
                    e.Handled = true;
                    break;
                case Key.Right when IsCardMode:
                    MoveListSelection(+1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    LaunchSelected();
                    e.Handled = true;
                    break;
                case Key.F when e.ControlKey:
                    FocusItemsSearch();
                    e.Handled = true;
                    break;
            }
        }
    }

    /// <summary>设置分类内容:数据(启动项数据文件路径与打开所在文件夹)。</summary>
    private UIElement BuildDataPanel()
    {
        var theme = _theme;
        var dataFilePath = _store.FilePath;

        return new StackPanel()
            .Padding(24)
            .Spacing(12)
            .Children(
                new Label().Text("数据").FontSize(20).Bold()
                    .WithTheme((_, label) => label.Foreground(theme.EditorArea.Foreground)),
                new Label().Text("数据文件").FontSize(14)
                    .WithTheme((_, label) => label.Foreground(theme.EditorArea.Foreground)),
                new StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Spacing(8)
                    .Children(
                        new Label()
                            .Text(dataFilePath)
                            .FontSize(12)
                            .MaxWidth(460)
                            .TextWrapping(TextWrapping.Wrap)
                            .WithTheme((_, label) => label.Foreground(theme.EditorArea.Foreground)),
                        new Button()
                            .Content(new Label().Text("打开所在文件夹"))
                            .ToolTip("在资源管理器中定位 launcher.json")
                            .OnClick(() => OpenDataFolder(dataFilePath))
                            .CanDrag(false)
                    )
            );
    }

    /// <summary>在资源管理器中打开数据文件所在文件夹并选中该文件。</summary>
    private static void OpenDataFolder(string filePath)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
        }
    }

    /// <summary>侧边栏「启动」上下文:分类搜索 + 分类导航树(「全部」置顶、分类树、「未分类」收尾)。</summary>
    private UIElement BuildCategoryTree()
    {
        var searchBox = new TextBox
        {
            Placeholder = "搜索分类",
            Text = SideBarFilter("launch"),
            CanDrag = false,
            BorderThickness = 0, // 平时不显示边框;聚焦时恢复边框作输入反馈
        };
        searchBox.OnGotFocus(() => searchBox.BorderThickness = 1);
        searchBox.OnLostFocus(() => searchBox.BorderThickness = 0);
        searchBox.TextChanged += text =>
        {
            _sideBarFilters["launch"] = text;
            RefreshCategoryTree();
        };
        _categorySearchBox = searchBox;

        var tree = new TreeView
        {
            SelectionMode = ItemsSelectionMode.Single,
            ExpandTrigger = TreeViewExpandTrigger.ClickChevron,
            CanDrag = false,
            BorderThickness = 0,
            CornerRadius = 0,
        };
        _tree = tree;
        tree.SelectionChanged += OnNavSelectionChanged;
        tree.MouseUp += OnCategoryTreeRightClick;
        // 分类树不显示默认边框:边框清零,背景跟随侧边栏区背景(去掉按钮式面板观感,与区背景融为一体)
        tree.WithTheme((_, view) =>
        {
            view.BorderBrush = Color.Transparent;
            view.Background = _theme.SideBar.Background;
        });

        var content = new StackPanel().Spacing(6);
        _categoryTreePanel = content;
        RefreshCategoryTree(); // 构建树项并默认选中「全部」;空态时面板内为引导视图

        return new StackPanel()
            .Padding(12)
            .Spacing(6)
            .Children(
                searchBox,
                content
            );
    }

    /// <summary>(重新)构建分类树项:按分类搜索词过滤,并默认选中「全部」;过滤为空时显示「无匹配分类」引导。</summary>
    private void RefreshCategoryTree()
    {
        var filtered = LauncherData.FilterNavTree(
            LauncherData.BuildNavTree(_store.Categories.ToList()), SideBarFilter("launch"));
        _treeItems = new TreeItemsView<CategoryTreeNode>(
            filtered,
            node => node.Children,
            node => node.Name,
            node => node.Id,
            node => node.Children.Count > 0);
        _tree!.ItemsSource = _treeItems;

        _categoryTreePanel!.Clear();
        if (filtered.Count == 0)
        {
            _categoryTreePanel.Add(EmptyStateView("无匹配分类", "清空搜索", ClearCategorySearch,
                _theme.SideBar.Foreground, _theme.SideBar.Background));
            return;
        }

        _categoryTreePanel.Add(_tree);
        _treeItems.SelectSingle(0);
    }

    private string SideBarFilter(string activityId) => _sideBarFilters.GetValueOrDefault(activityId, "");

    /// <summary>分类树右键:固定节点(「全部」「未分类」)无操作,用户分类弹出 新建子分类/重命名/删除。</summary>
    private void OnCategoryTreeRightClick(MouseEventArgs e)
    {
        if (!e.RightButton || _tree is not { } tree || _treeItems is not { } items)
        {
            return;
        }

        if (!tree.TryGetItemIndexAt(e, out var index))
        {
            return;
        }

        if (items.GetItem(index) is not CategoryTreeNode node || node.IsFixed)
        {
            return;
        }

        new ContextMenu(new Menu()
            .Item("新建子分类", () => AddSubCategory(node.Id))
            .Separator()
            .Item("重命名", () => RenameCategory(node.Id))
            .Item("删除", () => DeleteCategory(node.Id)))
            .ShowAt(tree, e.ScreenPosition);
    }

    /// <summary>删除分类:确认(提示将失去该分类的启动项数量)→ 摘除归属(永不删项,子分类整棵子树一并摘除)→ 刷新树与列表。</summary>
    private void DeleteCategory(string categoryId)
    {
        var categories = _store.Categories.ToList();
        var removed = LauncherData.AggregateSubtree(categories, _items, categoryId);
        var name = LauncherData.CategoryName(categories, categoryId) ?? categoryId;

        var confirmed = MessageBox.Confirm(
            $"删除分类「{name}」将连同其子分类一起删除,{removed.Count} 个启动项将失去此分类。",
            PromptIconKind.Warning,
            "",
            _window!);
        if (!confirmed)
        {
            return;
        }

        _items = LauncherData.DetachCategoryIds(_items, LauncherData.SubtreeIds(categories, categoryId));
        _store.UpdateCategories(LauncherData.RemoveCategoryNode(categories, categoryId));
        _store.Save(_items);
        _itemHotkeys.RegisterAll();

        // 当前导航在被删分类(或其子孙)内 → 回退「全部」
        if (_navId is not LauncherData.AllNavId and not LauncherData.UncategorizedNavId
            && LauncherData.Find(_store.Categories.ToList(), _navId) is null)
        {
            _navId = LauncherData.AllNavId;
        }

        RefreshCategoryTree();
        ShowNav(_navId);
        ShowEmptyDetail(); // 若详情正显示被删分类的启动项,清空
    }

    /// <summary>在指定分类下新建子分类(轻量输入对话框)。</summary>
    private void AddSubCategory(string parentId)
    {
        PromptText("新建子分类", "", name =>
        {
            if (name.Trim().Length == 0)
            {
                return;
            }

            var categories = _store.Categories.ToList();
            var newNode = new LauncherCategory("cat-" + Guid.NewGuid().ToString("N")[..4], name.Trim(), []);
            _store.UpdateCategories(LauncherData.AddCategoryNode(categories, parentId, newNode));
            _store.Save(_items);
            RefreshCategoryTree();
        });
    }

    /// <summary>重命名分类(轻量输入对话框)。</summary>
    private void RenameCategory(string categoryId)
    {
        var node = LauncherData.Find(_store.Categories.ToList(), categoryId);
        if (node is null)
        {
            return;
        }

        PromptText("重命名分类", node.Name, name =>
        {
            if (name.Trim().Length == 0)
            {
                return;
            }

            var categories = _store.Categories.ToList();
            _store.UpdateCategories(LauncherData.RenameCategoryNode(categories, categoryId, name.Trim()));
            _store.Save(_items);
            RefreshCategoryTree();
        });
    }

    /// <summary>轻量模态文本输入对话框(新建/重命名分类用)。</summary>
    private void PromptText(string title, string initial, Action<string> onConfirm)
    {
        var input = new TextBox
        {
            Text = initial,
            CanDrag = false,
        };

        var dialog = new Window()
            .Title(title)
            .Fixed(380, 170);
        var confirm = new Button()
            .Content(new Label().Text("确定"))
            .OnClick(() =>
            {
                dialog.Close();
                onConfirm(input.Text);
            })
            .CanDrag(false);
        var cancel = new Button()
            .Content(new Label().Text("取消"))
            .OnClick(() => dialog.Close())
            .CanDrag(false);

        dialog.Content = new StackPanel()
            .Padding(16)
            .Spacing(10)
            .Children(
                new Label().Text(title).FontSize(14),
                input,
                new StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Spacing(8)
                    .Children(confirm, cancel)
            );
        dialog.ShowDialog(_window!);
    }

    private void OnNavSelectionChanged(object? item)
    {
        if (item is CategoryTreeNode node)
        {
            ShowNav(node.Id);
            _workbench.OpenDocument("items");
        }
    }

    /// <summary>按导航节点显示编辑器区「启动项列表」:「全部」→ 所有项;「未分类」→ 无分类项;分类 id → 子树聚合。</summary>
    private void ShowNav(string navId)
    {
        _navId = navId;
        _listPanel.Clear();
        _listItems = [];
        _listButtons.Clear();
        _styledSelection = -1;
        _hoveredIndex = -1;

        var aggregated = LauncherData.AggregateForNav(_store.Categories.ToList(), _items, navId);
        var shown = aggregated
            .Where(item => LauncherSearch.Matches(item, _query))
            .Where(item => _kindFilter is null || LauncherData.KindOf(item.Command) == _kindFilter)
            .ToList();

        if (shown.Count == 0)
        {
            _listSelection.Clamp(0);
            var hasQuery = !string.IsNullOrWhiteSpace(_query);
            var hasFilter = _kindFilter is not null;
            // 列表已空:聚合为空且无过滤 → 无数据(引导新增);聚合有内容但被过滤排除 → 无匹配
            var kind = EmptyState.ForList(
                hasItems: false,
                hasQuery: hasQuery,
                hasFilter: hasFilter && aggregated.Count > 0);
            _listPanel.Add(kind == EmptyStateKind.NoMatch
                ? hasFilter
                    ? EmptyStateView("无匹配启动项", "全部类型", ClearKindFilter,
                        _theme.EditorArea.Foreground, _theme.EditorArea.Background)
                    : EmptyStateView("无匹配启动项", "清空搜索", ClearItemsSearch,
                        _theme.EditorArea.Foreground, _theme.EditorArea.Background)
                : EmptyStateView("暂无启动项", "＋ 新增启动项", CreateItem,
                    _theme.EditorArea.Foreground, _theme.EditorArea.Background));
            return;
        }

        if (_viewMode == "list")
        {
            for (var i = 0; i < shown.Count; i++)
            {
                var (container, main) = ListRow(shown[i], i);
                _listPanel.Add(container);
                _listItems.Add(shown[i]);
                _listButtons.Add(main);
            }
        }
        else
        {
            _cardPanel = new WrapPanel { ItemWidth = CardItemWidth, ItemHeight = CardItemHeight, Spacing = CardSpacing };
            var wrap = _cardPanel;
            for (var i = 0; i < shown.Count; i++)
            {
                var (container, main) = Card(shown[i], i);
                wrap.Add(container);
                _listItems.Add(shown[i]);
                _listButtons.Add(main);
            }

            _listPanel.Add(wrap);
        }

        _listSelection.Clamp(shown.Count);
        ApplyListSelection();
    }

    /// <summary>编辑器区列表行:三列布局(图标 / 名称+命令行 / 简介),单击启动,悬停浮现「编辑」图标按钮,右键菜单(编辑/删除)。</summary>
    private (UIElement Container, Button Main) ListRow(LauncherItem item, int index)
    {
        var icon = _icons.Resolve(item);
        var main = new Button()
            .Content(new Grid()
                .Columns("Auto,*,*")
                .Spacing(8)
                .Children(
                    ShellIcons.IconElement(icon, 32).VerticalAlignment(VerticalAlignment.Center),
                    new StackPanel()
                        .Spacing(2)
                        .VerticalAlignment(VerticalAlignment.Center)
                        .Column(1)
                        .Children(
                            new StackPanel()
                                .Orientation(Orientation.Horizontal)
                                .Spacing(6)
                                .Children(
                                    new Label().Text(item.Name)
                                        .Bold()
                                        .WithTheme((_, label) => label.Foreground(_theme.EditorArea.Foreground)),
                                    ItemKindTag(item)), // 名称右侧:小号、与名称同色
                            new Label().Text(item.Command).FontSize(11)
                                .WithTheme((_, label) => label.Foreground(_theme.EditorArea.Foreground))
                        ),
                    new Label()
                        .Text(item.Description ?? "")
                        .FontSize(11)
                        .TextAlignment(TextAlignment.Left)
                        .TextWrapping(TextWrapping.Wrap)
                        .VerticalAlignment(VerticalAlignment.Center)
                        .WithTheme((_, label) => label.Foreground(_theme.EditorArea.Foreground))
                        .Column(2)
                ))
            .CanDrag(false)
            .WithTheme((_, button) => button.Background(_theme.EditorArea.Background));
        main.OnClick(() => TryLaunchFromList(item)); // 单击 → 启动(双击经防重只启动一次)
        AttachContextMenu(main, item);

        return BuildItemShell(item, main, index, editAtCorner: false);
    }

    /// <summary>启动类型标记:#URL / #程序,由命令推导(命令是唯一事实来源);卡片置于名称下方、列表置于名称右侧,均为 10px 普通色。</summary>
    private UIElement ItemKindTag(LauncherItem item) =>
        new Label()
            .Text("#" + ItemKindLabel(LauncherData.KindOf(item.Command)))
            .FontSize(10)
            .VerticalAlignment(VerticalAlignment.Center)
            .WithTheme((_, label) => label.Foreground(_theme.EditorArea.Foreground));

    /// <summary>启动类型展示文案:类型过滤下拉与标记共用的唯一映射。</summary>
    private static string ItemKindLabel(LauncherData.ItemKind kind) =>
        kind == LauncherData.ItemKind.Url ? "URL" : "程序";

    /// <summary>卡片:图标(左侧)+ 名称与类型标记(名称下方小号标签)+ 底部第三行简介;单击启动,悬停浮现「编辑」图标按钮。</summary>
    private (UIElement Container, Button Main) Card(LauncherItem item, int index)
    {
        var icon = _icons.Resolve(item);
        var content = new List<UIElement>
        {
            new Grid()
                .Columns("Auto,*")
                .Spacing(8)
                .Children(
                    ShellIcons.IconElement(icon, 48),
                    new StackPanel()
                        .Spacing(2)
                        .VerticalAlignment(VerticalAlignment.Center)
                        .Column(1)
                        .Children(
                            new Label().Text(item.Name)
                                .Bold()
                                .FontSize(14)
                                .TextWrapping(TextWrapping.Wrap)
                                .WithTheme((_, label) => label.Foreground(_theme.EditorArea.Foreground)),
                            ItemKindTag(item)
                        )
                )
        };

        // 第三行:简介(无简介时「暂无简介」占位,避免卡片空洞)
        content.Add(new Label()
            .Text(string.IsNullOrWhiteSpace(item.Description) ? "暂无简介" : item.Description)
            .FontSize(11)
            .TextWrapping(TextWrapping.Wrap)
            .WithTheme((_, label) => label.Foreground(_theme.EditorArea.Foreground)));

        var main = new Button()
            .Content(new StackPanel()
                .Orientation(Orientation.Vertical)
                .Spacing(6)
                .VerticalAlignment(VerticalAlignment.Center) // 内容块在卡内垂直居中,上下留白均衡(配合 CardItemHeight=100)
                .Children(content.ToArray()))
            .CanDrag(false)
            .WithTheme((_, button) => button.Background(_theme.EditorArea.Background));
        main.OnClick(() => TryLaunchFromList(item)); // 单击 → 启动(双击经防重只启动一次)
        AttachContextMenu(main, item);
        return BuildItemShell(item, main, index, editAtCorner: true);
    }

    /// <summary>
    /// 组装列表项容器:主按钮(单击启动)+ 悬停浮现的「编辑」图标按钮,两者兄弟叠加
    /// (不嵌套按钮,避免点击冲突);主按钮与编辑按钮共用悬停计数,悬停态在两者间移动不丢失。
    /// 编辑按钮带 4px 内缩,悬浮时不压卡片/行边框。
    /// </summary>
    private (UIElement Container, Button Main) BuildItemShell(LauncherItem item, Button main, int index, bool editAtCorner)
    {
        var edit = new Button()
            .Size(28, 28)
            .Padding(0)
            .BorderThickness(0)
            .CornerRadius(0)
            .Margin(new Thickness(4)) // 内缩 4px:避开卡片默认 ControlBorder 边框与圆角,悬浮时不遮挡右上角
            .Content(EditorGlyph(EditIconData, 16))
            .ToolTip("编辑")
            .OnClick(() => EditItem(item))
            .CanDrag(false)
            .WithTheme((_, button) => button.Background(_theme.EditorArea.Background));
        edit.IsVisible = false; // 悬停时浮现
        edit.HorizontalAlignment = HorizontalAlignment.Right;
        edit.VerticalAlignment = editAtCorner ? VerticalAlignment.Top : VerticalAlignment.Center;

        var hover = new HoverRefCount();
        hover.RaisedChanged += () =>
        {
            edit.IsVisible = hover.IsRaised;
            SetItemHovered(index, hover.IsRaised);
        };
        main.MouseEnter += () => hover.Enter();
        main.MouseLeave += () => hover.Leave();
        edit.MouseEnter += () => hover.Enter();
        edit.MouseLeave += () => hover.Leave();

        return (new Grid().Children(main, edit), main);
    }

    /// <summary>挂右键菜单(编辑 / 删除),右键时在鼠标位置弹出。</summary>
    private void AttachContextMenu(UIElement element, LauncherItem item)
    {
        var menu = new ContextMenu(new Menu()
            .Item("编辑", () => EditItem(item))
            .Separator()
            .Item("删除", () => DeleteItem(item)));
        element.MouseUp += e =>
        {
            if (e.RightButton)
            {
                menu.ShowAt(element, e.ScreenPosition);
            }
        };
    }

    /// <summary>卡片网格列数:按卡片面板实际宽度与卡片尺寸推算,至少 1 列。</summary>
    private int CardColumns
    {
        get
        {
            if (_cardPanel is not { } panel || panel.ActualWidth <= 0)
            {
                return 1;
            }

            var slot = CardItemWidth + CardSpacing;
            return Math.Max(1, (int)((panel.ActualWidth + CardSpacing) / slot));
        }
    }

    /// <summary>当前是否为卡片形态(左右键仅卡片可用,列表为单列)。</summary>
    private bool IsCardMode => _viewMode == "card";

    /// <summary>卡片布局常量:卡宽/卡高/间距,ShowNav 与列数推算共用。</summary>
    private const double CardItemWidth = 170;
    private const double CardItemHeight = 100; // 内容约 69px,垂直居中后上下留白均衡(原 120 底部空 ~43px)
    private const double CardSpacing = 8;

    /// <summary>切换卡片/列表形态并持久化,立即重绘列表。</summary>
    private void ToggleViewMode()
    {
        _viewMode = _viewMode == "card" ? "list" : "card";
        _settings.WriteSection(LauncherSectionId, new LauncherSettings { ItemsViewMode = _viewMode }, LauncherSettingsJsonContext.Default.LauncherSettings);
        _settings.Save();
        _modeToggleButton!.Content(ViewModeGlyph());
        ShowNav(_navId);
    }

    /// <summary>编辑器区「启动项列表」文档:顶部工具栏(搜索 / 新增 / 形态切换)+ 聚合列表(可滚动)。</summary>
    private UIElement BuildItemsDocument()
    {
        var searchBox = new TextBox
        {
            Placeholder = "搜索启动项",
            CanDrag = false,
        };
        searchBox.MinWidth(320); // MewUI 布局限制下搜索框不随窗口自动拉伸,给合理最小宽度
        _itemsSearchBox = searchBox;
        searchBox.TextChanged += text =>
        {
            _query = text;
            ShowNav(_navId);
        };

        // 类型过滤下拉:全部 / URL / 程序,由命令推导,切换即时生效
        var kindOptions = new List<KindFilterOption>
        {
            new(null, "全部"),
            new(LauncherData.ItemKind.Url, ItemKindLabel(LauncherData.ItemKind.Url)),
            new(LauncherData.ItemKind.Program, ItemKindLabel(LauncherData.ItemKind.Program)),
        };
        var kindCombo = new ComboBox
        {
            ItemsSource = new ItemsView<KindFilterOption>(kindOptions, o => o.Label, o => o.Label),
            SelectedIndex = 0,
            ChangeOnWheel = false,
            CanDrag = false,
        };
        _kindCombo = kindCombo;
        kindCombo.SelectionChanged += selected =>
        {
            if (selected is KindFilterOption option)
            {
                _kindFilter = option.Kind;
                ShowNav(_navId);
            }
        };

        var addButton = new Button()
            .Size(30, 30)
            .MinWidth(30)
            .MinHeight(30)
            .Padding(0)
            .Content(EditorGlyph(AddIconData, 18))
            .ToolTip("新增启动项(归入当前分类)")
            .OnClick(CreateItem)
            .CanDrag(false)
            .BorderThickness(0)
            .CornerRadius(0);

        _modeToggleButton = new Button()
            .Size(30, 30)
            .MinWidth(30)
            .MinHeight(30)
            .Padding(0)
            .Content(ViewModeGlyph())
            .ToolTip("切换卡片 / 列表")
            .OnClick(ToggleViewMode)
            .CanDrag(false)
            .BorderThickness(0)
            .CornerRadius(0);

        var scrollViewer = new ScrollViewer
        {
            Content = _listPanel,
            VerticalScroll = ScrollMode.Auto,
        };
        _listScrollViewer = scrollViewer;

        // 工具栏:搜索框在左,两个等大小图标按钮紧贴其右(MewUI 0.19.1 布局限制:DockPanel
        // 末子元素填充会盖住停靠子元素、星号列右侧元素会被丢弃,故按钮位于搜索框右侧)。
        // 根布局用 Grid(Auto,*):工具栏占 Auto 行、列表占星号行。垂直 StackPanel 会给子元素
        // 无限高度,ScrollViewer 会按内容全高上报 desired、被视口裁剪且不产生滚动条;
        // 星号行把剩余受限高度传给 ScrollViewer,内容溢出时才出现竖向滚动条。
        // 内边距分布:根 Grid 不加水平内边距,工具栏行自行加(12,12,12,0);ScrollViewer
        // 横贯到右缘,竖向滚动条因此贴编辑器区右边缘;列表内容左/下内边距由 ScrollViewer
        // 的 Padding 提供(滚动条位于命中区内,与内容内边距互不挤占)。
        return new Grid()
            .Rows("Auto,*")
            .Spacing(8)
            .Children(
                new Grid()
                    .Columns("Auto,*")
                    .Spacing(8)
                    .Padding(new Thickness(12, 12, 12, 0))
                    .Children(
                        searchBox.Column(0),
                        new StackPanel()
                            .Orientation(Orientation.Horizontal)
                            .Spacing(4)
                            .Children(kindCombo, addButton, _modeToggleButton)
                            .Column(1)
                    )
                    .Row(0),
                scrollViewer
                    .Padding(new Thickness(12, 0, 0, 12))
                    .Row(1)
            );
    }

    /// <summary>形态切换按钮图标:卡片视图显示 2×2 卡片格(grid),列表视图显示列表(list)图标。</summary>
    private UIElement ViewModeGlyph() => EditorGlyph(
        _viewMode == "card" ? GridIconData : ListIconData, 18);

    /// <summary>
    /// 图标按钮的 PathShape:填充绑定到继承前景色,随主题/悬停/禁用自动变色
    /// (仿 MewUI.Gallery 的 SegmentIconShape 做法,图标取自 Fluent 图标集)。
    /// </summary>
    private static PathShape IconShape(string pathData, double size)
    {
        var shape = new PathShape()
            .Stretch(Stretch.Uniform)
            .Width(size)
            .Height(size)
            .Center();
        shape.Data = PathGeometry.Parse(pathData);
        shape.Bind(Shape.FillProperty, shape, TextElement.ForegroundProperty,
            (Color color) => new SolidColorBrush(color));
        return shape;
    }

    /// <summary>编辑区内固定使用编辑器前景主题色的图标。</summary>
    private UIElement EditorGlyph(string pathData, double size)
    {
        var shape = new PathShape()
            .Stretch(Stretch.Uniform)
            .Width(size)
            .Height(size)
            .Center();
        shape.Data = PathGeometry.Parse(pathData);
        shape.WithTheme((_, s) => s.Fill = new SolidColorBrush(_theme.EditorArea.Foreground));
        return shape;
    }

    // Fluent 图标路径数据(取自 MewUI.Gallery 的 Resources/Icons.xaml):新增(add_regular)、
    // 卡片视图(grid_regular)、列表视图(apps_list_regular)、启动(rocket_regular)、设置(settings_regular)。
    private const string AddIconData =
        "M14.5,13 L14.5,3.75378577 C14.5,3.33978577 14.164,3.00378577 13.75,3.00378577 C13.336,3.00378577 13,3.33978577 13,3.75378577 L13,13 L3.75387573,13 C3.33987573,13 3.00387573,13.336 3.00387573,13.75 C3.00387573,14.164 3.33987573,14.5 3.75387573,14.5 L13,14.5 L13,23.7523651 C13,24.1663651 13.336,24.5023651 13.75,24.5023651 C14.164,24.5023651 14.5,24.1663651 14.5,23.7523651 L14.5,14.5 L23.7498262,14.5030754 C24.1638262,14.5030754 24.4998262,14.1670754 24.4998262,13.7530754 C24.4998262,13.3390754 24.1638262,13.0030754 23.7498262,13.0030754 L14.5,13 Z";

    private const string GridIconData =
        "M10.75,15 C11.9926407,15 13,16.0073593 13,17.25 L13,22.75 C13,23.9926407 11.9926407,25 10.75,25 L5.25,25 C4.00735931,25 3,23.9926407 3,22.75 L3,17.25 C3,16.0073593 4.00735931,15 5.25,15 L10.75,15 Z M22.75,15 C23.9926407,15 25,16.0073593 25,17.25 L25,22.75 C25,23.9926407 23.9926407,25 22.75,25 L17.25,25 C16.0073593,25 15,23.9926407 15,22.75 L15,17.25 C15,16.0073593 16.0073593,15 17.25,15 L22.75,15 Z M10.75,16.5 L5.25,16.5 C4.83578644,16.5 4.5,16.8357864 4.5,17.25 L4.5,22.75 C4.5,23.1642136 4.83578644,23.5 5.25,23.5 L10.75,23.5 C11.1642136,23.5 11.5,23.1642136 11.5,22.75 L11.5,17.25 C11.5,16.8357864 11.1642136,16.5 10.75,16.5 Z M22.75,16.5 L17.25,16.5 C16.8357864,16.5 16.5,16.8357864 16.5,17.25 L16.5,22.75 C16.5,23.1642136 16.8357864,23.5 17.25,23.5 L22.75,23.5 C23.1642136,23.5 23.5,23.1642136 23.5,22.75 L23.5,17.25 C23.5,16.8357864 23.1642136,16.5 22.75,16.5 Z M10.75,3 C11.9926407,3 13,4.00735931 13,5.25 L13,10.75 C13,11.9926407 11.9926407,13 10.75,13 L5.25,13 C4.00735931,13 3,11.9926407 3,10.75 L3,5.25 C3,4.00735931 4.00735931,3 5.25,3 L10.75,3 Z M22.75,3 C23.9926407,3 25,4.00735931 25,5.25 L25,10.75 C25,11.9926407 23.9926407,13 22.75,13 L17.25,13 C16.0073593,13 15,11.9926407 15,10.75 L15,5.25 C15,4.00735931 16.0073593,3 17.25,3 L22.75,3 Z M10.75,4.5 L5.25,4.5 C4.83578644,4.5 4.5,4.83578644 4.5,5.25 L4.5,10.75 C4.5,11.1642136 4.83578644,11.5 5.25,11.5 L10.75,11.5 C11.1642136,11.5 11.5,11.1642136 11.5,10.75 L11.5,5.25 C11.5,4.83578644 11.1642136,4.5 10.75,4.5 Z M22.75,4.5 L17.25,4.5 C16.8357864,4.5 16.5,4.83578644 16.5,5.25 L16.5,10.75 C16.5,11.1642136 16.8357864,11.5 17.25,11.5 L22.75,11.5 C23.1642136,11.5 23.5,11.1642136 23.5,10.75 L23.5,5.25 C23.5,4.83578644 23.1642136,4.5 22.75,4.5 Z";

    private const string ListIconData =
        "M6.24787561,16.0021244 C7.21437393,16.0021244 7.99787561,16.7856261 7.99787561,17.7521244 L7.99787561,20.25 C7.99787561,21.2164983 7.21437393,22 6.24787561,22 L3.75,22 C2.78350169,22 2,21.2164983 2,20.25 L2,17.7521244 C2,16.7856261 2.78350169,16.0021244 3.75,16.0021244 L6.24787561,16.0021244 Z M6.24787561,17.5021244 L3.75,17.5021244 C3.61192881,17.5021244 3.5,17.6140532 3.5,17.7521244 L3.5,20.25 C3.5,20.3880712 3.61192881,20.5 3.75,20.5 L6.24787561,20.5 C6.3859468,20.5 6.49787561,20.3880712 6.49787561,20.25 L6.49787561,17.7521244 C6.49787561,17.6140532 6.3859468,17.5021244 6.24787561,17.5021244 Z M9.74809326,18 L21.2528964,18 C21.66711,18 22.0028964,18.3357864 22.0028964,18.75 C22.0028964,19.1296958 21.7207425,19.443491 21.354667,19.4931534 L21.2528964,19.5 L9.74809326,19.5 C9.3338797,19.5 8.99809326,19.1642136 8.99809326,18.75 C8.99809326,18.3703042 9.28024715,18.056509 9.64632271,18.0068466 L9.74809326,18 L21.2528964,18 L9.74809326,18 Z M6.24787561,9.00106219 C7.21437393,9.00106219 7.99787561,9.78456388 7.99787561,10.7510622 L7.99787561,13.2489378 C7.99787561,14.2154361 7.21437393,14.9989378 6.24787561,14.9989378 L3.75,14.9989378 C2.78350169,14.9989378 2,14.2154361 2,13.2489378 L2,10.7510622 C2,9.78456388 2.78350169,9.00106219 3.75,9.00106219 L6.24787561,9.00106219 Z M6.24787561,10.5010622 L3.75,10.5010622 C3.61192881,10.5010622 3.5,10.612991 3.5,10.7510622 L3.5,13.2489378 C3.5,13.387009 3.61192881,13.4989378 3.75,13.4989378 L6.24787561,13.4989378 C6.3859468,13.4989378 6.49787561,13.387009 6.49787561,13.2489378 L6.49787561,10.7510622 C6.49787561,10.612991 6.3859468,10.5010622 6.24787561,10.5010622 Z M9.74809326,11 L21.2528964,11 C21.66711,11 22.0028964,11.3357864 22.0028964,11.75 C22.0028964,12.1296958 21.7207425,12.443491 21.354667,12.4931534 L21.2528964,12.5 L9.74809326,12.5 C9.3338797,12.5 8.99809326,12.1642136 8.99809326,11.75 C8.99809326,11.3703042 9.28024715,11.056509 9.64632271,11.0068466 L9.74809326,11 L21.2528964,11 L9.74809326,11 Z M6.24787561,2 C7.21437393,2 7.99787561,2.78350169 7.99787561,3.75 L7.99787561,6.24787561 C7.99787561,7.21437393 7.21437393,7.99787561 6.24787561,7.99787561 L3.75,7.99787561 C2.78350169,7.99787561 2,7.21437393 2,6.24787561 L2,3.75 C2,2.78350169 2.78350169,2 3.75,2 L6.24787561,2 Z M6.24787561,3.5 L3.75,3.5 C3.61192881,3.5 3.5,3.61192881 3.5,3.75 L3.5,6.24787561 C3.5,6.3859468 3.61192881,6.49787561 3.75,6.49787561 L6.24787561,6.49787561 C6.3859468,6.49787561 6.49787561,6.3859468 6.49787561,6.24787561 L6.49787561,3.75 C6.49787561,3.61192881 6.3859468,3.5 6.24787561,3.5 Z M9.74809326,4 L21.2528964,4 C21.66711,4 22.0028964,4.33578644 22.0028964,4.75 C22.0028964,5.12969577 21.7207425,5.44349096 21.354667,5.49315338 L21.2528964,5.5 L9.74809326,5.5 C9.3338797,5.5 8.99809326,5.16421356 8.99809326,4.75 C8.99809326,4.37030423 9.28024715,4.05650904 9.64632271,4.00684662 L9.74809326,4 L21.2528964,4 L9.74809326,4 Z";

    private const string RocketIconData =
        "M8.63237,19.2805 C8.89863364,19.5467727 8.92283116,19.9634587 8.70497759,20.2570793 L8.63236,20.3412 L7.57375,21.3997 C7.28086,21.6926 6.80598,21.6926 6.51309,21.3997 C6.24682636,21.1334273 6.22262884,20.7167413 6.44048992,20.4231959 L6.51311,20.3391 L7.57171,19.2805 C7.86461,18.9876 8.33948,18.9876 8.63237,19.2805 Z M6.68984,17.3339 C6.95611273,17.6001727 6.98031934,18.016776 6.76245983,18.3103816 L6.68984,18.3945 L4.21497,20.8694 C3.92208,21.1623 3.4472,21.1623 3.15431,20.8694 C2.88804636,20.6031273 2.86384058,20.1864413 3.08169264,19.8928207 L3.15431,19.8087 L5.62918,17.3339 C5.92208,17.041 6.39695,17.041 6.68984,17.3339 Z M18.7782803,2.2324576 L19.0355,2.30675 L19.6976,2.51222 C20.5007214,2.76146714 21.1420857,3.3630448 21.444828,4.14012008 L21.5086,4.32248 L21.715,4.98679 C22.432525,7.29543464 21.8591435,9.80415445 20.2277124,11.5699999 L20.0421,11.7631 L19.0442,12.761 C20.030408,14.075912 19.9692176,15.9274717 18.8605403,17.1756967 L18.7165,17.3285 L17.4739,18.5711 C17.2077182,18.8373727 16.7910405,18.8615793 16.4974207,18.6437198 L16.4133,18.5711 L14.8237,16.9815 L14.6469,17.1583 C14.0037,17.8015 12.9842952,17.8393353 12.2968358,17.2718059 L12.172,17.1583 L11.6749,16.6611 L10.8769,18.056 C10.7609,18.2588 10.5569,18.396 10.3253,18.427 C10.1323,18.45275 9.938425,18.4023889 9.78353611,18.2892269 L9.6956,18.2139 L5.80649,14.3248 C5.64112,14.1594 5.56236,13.9264 5.59349,13.6946 C5.62018143,13.496 5.72499286,13.317751 5.88231915,13.1978892 L5.96524,13.143 L7.3608,12.347 L6.86546,11.8517 C6.22224118,11.2085 6.18440478,10.1890952 6.7519508,9.50165244 L6.86546,9.37682 L7.04527,9.19701 L5.45429,7.60604 C5.18802636,7.33977636 5.16382058,6.92310777 5.38167264,6.62949778 L5.45429,6.54538 L6.69682,5.30285 C7.8932872,4.1063828 9.74107677,3.95821717 11.0988173,4.8583531 L11.2659,4.97633 L12.2618,3.98045 C13.9706107,2.27162964 16.4567735,1.61138838 18.7782803,2.2324576 Z M4.74526,15.3893 C5.03816,15.6822 5.03816,16.1571 4.74526,16.45 L3.6846,17.5106 C3.39171,17.8035 2.91684,17.8035 2.62394,17.5106 C2.33105,17.2177 2.33105,16.7429 2.62394,16.45 L3.6846,15.3893 C3.9775,15.0964 4.45237,15.0964 4.74526,15.3893 Z M17.9641,13.8411 L15.8843,15.9208 L16.9436,16.9801 L17.6558,16.2679 C18.3138,15.6099 18.4166,14.6069 17.9641,13.8411 Z M8.46045,13.4467 L7.56207,13.9591 L10.0622,16.4593 L10.5756,15.5618 L8.46045,13.4467 Z M13.4981149,4.87341403 L13.3225,5.04112 L7.92612,10.4375 C7.84476167,10.5188333 7.831195,10.6423194 7.88543736,10.7377037 L7.92612,10.791 L13.2327,16.0976 C13.3140333,16.1789333 13.4375194,16.1924889 13.5329037,16.1382667 L13.5862,16.0976 L18.9814,10.7024 C20.3028458,9.38101167 20.8180259,7.46208293 20.3493287,5.66554744 L20.2826,5.43197 L20.0761,4.76766 C19.9675667,4.41824667 19.7122284,4.13626247 19.3807398,3.99222154 L19.253,3.94481 L18.5909,3.73934 C16.8068667,3.18566292 14.8695519,3.62291488 13.4981149,4.87341403 Z M16.5927,7.43109 C17.569,8.4074 17.569,9.99031 16.5927,10.9666 C15.6164,11.9429 14.0335,11.9429 13.0572,10.9666 C12.0808,9.99031 12.0808,8.4074 13.0572,7.43109 C14.0335,6.45478 15.6164,6.45478 16.5927,7.43109 Z M14.1178,8.49175 C13.7273,8.88227 13.7273,9.51544 14.1178,9.90596 C14.5083,10.2965 15.1415,10.2965 15.532,9.90596 C15.9226,9.51544 15.9226,8.88227 15.532,8.49175 C15.1415,8.10123 14.5083,8.10123 14.1178,8.49175 Z M7.88485721,6.24655617 L7.75748,6.36351 L7.04528,7.07571 L8.10593,8.13635 L10.186,6.05628 C9.46789375,5.63094563 8.54056387,5.69437102 7.88485721,6.24655617 Z";

    /// <summary>新建启动项:自动归入当前导航节点对应的分类(「全部」/「未分类」下新建归未分类)。</summary>
    private void CreateItem()
    {
        var item = new LauncherItem(
            "item-" + Guid.NewGuid().ToString("N")[..8],
            "新建启动项",
            "",
            CategoryIds: LauncherData.CategoryIdsForNewItem(_navId));

        _items.Add(item);
        _store.Save(_items);
        _itemHotkeys.RegisterAll();
        EditItem(item);
        ShowNav(_navId);
    }

    private UIElement BuildOutputPanel()
    {
        AppendLog("就绪");
        return _logPanel;
    }

    private void AppendLog(string message)
    {
        _logPanel.Add(new Label()
            .Text($"{DateTime.Now:HH:mm:ss}  {message}")
            .FontSize(12)
            .WithTheme((_, label) => label.Foreground(_theme.Panel.Foreground)));
    }

    /// <summary>列表单击启动入口:同一启动项在防重时间窗内(双击场景)只启动一次。</summary>
    private void TryLaunchFromList(LauncherItem item)
    {
        if (_launchDebouncer.ShouldLaunch(item.Id, DateTime.Now))
        {
            LaunchItem(item);
        }
    }

    /// <summary>列表键盘导航:移动选中并确保可见。</summary>
    private void MoveListSelection(int delta)
    {
        if (_listItems.Count == 0)
        {
            return;
        }

        if (delta < 0)
        {
            _listSelection.MoveUp(_listItems.Count);
        }
        else
        {
            _listSelection.MoveDown(_listItems.Count);
        }

        ApplyListSelection();
        EnsureSelectionVisible();
    }

    /// <summary>Enter 启动列表选中项(与单击共用防重入口)。</summary>
    private void LaunchSelected()
    {
        if (_listItems.Count == 0 || _listSelection.Selected >= _listItems.Count)
        {
            return;
        }

        TryLaunchFromList(_listItems[_listSelection.Selected]);
    }

    /// <summary>聚焦启动项列表搜索框(「/」或 Ctrl+F)。</summary>
    private void FocusItemsSearch() => _itemsSearchBox?.Focus();

    /// <summary>按当前选中索引重涂选中项为 accent,其余恢复区背景(仅重涂新旧两项,不重建列表)。</summary>
    private void ApplyListSelection()
    {
        if (_listButtons.Count == 0)
        {
            _styledSelection = -1;
            return;
        }

        var selected = _listSelection.Selected;
        if (_styledSelection == selected)
        {
            return;
        }

        if (_styledSelection >= 0 && _styledSelection < _listButtons.Count)
        {
            StyleListItem(_styledSelection, hovered: _styledSelection == _hoveredIndex);
        }

        if (selected >= 0 && selected < _listButtons.Count)
        {
            StyleListItem(selected, hovered: selected == _hoveredIndex);
        }

        _styledSelection = selected;
    }

    /// <summary>按「选中 &gt; 悬停 &gt; 常态」优先级重涂列表项:选中 = 低调 accent 背景;悬停 = 背景微亮。</summary>
    private void StyleListItem(int index, bool hovered)
    {
        if (index < 0 || index >= _listButtons.Count)
        {
            return;
        }

        var button = _listButtons[index];
        var selected = index == _listSelection.Selected;
        button.Background(selected
            ? SelectedBackground
            : hovered ? HoverBackground : _theme.EditorArea.Background);
    }

    /// <summary>悬停背景:暗主题向白微亮、亮主题向黑微暗(等效「升一层」)。</summary>
    private Color HoverBackground =>
        _theme.IsDark
            ? _theme.EditorArea.Background.Lerp(Color.FromRgb(255, 255, 255), 0.08)
            : _theme.EditorArea.Background.Lerp(Color.FromRgb(0, 0, 0), 0.06);

    /// <summary>选中背景:accent 与区背景按 2:8 回混的低调选中色(替代整块鲜艳 accent)。</summary>
    private Color SelectedBackground =>
        _theme.EditorArea.Accent.Lerp(_theme.EditorArea.Background, 0.8);

    /// <summary>记录当前悬停项并重涂:进入时替换旧悬停项,离开时仅当是当前悬停项才清除(事件顺序无关)。</summary>
    private void SetItemHovered(int index, bool raised)
    {
        if (raised)
        {
            if (_hoveredIndex == index)
            {
                return;
            }

            if (_hoveredIndex >= 0)
            {
                StyleListItem(_hoveredIndex, hovered: false);
            }

            _hoveredIndex = index;
            StyleListItem(index, hovered: true);
        }
        else if (_hoveredIndex == index)
        {
            _hoveredIndex = -1;
            StyleListItem(index, hovered: false);
        }
    }

    /// <summary>主题切换后全量重涂(WithTheme 回调先恢复区背景,此处重涂选中/悬停态)。</summary>
    private void ReapplyListSelection()
    {
        for (var i = 0; i < _listButtons.Count; i++)
        {
            StyleListItem(i, hovered: i == _hoveredIndex);
        }

        _styledSelection = _listSelection.Selected;
    }

    /// <summary>选中项滚出可视区时,滚动使其可见。</summary>
    private void EnsureSelectionVisible()
    {
        if (_listButtons.Count == 0 || _listScrollViewer is not { } scroller)
        {
            return;
        }

        var selected = _listButtons[_listSelection.Selected];
        var itemRect = selected.RectToScreen(new Rect(0, 0, selected.RenderSize.Width, selected.RenderSize.Height));
        var viewportRect = scroller.RectToScreen(new Rect(0, 0, scroller.ViewportWidth, scroller.ViewportHeight));

        if (itemRect.Top < viewportRect.Top)
        {
            scroller.SetScrollOffsets(scroller.HorizontalOffset, scroller.VerticalOffset - (viewportRect.Top - itemRect.Top));
        }
        else if (itemRect.Bottom > viewportRect.Bottom)
        {
            scroller.SetScrollOffsets(scroller.HorizontalOffset, scroller.VerticalOffset + (itemRect.Bottom - viewportRect.Bottom));
        }
    }

    /// <summary>焦点是否在文本输入控件内(搜索框/详情表单/下拉框):是则不劫持按键。</summary>
    private bool IsTextInputFocused() =>
        _window is { } window
        && window.FocusManager?.FocusedElement is TextBox or MultiLineTextBox or PasswordBox or ComboBox;

    private void LaunchItem(LauncherItem item)
    {
        var result = _runner.Launch(item);
        AppendLog(result.Success ? "✓ " + result.Message : "✗ " + result.Message);
        _launchStatus.Value = result.Message;
        // 失败醒目:状态栏红字 + toast;成功静默(仅状态栏轻文字)
        _workbench.SetStatusTextColor("launch", result.Success ? null : ShellIcons.HotkeyWarning);
        if (!result.Success)
        {
            _window?.ShowToast(result.Message);
        }
    }

    private void Feedback(string message)
    {
        AppendLog("⚠ " + message);
        _launchStatus.Value = message;
    }

    /// <summary>空状态组件:文案 + 引导动作按钮,启动项列表与分类树共用;颜色取自所在区的五区色板。</summary>
    private UIElement EmptyStateView(string message, string actionLabel, Action action, Color foreground, Color background) =>
        new StackPanel()
            .Padding(16)
            .Spacing(8)
            .Children(
                new Label().Text(message).FontSize(13)
                    .WithTheme((_, label) => label.Foreground(foreground)),
                new Button()
                    .Content(new Label().Text(actionLabel)
                        .WithTheme((_, label) => label.Foreground(foreground)))
                    .OnClick(action)
                    .CanDrag(false)
                    .WithTheme((_, button) => button.Background(background))
            );

    /// <summary>清空启动项列表搜索词并刷新列表。</summary>
    private void ClearItemsSearch()
    {
        _query = "";
        if (_itemsSearchBox is { } box)
        {
            box.Text = "";
        }

        ShowNav(_navId);
    }

    /// <summary>清除启动类型过滤(回到「全部」)并刷新列表;由下拉 SelectionChanged 统一走过滤刷新路径,避免重复刷新。</summary>
    private void ClearKindFilter()
    {
        if (_kindCombo is { } combo)
        {
            combo.SelectedIndex = 0;
        }
    }

    /// <summary>清空分类树搜索词并刷新树。</summary>
    private void ClearCategorySearch()
    {
        _sideBarFilters["launch"] = "";
        if (_categorySearchBox is { } box)
        {
            box.Text = "";
        }

        RefreshCategoryTree();
    }

    private void EditItem(LauncherItem item)
    {
        _current = item;
        ShowItemDetail(item);
        _workbench.SetDocumentReveal(DetailDocumentId, "launch", () => RevealInSidebar(item));
        _workbench.OpenDocument(DetailDocumentId); // 编辑器区激活「启动项详情」文档
        _workbench.SetDocumentTitle(DetailDocumentId, item.Name); // 标签随当前对象显示项名
    }

    /// <summary>「在侧边栏定位」:按树序取首个所属分类节点并切到该节点、选中它;无归属定位「未分类」。行为确定、可重复。</summary>
    private void RevealInSidebar(LauncherItem item)
    {
        var navId = LauncherData.FirstCategoryIdInTreeOrder(_store.Categories.ToList(), item.CategoryIds ?? [])
            ?? LauncherData.UncategorizedNavId;
        // 目标节点可能被侧边栏搜索过滤隐藏,先清空过滤,保证定位后节点可见且被选中
        ClearCategorySearch();
        ShowNav(navId);
        SelectNavNode(navId);
    }

    /// <summary>侧边栏树中选中指定导航节点;节点不在当前树中(理论上已由定位前清空过滤保证)时保持现状。</summary>
    private void SelectNavNode(string navId)
    {
        if (_treeItems is not { } items)
        {
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (items.GetItem(i) is CategoryTreeNode node && node.Id == navId)
            {
                items.SelectSingle(i);
                return;
            }
        }
    }

    private void ShowEmptyDetail()
    {
        _current = null;
        _detailPanel.Clear();
        _detailPanel.Add(new Label()
            .Text("从左侧选择启动项查看详情")
            .WithTheme((_, label) => label.Foreground(_theme.EditorArea.Foreground)));
        _workbench.SetDocumentTitle(DetailDocumentId, "启动项详情"); // 无选中项时标签回落默认名
    }

    private void ShowItemDetail(LauncherItem item)
    {
        _detailPanel.Clear();
        _detailPanel.Add(BuildDetailForm(item));
    }

    private UIElement BuildDetailForm(LauncherItem item)
    {
        _loading = true;

        var name = TextField(item.Name, "启动项名称");
        var description = TextField(item.Description ?? "", "简要介绍该启动项");
        var command = TextField(item.Command, "程序、脚本或 URL");
        var args = TextField(item.Args ?? "", "可选参数");
        var workingDirectory = TextField(item.WorkingDirectory ?? "", "可选工作目录");
        var categoryOptions = LauncherData.FlattenCategoryOptions(_store.Categories.ToList());
        var categoryChecks = BuildCategoryChecks(categoryOptions, item.CategoryIds ?? []);
        var icon = TextField(item.Icon ?? "", "可选图标路径");
        var hotkey = TextField(item.Hotkey ?? "", "可选每项热键,如 Ctrl+Shift+1");
        var hotkeyHint = new Label()
            .Text("")
            .FontSize(11)
            .WithTheme((_, label) => label.Foreground(ShellIcons.HotkeyWarning));

        _loading = false;

        name.TextChanged += text => UpdateCurrent(i => i with { Name = text });
        description.TextChanged += text => UpdateCurrent(i => i with { Description = string.IsNullOrWhiteSpace(text) ? null : text });
        command.TextChanged += text => UpdateCurrent(i => i with { Command = text });
        args.TextChanged += text => UpdateCurrent(i => i with { Args = string.IsNullOrWhiteSpace(text) ? null : text });
        workingDirectory.TextChanged += text => UpdateCurrent(i => i with { WorkingDirectory = string.IsNullOrWhiteSpace(text) ? null : text });
        icon.TextChanged += text => UpdateCurrent(i => i with { Icon = string.IsNullOrWhiteSpace(text) ? null : text });
        hotkey.TextChanged += text =>
        {
            UpdateCurrent(i => i with { Hotkey = string.IsNullOrWhiteSpace(text) ? null : text });
            hotkeyHint.Text = text.Length > 0 && !HotkeyParser.TryParse(text, out _, out _)
                ? "热键格式无效,如 Ctrl+Shift+1(需至少一个修饰键)"
                : "";
        };

        return new StackPanel()
            .Padding(24)
            .Spacing(12)
            .Children(
                // 第一行工具行:启动 / 删除(编辑时测试启动不必滚动)
                new StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content(new Label().Text("启动"))
                            .OnClick(() => LaunchItem(item))
                            .CanDrag(false),
                        new Button()
                            .Content(new Label().Text("删除启动项"))
                            .OnClick(() => DeleteItem(item))
                            .CanDrag(false)
                    ),
                SectionTitle("基本", _theme.EditorArea.Foreground),
                FieldRow("名称", name),
                FieldRow("简介", description),
                FieldRow("命令", command),
                FieldRow("参数", args),
                FieldRow("工作目录", workingDirectory),
                FieldRow("分类", categoryChecks),
                SectionTitle("高级", _theme.EditorArea.Foreground),
                FieldRow("图标", icon),
                FieldRow("每项热键", hotkey),
                hotkeyHint
            );
    }

    /// <summary>分类平铺多选:未分类置顶为隐式状态(全不勾选),分类按「父 / 子」路径平铺为 checkbox,勾选互不联动(父不连带子)。</summary>
    private UIElement BuildCategoryChecks(List<CategoryOption> options, List<string> selectedIds)
    {
        var selected = new HashSet<string>(selectedIds, StringComparer.Ordinal);
        var panel = new StackPanel().Spacing(6);
        panel.Add(new Label().Text("未分类(全不勾选)").FontSize(12)
            .WithTheme((_, label) => label.Foreground(_theme.EditorArea.Foreground)));
        foreach (var option in options)
        {
            if (option.Id is not { } categoryId)
            {
                continue; // 未分类已置顶为非可勾选项
            }

            var check = new CheckBox()
                .IsChecked(selected.Contains(categoryId))
                .Content(option.Path, false)
                .CanDrag(false);
            check.OnCheckedChanged(isChecked => UpdateCurrent(i => i with
            {
                CategoryIds = isChecked
                    ? [.. (i.CategoryIds ?? []), categoryId]
                    : (i.CategoryIds ?? []).Where(id => id != categoryId).ToList()
            }));
            panel.Add(check);
        }

        return panel;
    }

    private UIElement FieldRow(string label, UIElement input) => new StackPanel()
        .Spacing(4)
        .Children(
            new Label().Text(label).FontSize(12)
                .WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)),
            input
        );

    /// <summary>详情页分区标题(基本/高级)。</summary>
    private static UIElement SectionTitle(string text, Color foreground) => new Label()
        .Text(text)
        .FontSize(14)
        .Bold()
        .WithTheme((_, label) => label.Foreground(foreground));

    private static TextBox TextField(string value, string placeholder) => new()
    {
        Text = value,
        Placeholder = placeholder,
        CanDrag = false,
    };

    private void UpdateCurrent(Func<LauncherItem, LauncherItem> edit)
    {
        if (_current is not { } current || _loading)
        {
            return;
        }

        var index = _items.FindIndex(item => item.Id == current.Id);
        if (index < 0)
        {
            return;
        }

        _current = edit(_items[index]);
        _items[index] = _current;
        _store.Save(_items);
        _itemHotkeys.RegisterAll();
        ShowNav(_navId);
    }

    private void DeleteItem(LauncherItem item)
    {
        _items.RemoveAll(candidate => candidate.Id == item.Id);
        _store.Save(_items);
        _itemHotkeys.RegisterAll();
        ShowEmptyDetail();
        ShowNav(_navId);
    }
}

/// <summary>列表工具行类型过滤下拉选项:Kind 为 null 表示「全部」。</summary>
internal sealed record KindFilterOption(LauncherData.ItemKind? Kind, string Label);
