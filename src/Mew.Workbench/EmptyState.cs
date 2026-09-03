namespace Mew.Workbench;

/// <summary>空状态类型:决定空态展示的文案与引导动作。</summary>
public enum EmptyStateKind
{
    /// <summary>无空态(有内容)。</summary>
    None,

    /// <summary>无数据:引导新增。</summary>
    NoData,

    /// <summary>有搜索词但无匹配:引导清空搜索。</summary>
    NoMatch,
}

/// <summary>空状态判定纯逻辑:启动项列表与分类树共用。</summary>
public static class EmptyState
{
    /// <summary>列表空态:有内容 → 无空态;无内容且有搜索词或类型过滤 → 无匹配;否则 → 无数据。</summary>
    public static EmptyStateKind ForList(bool hasItems, bool hasQuery, bool hasFilter = false) =>
        hasItems ? EmptyStateKind.None : hasQuery || hasFilter ? EmptyStateKind.NoMatch : EmptyStateKind.NoData;

    /// <summary>分类树空态:树恒有固定节点(「全部」「未分类」),只有搜索过滤后才可能为空,故空态恒为「无匹配」。</summary>
    public static EmptyStateKind ForCategoryTree(bool hasNodes) =>
        hasNodes ? EmptyStateKind.None : EmptyStateKind.NoMatch;
}
