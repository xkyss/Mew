namespace Mew.Workbench;

/// <summary>
/// 多源浮层结果聚合:跨源扁平混排;每源上限 = 全局上限 / 源数
/// (单源时为全局上限,与单源时代行数一致),全局再取上限截断。
/// 结果携带来源供行渲染打标记。纯逻辑,可独立测试。
/// </summary>
public static class OverlaySearchAggregator
{
    /// <summary>全局结果上限(单源时代浮层的行数上限)。</summary>
    public const int DefaultGlobalCap = 8;

    public static IReadOnlyList<OverlayResultEntry> Aggregate(
        IReadOnlyList<ISearchSource> sources, string query, int globalCap = DefaultGlobalCap)
    {
        if (sources.Count == 0)
        {
            return [];
        }

        var perSourceMax = Math.Max(1, globalCap / sources.Count);
        var entries = new List<OverlayResultEntry>();
        foreach (var source in sources)
        {
            // 防御:契约要求源遵守 maxResults,聚合侧再按每源上限兜底
            foreach (var result in source.Search(query, perSourceMax).Take(perSourceMax))
            {
                entries.Add(new OverlayResultEntry(source, result));
            }
        }

        return entries.Take(globalCap).ToList();
    }
}
