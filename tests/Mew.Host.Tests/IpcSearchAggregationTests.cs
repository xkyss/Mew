using Mew.Workbench;
using Mew.Workbench.Ipc;
using Xunit;

namespace Mew.Host.Tests;

public class IpcSearchAggregationTests
{
    [Fact]
    public void Register_协议版本匹配_成功_不匹配拒绝()
    {
        var server = new IpcServer();
        var src = new FakeSource("a", "A", _ => []);
        Assert.True(server.RegisterInMemoryClient("a", "A", 1, new PluginCapabilitiesDto(new SearchCapabilityDto("a", "A"), null, null), src, out var err));
        Assert.Null(err);

        var src2 = new FakeSource("b", "B", _ => []);
        Assert.False(server.RegisterInMemoryClient("b", "B", 999, new PluginCapabilitiesDto(new SearchCapabilityDto("b", "B"), null, null), src2, out err));
        Assert.Contains("协议版本", err!);
    }

    [Fact]
    public void Register_重复id_后者拒绝()
    {
        var server = new IpcServer();
        var src1 = new FakeSource("dup", "Dup", _ => []);
        var src2 = new FakeSource("dup", "Dup2", _ => []);
        Assert.True(server.RegisterInMemoryClient("dup", "Dup", 1, new PluginCapabilitiesDto(new SearchCapabilityDto("dup", "Dup"), null, null), src1, out _));
        Assert.False(server.RegisterInMemoryClient("dup", "Dup2", 1, new PluginCapabilitiesDto(new SearchCapabilityDto("dup", "Dup2"), null, null), src2, out var err));
        Assert.Contains("id 重复", err!);
    }

    [Fact]
    public void Register_未声明search能力_注册搜索源被拒绝()
    {
        var server = new IpcServer();
        var src = new FakeSource("x", "X", _ => []);
        Assert.False(server.RegisterInMemoryClient("x", "X", 1, new PluginCapabilitiesDto(null, null, null), src, out var err));
        Assert.Contains("未声明 search", err!);
    }

    [Fact]
    public void Search_多源_扁平混排_每源上限_全局上限_来源标记()
    {
        var server = new IpcServer();
        // 3 源，每源返回 10 条，globalCap=8 => perSourceMax=2 (8/3=2), 总数 6  <= globalCap
        server.RegisterInMemoryClient("a", "Alpha", 1, cap("a"), new FakeSource("a", "Alpha", max => Enumerable.Range(0, 10).Select(i => FakeResult($"A{i}")).ToList()), out _);
        server.RegisterInMemoryClient("b", "Beta", 1, cap("b"), new FakeSource("b", "Beta", max => Enumerable.Range(0, 10).Select(i => FakeResult($"B{i}")).ToList()), out _);
        server.RegisterInMemoryClient("c", "Gamma", 1, cap("c"), new FakeSource("c", "Gamma", max => Enumerable.Range(0, 10).Select(i => FakeResult($"C{i}")).ToList()), out _);

        var entries = server.Search("", globalCap: 8);
        // 扁平混排：A0,A1,B0,B1,C0,C1 顺序（按注册顺序）
        Assert.Equal(6, entries.Count);
        Assert.Equal("a", entries[0].Source.Id);
        Assert.Equal("Alpha", entries[0].Source.DisplayName);
        Assert.Equal("b", entries[2].Source.Id);
        Assert.Equal("c", entries[4].Source.Id);
        // 每源上限验证：每源不超过 2
        Assert.Equal(2, entries.Count(e => e.Source.Id == "a"));
        Assert.Equal(2, entries.Count(e => e.Source.Id == "b"));
        Assert.Equal(2, entries.Count(e => e.Source.Id == "c"));
    }

    [Fact]
    public void Search_单源_行数与v0_2_0一致()
    {
        var server = new IpcServer();
        server.RegisterInMemoryClient("solo", "Solo", 1, cap("solo"), new FakeSource("solo", "Solo", max => Enumerable.Range(0, 20).Select(i => FakeResult($"R{i}")).ToList()), out _);
        var entries = server.Search("", globalCap: 8);
        Assert.Equal(8, entries.Count);
        Assert.All(entries, e => Assert.Equal("solo", e.Source.Id));
    }

    [Fact]
    public void Search_全局截断_多源大数据_不超过全局上限()
    {
        var server = new IpcServer();
        // 2 源，每源 10 条，global=8 => perSourceMax=4, 总 8
        server.RegisterInMemoryClient("a", "A", 1, cap("a"), new FakeSource("a", "A", max => Enumerable.Range(0, 10).Select(i => FakeResult($"A{i}")).ToList()), out _);
        server.RegisterInMemoryClient("b", "B", 1, cap("b"), new FakeSource("b", "B", max => Enumerable.Range(0, 10).Select(i => FakeResult($"B{i}")).ToList()), out _);
        var entries = server.Search("", globalCap: 8);
        Assert.Equal(8, entries.Count);
        Assert.Equal(4, entries.Count(e => e.Source.Id == "a"));
        Assert.Equal(4, entries.Count(e => e.Source.Id == "b"));
    }

    [Fact]
    public void Search_空源_返回空()
    {
        var server = new IpcServer();
        var entries = server.Search("", globalCap: 8);
        Assert.Empty(entries);
    }

    private static PluginCapabilitiesDto cap(string id) => new(new SearchCapabilityDto(id, id), null, null);
    private static SearchResult FakeResult(string title) => new(title, "sub", null, () => { });

    private sealed class FakeSource : ISearchSource
    {
        private readonly Func<int, IReadOnlyList<SearchResult>> _fn;
        public FakeSource(string id, string displayName, Func<int, IReadOnlyList<SearchResult>> fn) { Id = id; DisplayName = displayName; _fn = fn; }
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<SearchResult> Search(string query, int maxResults) => _fn(maxResults).Take(maxResults).ToList();
    }
}
