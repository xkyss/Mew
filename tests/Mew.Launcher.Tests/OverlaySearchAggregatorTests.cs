using Mew.Workbench;
using Xunit;

namespace Mew.Launcher.Tests;

/// <summary>
/// 浮层多源聚合(纯逻辑,不涉窗口):扁平混排、每源上限、全局上限、来源标记。
/// </summary>
public class OverlaySearchAggregatorTests
{
    private static SearchResult Result(string title) => new(title, "sub", null, () => { });

    /// <summary>守约源:严格遵守 maxResults。</summary>
    private sealed class FakeSearchSource : ISearchSource
    {
        private readonly IReadOnlyList<string> _titles;

        public FakeSearchSource(string id, string displayName, IReadOnlyList<string> titles)
        {
            Id = id;
            DisplayName = displayName;
            _titles = titles;
        }

        public string Id { get; }
        public string DisplayName { get; }

        public IReadOnlyList<SearchResult> Search(string query, int maxResults) =>
            _titles.Select(Result).Take(maxResults).ToList();
    }

    /// <summary>违约源:无视 maxResults 返回全量,聚合侧须按每源上限兜底。</summary>
    private sealed class GreedySearchSource : ISearchSource
    {
        private readonly IReadOnlyList<string> _titles;

        public GreedySearchSource(IReadOnlyList<string> titles) => _titles = titles;

        public string Id { get; } = "greedy";
        public string DisplayName { get; } = "贪婪源";

        public IReadOnlyList<SearchResult> Search(string query, int maxResults) =>
            _titles.Select(Result).ToList();
    }

    [Fact]
    public void 单源_结果原样且按源顺序()
    {
        var source = new FakeSearchSource("a", "A源", Enumerable.Range(1, 8).Select(i => $"a{i}").ToList());

        var entries = OverlaySearchAggregator.Aggregate([source], "");

        Assert.Equal(8, entries.Count);
        Assert.All(entries, e => Assert.Same(source, e.Source));
        Assert.Equal("a1", entries[0].Result.Title);
        Assert.Equal("a8", entries[^1].Result.Title);
    }

    [Fact]
    public void 单源_全局上限截断()
    {
        var source = new FakeSearchSource("a", "A源", Enumerable.Range(1, 12).Select(i => $"a{i}").ToList());

        var entries = OverlaySearchAggregator.Aggregate([source], "");

        Assert.Equal(OverlaySearchAggregator.DefaultGlobalCap, entries.Count);
        Assert.Equal("a1", entries[0].Result.Title);
        Assert.Equal("a8", entries[^1].Result.Title);
    }

    [Fact]
    public void 双源_扁平混排_每源上限_来源标记()
    {
        var a = new FakeSearchSource("a", "A源", Enumerable.Range(1, 5).Select(i => $"a{i}").ToList());
        var b = new FakeSearchSource("b", "B源", Enumerable.Range(1, 5).Select(i => $"b{i}").ToList());

        var entries = OverlaySearchAggregator.Aggregate([a, b], "");

        // 每源 8/2=4,双源共 8 条,按源顺序混排,来源标记正确
        Assert.Equal(8, entries.Count);
        Assert.Equal(["a1", "a2", "a3", "a4", "b1", "b2", "b3", "b4"], entries.Select(e => e.Result.Title));
        Assert.All(entries.Take(4), e => Assert.Same(a, e.Source));
        Assert.All(entries.Skip(4), e => Assert.Same(b, e.Source));
    }

    [Fact]
    public void 双源_来源余量不足_不补位()
    {
        var a = new FakeSearchSource("a", "A源", new[] { "a1", "a2" });
        var b = new FakeSearchSource("b", "B源", new[] { "b1", "b2" });

        var entries = OverlaySearchAggregator.Aggregate([a, b], "");

        // 每源 4 上限,实际各 2 条;对方缺额不补位
        Assert.Equal(4, entries.Count);
        Assert.Equal(["a1", "a2", "b1", "b2"], entries.Select(e => e.Result.Title));
    }

    [Fact]
    public void 违约源_聚合按每源上限兜底()
    {
        var a = new FakeSearchSource("a", "A源", new[] { "a1" });
        var greedy = new GreedySearchSource(Enumerable.Range(1, 10).Select(i => $"g{i}").ToList());

        var entries = OverlaySearchAggregator.Aggregate([a, greedy], "");

        // a1 + g1..g4(每源 4 上限)
        Assert.Equal(5, entries.Count);
        Assert.Equal("g4", entries[^1].Result.Title);
    }

    [Fact]
    public void 零源_返回空()
    {
        Assert.Empty(OverlaySearchAggregator.Aggregate([], ""));
    }
}
