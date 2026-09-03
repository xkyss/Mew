using Mew.Workbench;

namespace Mew.Launcher;

/// <summary>
/// Launcher 工具模块的浮层搜索源:把启动项领域搜索(命令匹配规则不变)贡献到框架浮层。
/// 行数据:主行 = 启动项名称,副行 = 命令,图标经框架 IconResolver 解析。
/// </summary>
internal sealed class LauncherSearchSource : ISearchSource
{
    private readonly List<LauncherItem> _items;
    private readonly LauncherRunner _runner;
    private readonly IconResolver _icons;

    public LauncherSearchSource(List<LauncherItem> items, LauncherRunner runner, IconResolver icons)
    {
        _items = items;
        _runner = runner;
        _icons = icons;
    }

    public string Id => "launcher";

    public string DisplayName => "启动项";

    public IReadOnlyList<SearchResult> Search(string query, int maxResults) =>
        _items
            .Where(item => LauncherSearch.Matches(item, query))
            .Take(maxResults)
            .Select(item => new SearchResult(
                item.Name,
                item.Command,
                _icons.Resolve(item),
                () => _runner.Launch(item)))
            .ToList();
}
