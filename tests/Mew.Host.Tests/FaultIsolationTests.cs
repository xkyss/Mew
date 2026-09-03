using Mew.Workbench;
using Mew.Workbench.Ipc;
using Mew.Workbench.Plugins;
using Xunit;

namespace Mew.Host.Tests;

public class FaultIsolationTests
{
    [Fact]
    public void Search_独立插件崩溃_仅该源消失_不影响其他()
    {
        var server = new IpcServer();
        server.RegisterInMemoryClient("a", "A", 1, cap("a"), new FakeSource("a", "A", _ => [new SearchResult("A1", "sub", null, () => { })]), out _);
        server.RegisterInMemoryClient("b", "B", 1, cap("b"), new FakeSource("b", "B", _ => [new SearchResult("B1", "sub", null, () => { })]), out _);

        var before = server.Search("");
        Assert.Equal(2, before.Count);

        server.Unregister("a");

        var after = server.Search("");
        Assert.Single(after);
        Assert.Equal("b", after[0].Source.Id);
    }

    [Fact]
    public void ProtocolVersion不匹配_注册拒绝_搜索不包含()
    {
        var server = new IpcServer();
        var src = new FakeSource("bad", "Bad", _ => [new SearchResult("R", "sub", null, () => { })]);
        //  manifest 校验层已拒绝，此处模拟 IpcServer 层也拒绝
        Assert.False(server.RegisterInMemoryClient("bad", "Bad", 999, cap("bad"), src, out var err));
        Assert.Contains("协议版本", err!);
        Assert.Empty(server.Search(""));
    }

    [Fact]
    public void HostLog_可写入_用于诊断()
    {
        // 仅验证 IpcServer 的 ClientDisconnected 事件可被订阅用于日志与标为已崩溃
        var server = new IpcServer();
        var crashed = new List<string>();
        server.ClientDisconnected += id => crashed.Add(id);
        server.RegisterInMemoryClient("x", "X", 1, cap("x"), new FakeSource("x", "X", _ => []), out _);
        server.Unregister("x");
        Assert.Contains("x", crashed);
    }

    [Fact]
    public void Manifest_协议版本不匹配_校验失败()
    {
        var m = new PluginManifest
        {
            Id = "test",
            DisplayName = "Test",
            Version = "0.1.0",
            Entry = new PluginEntry { Type = "exe", Path = "a.exe" },
            ProtocolVersion = 999
        };
        var errors = m.Validate();
        Assert.Contains(errors, e => e.Contains("协议版本不匹配"));
    }

    private static PluginCapabilitiesDto cap(string id) => new(new SearchCapabilityDto(id, id), null, null);

    private sealed class FakeSource : ISearchSource
    {
        private readonly Func<int, IReadOnlyList<SearchResult>> _fn;
        public FakeSource(string id, string displayName, Func<int, IReadOnlyList<SearchResult>> fn) { Id = id; DisplayName = displayName; _fn = fn; }
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<SearchResult> Search(string query, int maxResults) => _fn(maxResults).Take(maxResults).ToList();
    }
}
