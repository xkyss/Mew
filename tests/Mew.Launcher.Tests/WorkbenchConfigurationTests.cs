using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Mew.Launcher;
using Mew.Workbench;
using Xunit;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Launcher.Tests;

/// <summary>
/// Workbench 配置/运行期校验:五区配对、文档标题、呈现状态持久化等。
/// 与 LauncherModuleTests 同集合串行:两者都操作真实 %APPDATA%\Mew 布局文件,
/// 且 MewDock 在 Build 后持有文件句柄,并行会互相踩文件。
/// </summary>
[Collection("IsolatedUserFiles")]
public class WorkbenchConfigurationTests
{
    /// <summary>回归:点击工具窗格的关闭按钮后,View 菜单与持久化使用的 Workbench 显隐状态必须同步。</summary>
    [Fact]
    public void CloseToolPane_关闭侧边栏和底部面板_View菜单同步为显示()
    {
        using var _ = IsolateUserLayoutFiles();

        var workbench = CreateWorkbenchWithAllZones();
        var shell = workbench.Build();
        var dock = FindByType(shell, typeof(DockingManager)) as DockingManager
            ?? throw new InvalidOperationException("未找到 DockingManager。");
        var menu = TitleBarBuilder.BuildViewMenu(workbench);
        var sideBarItem = Assert.IsType<MenuItem>(menu.Items[0]);
        var panelItem = Assert.IsType<MenuItem>(menu.Items[1]);
        var sideBarPane = dock.Panes.Single(pane => pane.Component == "launch");
        var panelPane = dock.Panes.Single(pane => pane.Component == "output");

        sideBarPane.Close();
        panelPane.Close();

        Assert.False(workbench.IsSideBarVisible);
        Assert.False(workbench.IsPanelVisible);
        Assert.Equal("显示侧边栏", sideBarItem.Text);
        Assert.Equal("显示底部面板", panelItem.Text);
    }

    /// <summary>回归:活动栏切换侧边栏后,View 菜单必须同步反映实际状态。</summary>
    [Fact]
    public void ViewMenu_活动栏隐藏侧边栏后_菜单项同步显示状态()
    {
        using var _ = IsolateUserLayoutFiles();

        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
            .SideBar(side => side.View("launch", "启动", new StackPanel()));
        workbench.Build();
        var menu = TitleBarBuilder.BuildViewMenu(workbench);
        var sideBarItem = Assert.IsType<MenuItem>(menu.Items[0]);

        Assert.Equal("隐藏侧边栏", sideBarItem.Text);

        workbench.SelectActivity("launch");

        Assert.Equal("显示侧边栏", sideBarItem.Text);
    }

    /// <summary>回归:View 菜单切换的四个区域在下次构建时必须恢复,否则菜单文案与实际界面会脱节。</summary>
    [Fact]
    public void Presentation_所有区域隐藏后重建_恢复相同显隐状态()
    {
        using var _ = IsolateUserLayoutFiles();

        var first = CreateWorkbenchWithAllZones();
        first.Build();
        first.ToggleActivityBar();
        first.ToggleSideBar();
        first.TogglePanel();
        first.ToggleStatusBar();

        var restored = CreateWorkbenchWithAllZones();
        restored.Build();

        Assert.False(restored.IsActivityBarVisible);
        Assert.False(restored.IsSideBarVisible);
        Assert.False(restored.IsPanelVisible);
        Assert.False(restored.IsStatusBarVisible);
    }

    [Fact]
    public void Presentation_旧文件仅记录侧边栏_新增区域保持默认显示()
    {
        using var _ = IsolateUserLayoutFiles();
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
        Directory.CreateDirectory(appData);
        File.WriteAllText(Path.Combine(appData, "presentation.json"), """{ "ActiveActivityId": "launch", "IsSideBarVisible": false }""");

        var workbench = CreateWorkbenchWithAllZones();
        workbench.Build();

        Assert.True(workbench.IsActivityBarVisible);
        Assert.False(workbench.IsSideBarVisible);
        Assert.True(workbench.IsPanelVisible);
        Assert.True(workbench.IsStatusBarVisible);
    }

    /// <summary>回归:View 菜单的「隐藏底部面板」必须关闭窗格,而非仅切换为悬浮可见的自动隐藏状态。</summary>
    [Fact]
    public void TogglePanel_隐藏后关闭窗格_再次切换恢复窗格()
    {
        using var _ = IsolateUserLayoutFiles();

        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
            .SideBar(side => side.View("launch", "启动", new StackPanel()))
            .Panel(panel => panel.View("output", "输出", new StackPanel()));

        var shell = workbench.Build();
        var dock = FindByType(shell, typeof(DockingManager)) as DockingManager
            ?? throw new InvalidOperationException("未找到 DockingManager。");

        Assert.Contains(dock.Panes, pane => pane.Component == "output");

        workbench.TogglePanel();

        Assert.False(workbench.IsPanelVisible);
        Assert.DoesNotContain(dock.Panes, pane => pane.Component == "output");

        workbench.TogglePanel();

        Assert.True(workbench.IsPanelVisible);
        Assert.Contains(dock.Panes, pane => pane.Component == "output");
    }

    [Fact]
    public void Build_停靠组件跨区域重名_拒绝配置()
    {
        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
            .SideBar(side => side.View("launch", "启动", new StackPanel()))
            .EditorArea(editor => editor.Document("launch", "启动项", new StackPanel()));

        var exception = Assert.Throws<InvalidOperationException>(workbench.Build);

        Assert.Equal("侧边栏、编辑器区与底部面板的停靠组件 ID 必须全局唯一。", exception.Message);
    }

    /// <summary>
    /// 回归:设置文档 tab 关闭后重新打开时内容必须重新挂接回停靠树(否则 tab 显示空白)。
    /// 此前 OpenDocument 把共享内容实例(原始 StackPanel)作为显式内容传入,而 ContentFactory
    /// 解析出的是新的 Border 包装;MewDock 的 SyncContent 分离旧 Border 后,因共享元素的
    /// Parent 仍指向旧包装而无法重新挂接,重新打开的 tab 内容为空。
    /// </summary>
    [Fact]
    public void OpenDocument_关闭后重新打开_内容仍挂接在停靠树()
    {
        using var _ = IsolateUserLayoutFiles();

        var settingsContent = new StackPanel();
        var workbench = new WorkbenchType()
            .ActivityBar(bar =>
            {
                bar.Item("launch", "启动", GlyphKind.Hamburger);
                bar.Item("settings", "设置", GlyphKind.Hamburger);
            })
            .SideBar(side => side
                .View("launch", "启动", new StackPanel())
                .View("settings", "设置", new StackPanel()))
            .EditorArea(editor => editor
                .Document("items", "启动项", new StackPanel())
                .Document("settings-document", "设置", settingsContent));

        var shell = workbench.Build();
        var dock = FindByType(shell, typeof(DockingManager)) as DockingManager
            ?? throw new InvalidOperationException("未找到 DockingManager。");

        // 打开设置文档 → 内容挂接在停靠树中
        workbench.OpenDocument("settings-document");
        Assert.True(IsInLiveTree(shell, settingsContent), "打开后设置内容应挂接在停靠树中。");

        // 关闭设置 tab → 文档 pane 消失,内容离开停靠树
        var pane = dock.DocumentPanes.FirstOrDefault(p => p.Component == "settings-document");
        Assert.NotNull(pane);
        pane!.Close();
        Assert.DoesNotContain(dock.DocumentPanes, p => p.Component == "settings-document");

        // 重新打开设置文档 → 内容再次挂接(修复前此处内容被孤立,tab 空白)
        workbench.OpenDocument("settings-document");
        Assert.True(IsInLiveTree(shell, settingsContent), "重新打开后设置内容应重新挂接在停靠树中。");

        // 激活其他文档(相当于点击侧边栏「全部」)后切回设置 tab,内容仍保持挂接
        workbench.OpenDocument("items");
        workbench.OpenDocument("settings-document");
        Assert.True(IsInLiveTree(shell, settingsContent), "切换文档后设置内容仍应保持挂接。");
    }

    /// <summary>改文档标题能力:已注册文档的标签标题随调用更新(详情文档随当前对象变化)。</summary>
    [Fact]
    public void SetDocumentTitle_已注册文档_更新标签标题()
    {
        using var _ = IsolateUserLayoutFiles();

        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
            .SideBar(side => side.View("launch", "启动", new StackPanel()))
            .EditorArea(editor => editor.Document("detail", "启动项详情", new StackPanel()));

        var shell = workbench.Build();
        var dock = FindByType(shell, typeof(DockingManager)) as DockingManager
            ?? throw new InvalidOperationException("未找到 DockingManager。");
        workbench.OpenDocument("detail");

        workbench.SetDocumentTitle("detail", "我的启动项");

        Assert.Equal("我的启动项", dock.DocumentPanes.Single(p => p.Component == "detail").Title);
    }

    /// <summary>改文档标题能力:未注册文档拒绝更新,与 SetDocumentReveal 的校验一致。</summary>
    [Fact]
    public void SetDocumentTitle_未注册文档_拒绝()
    {
        using var _ = IsolateUserLayoutFiles();

        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
            .SideBar(side => side.View("launch", "启动", new StackPanel()))
            .EditorArea(editor => editor.Document("detail", "启动项详情", new StackPanel()));
        workbench.Build();

        var exception = Assert.Throws<ArgumentException>(() => workbench.SetDocumentTitle("ghost", "标题"));

        Assert.Equal("不存在编辑器文档“ghost”。 (Parameter 'id')", exception.Message);
    }

    /// <summary>键盘导航的门控依据:激活哪个编辑器文档即返回哪个文档 id(列表键盘导航只在启动项列表激活时生效)。</summary>
    [Fact]
    public void ActiveDocumentId_随激活文档变化()
    {
        using var _ = IsolateUserLayoutFiles();

        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
            .SideBar(side => side.View("launch", "启动", new StackPanel()))
            .EditorArea(editor => editor
                .Document("items", "启动项", new StackPanel())
                .Document("detail", "启动项详情", new StackPanel()));
        workbench.Build();

        workbench.OpenDocument("items");
        Assert.Equal("items", workbench.ActiveDocumentId);

        workbench.OpenDocument("detail");
        Assert.Equal("detail", workbench.ActiveDocumentId);
    }

    /// <summary>启动失败反馈的承载能力:状态栏项可临时变红(失败醒目),置 null 恢复区前景色。</summary>
    [Fact]
    public void SetStatusTextColor_状态栏项变红_置null恢复区前景()
    {
        using var _ = IsolateUserLayoutFiles();

        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
            .SideBar(side => side.View("launch", "启动", new StackPanel()))
            .StatusBar(status => status.Item("launch", "就绪"));
        var shell = workbench.Build();
        var label = FindAllByType(shell, typeof(Label))
            .OfType<Label>()
            .Single(l => l.Text == "就绪");
        var zoneForeground = workbench.ThemeContext.StatusBar.Foreground;

        var red = Color.FromRgb(200, 60, 60);
        workbench.SetStatusTextColor("launch", red);
        Assert.Equal(red, label.Foreground);

        workbench.SetStatusTextColor("launch", null);
        Assert.Equal(zoneForeground, label.Foreground);
    }

    /// <summary>区背景覆盖:设置后仅该区生效,其他区(含默认同色板的区)保持默认,前景/强调色不变。</summary>
    [Fact]
    public void ThemeContext_区背景覆盖_各区独立生效()
    {
        var theme = new WorkbenchType().ThemeContext;
        var panelBefore = theme.Get(WorkbenchZone.Panel);
        var editorBefore = theme.Get(WorkbenchZone.EditorArea);

        var overrideColor = Color.FromRgb(12, 34, 56);
        theme.SetZoneBackground(WorkbenchZone.Panel, overrideColor);

        var panelAfter = theme.Get(WorkbenchZone.Panel);
        Assert.Equal(overrideColor, panelAfter.Background);
        Assert.Equal(panelBefore.Foreground, panelAfter.Foreground);
        Assert.Equal(panelBefore.Accent, panelAfter.Accent);

        // 面板与侧边栏默认取同一色板背景,覆盖面板不影响侧边栏
        Assert.Equal(panelBefore.Background, theme.Get(WorkbenchZone.SideBar).Background);
        Assert.NotEqual(panelAfter.Background, theme.Get(WorkbenchZone.SideBar).Background);

        // 编辑器区不受影响
        Assert.Equal(editorBefore.Background, theme.Get(WorkbenchZone.EditorArea).Background);
    }

    /// <summary>临时移走用户真实布局/呈现文件,保证用例在任意本机状态下可复现且不污染用户数据。</summary>
    private static IDisposable IsolateUserLayoutFiles()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
        var layoutPath = Path.Combine(appData, "layout.json");
        var presentationPath = Path.Combine(appData, "presentation.json");
        var savedLayout = File.Exists(layoutPath) ? File.ReadAllBytes(layoutPath) : null;
        var savedPresentation = File.Exists(presentationPath) ? File.ReadAllBytes(presentationPath) : null;
        File.Delete(layoutPath);
        File.Delete(presentationPath);

        return new Disposable(() =>
        {
            if (savedLayout is not null)
            {
                Directory.CreateDirectory(appData);
                File.WriteAllBytes(layoutPath, savedLayout);
            }
            else
            {
                File.Delete(layoutPath);
            }

            if (savedPresentation is not null)
            {
                Directory.CreateDirectory(appData);
                File.WriteAllBytes(presentationPath, savedPresentation);
            }
            else
            {
                File.Delete(presentationPath);
            }
        });
    }

    private static WorkbenchType CreateWorkbenchWithAllZones() => new WorkbenchType()
        .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
        .SideBar(side => side.View("launch", "启动", new StackPanel()))
        .EditorArea(editor => editor.Document("items", "启动项", new StackPanel()))
        .Panel(panel => panel.View("output", "输出", new StackPanel()))
        .StatusBar(status => status.Item("launch", "就绪"));

    private sealed class Disposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    /// <summary>从指定根元素向下查找目标类型的全部元素。</summary>
    private static List<UIElement> FindAllByType(UIElement root, Type type)
    {
        var found = new List<UIElement>();
        if (type.IsInstanceOfType(root))
        {
            found.Add(root);
        }

        if (root is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                found.AddRange(FindAllByType((UIElement)child, type));
                return true;
            });
        }

        return found;
    }

    /// <summary>从指定根元素向下查找目标类型的第一个元素。</summary>
    private static UIElement? FindByType(UIElement root, Type type)
    {
        if (type.IsInstanceOfType(root))
        {
            return root;
        }

        if (root is IVisualTreeHost host)
        {
            UIElement? found = null;
            host.VisitChildren(child =>
            {
                found = FindByType((UIElement)child, type);
                return found is null;
            });
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>目标元素是否仍挂接在根元素的可见树中(沿 IVisualTreeHost 深度优先可达,即实际可见)。</summary>
    private static bool IsInLiveTree(UIElement root, UIElement target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        if (root is IVisualTreeHost host)
        {
            var found = false;
            host.VisitChildren(child =>
            {
                if (IsInLiveTree((UIElement)child, target))
                {
                    found = true;
                    return false;
                }

                return true;
            });
            return found;
        }

        return false;
    }
}
