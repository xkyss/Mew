using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Mew.Workbench;

/// <summary>
/// 标题栏构建(框架级外壳服务,随 ADR-000200 从 Launcher 移入主框架):
/// 左区图标 + 菜单栏(File=设置/退出、View=区域显隐、Help=关于)、右区「切换主题」图标按钮。
/// 语义动作(设置/退出/关于/主题循环)由宿主注入,本类只负责组装;归属由调用方(宿主)决定。
/// </summary>
public static class TitleBarBuilder
{
    /// <summary>标题栏:左区图标 + 菜单栏(File=设置/退出、View=区域显隐、Help=关于)、右区「切换主题」图标按钮。</summary>
    public static Button BuildTitleBar(
        NativeChromeWindow window,
        Action quit,
        Action openSettings,
        Action cycleTheme,
        Workbench workbench,
        Action<NativeChromeWindow> showAbout)
    {
        var appIcon = IconResolver.ExtractIcon(Environment.ProcessPath!);
        if (appIcon is not null)
        {
            window.TitleBarLeft.Add(new Image()
                .Source(appIcon)
                .Size(24, 24)
                .Margin(new Thickness(6, 0, 6, 0)));
        }

        var menuBar = new MenuBar()
            .Height(28)
            .DrawBottomSeparator(false)
            .Background(Color.Transparent);
        var scope = menuBar.Commands;
        menuBar.Items(
            SubMenu("_File", new Menu()
                .Item(MewCommands.Register(scope, "mew.settings", "设置", openSettings))
                .Separator()
                .Item(MewCommands.Register(scope, "mew.quit", "退出", quit))),
            SubMenu("_View", BuildViewMenu(workbench, scope)),
            SubMenu("_Help", new Menu().Item(MewCommands.Register(scope, "mew.about", "关于", () => showAbout(window))))
        );

        window.TitleBarLeft.Add(menuBar);

        // 右区:切换主题图标按钮(图标与提示由宿主 UpdateThemeButton 随模式刷新)
        var themeButton = new Button()
            .Content(new Label().Text(""))
            .ToolTip("切换主题")
            .OnClick(cycleTheme)
            .CanDrag(false)
            .Size(36, 28);
        window.TitleBarRight.Add(themeButton);
        return themeButton;
    }

    /// <summary>顶层菜单项 + 子菜单（0.20 菜单模型:MenuItem.Text + SubMenu 引用）。</summary>
    private static MenuItem SubMenu(string text, Menu subMenu) => new MenuItem(text).Menu(subMenu);

    /// <summary>查看菜单:显示/隐藏 侧边栏、底部面板、活动栏、状态栏;菜单项文本随当前显隐状态反转。</summary>
    public static Menu BuildViewMenu(Workbench workbench, CommandScope scope) => new Menu()
        .Add(ViewToggleItem(workbench, scope, workbench.ToggleSideBar, () => workbench.IsSideBarVisible, "侧边栏"))
        .Add(ViewToggleItem(workbench, scope, workbench.TogglePanel, () => workbench.IsPanelVisible, "底部面板"))
        .Add(ViewToggleItem(workbench, scope, workbench.ToggleActivityBar, () => workbench.IsActivityBarVisible, "活动栏"))
        .Add(ViewToggleItem(workbench, scope, workbench.ToggleStatusBar, () => workbench.IsStatusBarVisible, "状态栏"));

    /// <summary>构造「(显示/隐藏)xx」菜单项:文本反映当前状态,点击切换后文本反转（点击经命令派发）。</summary>
    private static MenuItem ViewToggleItem(Workbench workbench, CommandScope scope, Action toggle, Func<bool> isVisible, string label)
    {
        var command = MewCommands.Register(scope, $"mew.view.{label}", "", toggle);
        var item = new MenuItem(command);
        void UpdateText()
        {
            item.Text = isVisible() ? $"隐藏{label}" : $"显示{label}";
        }

        workbench.PresentationChanged += UpdateText;
        UpdateText();
        return item;
    }
}
