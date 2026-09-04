using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Mew.Launcher;
using Mew.Workbench;
using Xunit;
using OverlayServiceContract = Mew.Workbench.IOverlayService;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Host.Tests;

/// <summary>
/// 宿主组装测试缝(票据 08):复刻 MewHost 组合根的组装路径(无窗口/消息循环,USER STORY 15)
/// ——真实 Launcher 模块 + 测试内假模块经 ToolModuleContext 贡献,宿主补设置上下文后统一 Build;
/// 配对违规与跨模块 ID 冲突在 Build 期被拒绝。与 LauncherModuleTests 同构:操作真实
/// %APPDATA%\Mew 布局/数据文件,集合内串行(跨程序集顺序由 dotnet test 逐工程执行保证)。
/// </summary>
[Collection("HostComposition")]
public class HostCompositionTests
{
    /// <summary>组装:两模块五区贡献 + 设置节 + 浮层搜索源全部就位,Build 通过。</summary>
    [Fact]
    public void Configure_真实Launcher加假模块_Build成功_五区含各模块贡献()
    {
        using var _ = IsolateUserFiles();

        var workbench = new WorkbenchType();
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "mew-host-tests", Guid.NewGuid().ToString("N") + ".json"));
        var overlay = new RecordingOverlay();
        var settingsSections = new SettingsSectionRegistry();
        var context = new ToolModuleContext(
            workbench,
            windowHandle: IntPtr.Zero,
            window: null,
            hotkeys: new HotkeyService(),
            settings: settings,
            overlay: overlay,
            theme: workbench.ThemeContext,
            settingsSections: settingsSections);

        new LauncherModule().Configure(context);
        new FakeModule().Configure(context);

        // 宿主补上设置上下文(票据 06:设置活动栏/侧边栏/文档归宿主)
        workbench
            .ActivityBar(bar => bar.Item("settings", "设置", GlyphKind.Hamburger))
            .SideBar(side => side.View("settings", "设置", new StackPanel()))
            .EditorArea(editor => editor.Document("settings-document", "设置", new StackPanel()));

        var shell = workbench.Build();
        var dock = FindByType(shell, typeof(DockingManager)) as DockingManager
            ?? throw new InvalidOperationException("未找到 DockingManager。");

        // 侧边栏:同一时刻只显示活动上下文一个视图,切换后各模块的视图都建成
        Assert.Contains(dock.Panes, pane => pane.Component == "launch");
        workbench.SelectActivity("todo");
        Assert.Contains(dock.Panes, pane => pane.Component == "todo");
        Assert.Contains(dock.Panes, pane => pane.Component == "output");
        Assert.Contains(dock.Panes, pane => pane.Component == "todo-output");

        // 编辑器文档:运行时打开后两模块文档都挂接
        workbench.OpenDocument("items");
        workbench.OpenDocument("todo-items");
        Assert.Contains(dock.DocumentPanes, pane => pane.Component == "items");
        Assert.Contains(dock.DocumentPanes, pane => pane.Component == "todo-items");

        // 状态栏:假模块的状态项可见
        Assert.Contains(FindAllByType(shell, typeof(Label)).OfType<Label>(), label => label.Text == "待办就绪");

        // 设置节:Launcher「数据」+ 假模块节(按注册顺序)
        Assert.Equal(["data", "todo-options"], settingsSections.Sections.Select(section => section.Id));

        // 浮层搜索源:两模块都注册
        Assert.Equal(["launcher", "todo"], overlay.Sources.Select(source => source.Id));
    }

    /// <summary>Build 期配对校验:活动栏项没有对应侧边栏视图即拒绝(防止模块贡献半截)。</summary>
    [Fact]
    public void 组装_配对违规_活动栏多出无侧边栏项_Build拒绝()
    {
        var workbench = new WorkbenchType()
            .ActivityBar(bar => bar.Item("launch", "启动", GlyphKind.Hamburger))
            .SideBar(side => side.View("launch", "启动", new StackPanel()));
        workbench.ActivityBar(bar => bar.Item("orphan", "孤儿", GlyphKind.Hamburger)); // 无配对侧边栏视图

        var exception = Assert.Throws<InvalidOperationException>(workbench.Build);

        Assert.Equal("活动栏项必须与侧边栏视图按唯一 ID 一对一配对。", exception.Message);
    }

    /// <summary>Build 期跨模块校验:两个模块贡献了相同停靠组件 id(如文档「items」)即拒绝。</summary>
    [Fact]
    public void 组装_跨模块停靠ID冲突_Build拒绝()
    {
        using var _ = IsolateUserFiles();

        var workbench = new WorkbenchType();
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "mew-host-tests", Guid.NewGuid().ToString("N") + ".json"));
        var context = new ToolModuleContext(
            workbench,
            windowHandle: IntPtr.Zero,
            window: null,
            hotkeys: new HotkeyService(),
            settings: settings,
            overlay: new RecordingOverlay(),
            theme: workbench.ThemeContext,
            settingsSections: new SettingsSectionRegistry());

        new LauncherModule().Configure(context);
        new ConflictingModule().Configure(context); // 与 Launcher 共用文档 id「items」

        var exception = Assert.Throws<InvalidOperationException>(workbench.Build);

        Assert.Equal("侧边栏、编辑器区与底部面板的停靠组件 ID 必须全局唯一。", exception.Message);
    }

    /// <summary>与 Launcher 模块的「items」文档 id 冲突的假模块,验证宿主 Build 期跨模块 ID 校验。</summary>
    private sealed class ConflictingModule : IMewToolModule
    {
        public string Id => "conflict";

        public string DisplayName => "冲突模块";

        public void Configure(ToolModuleContext context)
        {
            context.Workbench
                .ActivityBar(bar => bar.Item("conflict", "冲突", GlyphKind.Hamburger))
                .SideBar(side => side.View("conflict", "冲突", new StackPanel()))
                .EditorArea(editor => editor.Document("items", "重复文档", new StackPanel()));
        }
    }

    /// <summary>记录注册的搜索源的浮层契约替身(测试环境不建 MewUI 窗口)。</summary>
    private sealed class RecordingOverlay : OverlayServiceContract
    {
        public List<ISearchSource> Sources { get; } = [];

        public void AddSearchSource(ISearchSource source) => Sources.Add(source);
    }

    /// <summary>临时移走用户真实数据/布局文件,保证用例不污染用户数据且任意本机可复现。</summary>
    private static IDisposable IsolateUserFiles()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
        var names = new[] { "launcher.json", "layout.json", "presentation.json" };
        var saved = names.ToDictionary(name => name, name => File.Exists(Path.Combine(appData, name))
            ? File.ReadAllBytes(Path.Combine(appData, name))
            : null);
        foreach (var name in names)
        {
            File.Delete(Path.Combine(appData, name));
        }

        return new Disposable(() =>
        {
            foreach (var (name, bytes) in saved)
            {
                var path = Path.Combine(appData, name);
                if (bytes is not null)
                {
                    Directory.CreateDirectory(appData);
                    File.WriteAllBytes(path, bytes);
                }
                else
                {
                    File.Delete(path);
                }
            }
        });
    }

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
}
