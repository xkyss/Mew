using Aprillz.MewUI.Controls;
using Mew.Workbench;

namespace Mew.Host.Tests;

/// <summary>
/// 测试内假第二模块(票据 08):验证宿主可组装多个工具模块——独立贡献活动栏/侧边栏、
/// 编辑器文档、底部面板、状态栏项、设置节与浮层搜索源,与真实 Launcher 模块并存。
/// </summary>
public sealed class FakeModule : IMewToolModule
{
    public string Id => "todo";

    public string DisplayName => "待办";

    public void Configure(ToolModuleContext context)
    {
        context.Workbench
            .ActivityBar(bar => bar.Item("todo", "待办", GlyphKind.Hamburger))
            .SideBar(side => side.View("todo", "待办", new StackPanel()))
            .EditorArea(editor => editor.Document("todo-items", "待办列表", new StackPanel()))
            .Panel(panel => panel.View("todo-output", "待办输出", new StackPanel()))
            .StatusBar(status => status.Item("todo", "待办就绪"));

        context.SettingsSections.Add("todo-options", "待办选项", () => new StackPanel());
        context.Overlay.AddSearchSource(new TodoSearchSource());
    }
}

/// <summary>假模块的浮层搜索源:行为不关键,用于验证多源注册与聚合入口。</summary>
internal sealed class TodoSearchSource : ISearchSource
{
    public string Id => "todo";

    public string DisplayName => "待办";

    public IReadOnlyList<SearchResult> Search(string query, int maxResults) => [];
}
