using Mew.Workbench;
using Xunit;

namespace Mew.Launcher.Tests;

/// <summary>
/// 空状态判定纯逻辑:启动项列表区分「无数据/无匹配」,分类树空态恒为「无匹配」。
/// </summary>
public class EmptyStateTests
{
    [Fact]
    public void ForList_有内容_无空态()
    {
        Assert.Equal(EmptyStateKind.None, EmptyState.ForList(hasItems: true, hasQuery: false));
        Assert.Equal(EmptyStateKind.None, EmptyState.ForList(hasItems: true, hasQuery: true));
    }

    [Fact]
    public void ForList_无内容且无搜索词_无数据()
    {
        Assert.Equal(EmptyStateKind.NoData, EmptyState.ForList(hasItems: false, hasQuery: false));
    }

    [Fact]
    public void ForList_无内容但有搜索词_无匹配()
    {
        Assert.Equal(EmptyStateKind.NoMatch, EmptyState.ForList(hasItems: false, hasQuery: true));
    }

    [Fact]
    public void ForList_无内容但有类型过滤_无匹配()
    {
        Assert.Equal(EmptyStateKind.NoMatch, EmptyState.ForList(hasItems: false, hasQuery: false, hasFilter: true));
        Assert.Equal(EmptyStateKind.NoMatch, EmptyState.ForList(hasItems: false, hasQuery: true, hasFilter: true));
    }

    [Fact]
    public void ForCategoryTree_有节点_无空态()
    {
        Assert.Equal(EmptyStateKind.None, EmptyState.ForCategoryTree(hasNodes: true));
    }

    [Fact]
    public void ForCategoryTree_无节点_无匹配()
    {
        Assert.Equal(EmptyStateKind.NoMatch, EmptyState.ForCategoryTree(hasNodes: false));
    }
}
