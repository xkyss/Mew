using System.Text.Json;
using Mew.Launcher;
using Xunit;

namespace Mew.Launcher.Tests;

/// <summary>
/// 数据文件读写:新结构往返、手写新格式可识别、旧格式首启迁移并写回新格式。
/// </summary>
public class LauncherStoreTests : IDisposable
{
    private readonly string _dir;

    public LauncherStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mew-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private string TempFile(string name) => Path.Combine(_dir, name);

    [Fact]
    public void SaveThenLoad_新结构往返一致()
    {
        var store = new LauncherStore(TempFile("launcher.json"));
        store.UpdateCategories(new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
        });
        var items = new List<LauncherItem>
        {
            new("steam", "Steam", "steam", CategoryIds: ["games-steam"], Description: "游戏平台"),
            new("github", "GitHub", "https://github.com", CategoryIds: null),
        };
        store.Save(items);

        var reloaded = new LauncherStore(TempFile("launcher.json"));
        var loaded = reloaded.Load();

        Assert.Equal(2, loaded.Count);
        var steam = loaded.Single(i => i.Id == "steam");
        Assert.Equal("games-steam", steam.CategoryIds!.Single());
        Assert.Equal("游戏平台", steam.Description);
        var github = loaded.Single(i => i.Id == "github");
        Assert.Empty(github.CategoryIds!);
        Assert.Equal("games-steam", reloaded.Categories[0].Children![0].Id);
    }

    [Fact]
    public void SaveThenLoad_多归属数组_往返保持()
    {
        var store = new LauncherStore(TempFile("launcher.json"));
        store.UpdateCategories(new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
            new("tools", "工具", []),
        });
        var items = new List<LauncherItem>
        {
            new("steam", "Steam", "steam", CategoryIds: ["games-steam", "tools"]),
        };
        store.Save(items);

        var loaded = new LauncherStore(TempFile("launcher.json")).Load();

        var steam = loaded.Single();
        Assert.Equal(2, steam.CategoryIds!.Count);
        Assert.Contains("games-steam", steam.CategoryIds);
        Assert.Contains("tools", steam.CategoryIds);
    }

    [Fact]
    public void Load_手写新格式JSON_未知字段忽略可选缺省()
    {
        var json = """
        {
          "categories": [
            { "id": "games", "name": "游戏", "children": [] }
          ],
          "items": [
            { "id": "steam", "name": "Steam", "command": "steam", "categoryId": "games", "futureField": "ignored" },
            { "id": "no-name", "command": "notepad" }
          ],
          "extraRoot": true
        }
        """;
        File.WriteAllText(TempFile("launcher.json"), json);

        var loaded = new LauncherStore(TempFile("launcher.json")).Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("games", loaded[0].CategoryIds!.Single()); // 旧字段 categoryId 单值 → 数组
        Assert.Equal("", loaded[1].Name); // 缺省归 "" 而非崩溃
        Assert.Empty(loaded[1].CategoryIds!); // 缺省归属字段 → 未分类
    }

    [Fact]
    public void Load_旧单值categoryId_迁移后写回数组格式()
    {
        var json = """
        {
          "categories": [ { "id": "games", "name": "游戏", "children": [] } ],
          "items": [ { "id": "steam", "name": "Steam", "command": "steam", "categoryId": "games" } ]
        }
        """;
        File.WriteAllText(TempFile("launcher.json"), json);

        var loaded = new LauncherStore(TempFile("launcher.json")).Load();

        Assert.Equal("games", loaded[0].CategoryIds!.Single());
        // 迁移后立即写回新格式:categoryIds 数组、旧字段省略
        var text = File.ReadAllText(TempFile("launcher.json"));
        Assert.Contains("\"categoryIds\"", text);
        Assert.DoesNotContain("\"categoryId\"", text);
    }

    [Fact]
    public void Load_旧格式数组_自动迁移并写回新格式()
    {
        var legacyJson = """
        [
          { "id": "vs-code", "name": "VS Code", "command": "code", "category": "开发" },
          { "id": "github", "name": "GitHub", "command": "https://github.com", "category": "默认" }
        ]
        """;
        File.WriteAllText(TempFile("launcher.json"), legacyJson);

        var store = new LauncherStore(TempFile("launcher.json"));
        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Single(store.Categories);
        Assert.Equal("开发", store.Categories[0].Name);
        Assert.Equal("cat-1", store.Categories[0].Id); // 迁移自动生成唯一 slug
        Assert.Empty(loaded[1].CategoryIds!); // 「默认」→ 未分类
        Assert.Equal("cat-1", loaded[0].CategoryIds!.Single()); // vs-code 归属迁移生成的分类

        // 已写回新格式:再次加载走新结构路径
        var json = File.ReadAllText(TempFile("launcher.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        var reloaded = new LauncherStore(TempFile("launcher.json")).Load();
        Assert.Equal(2, reloaded.Count);
    }

    [Fact]
    public void Load_无文件_生成默认数据并落盘新格式()
    {
        var store = new LauncherStore(TempFile("launcher.json"));
        var loaded = store.Load();

        Assert.NotEmpty(loaded);
        Assert.True(File.Exists(TempFile("launcher.json")));
        var json = File.ReadAllText(TempFile("launcher.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind); // 新结构,非数组
        Assert.NotEmpty(doc.RootElement.GetProperty("categories").EnumerateArray());
    }

    [Fact]
    public void Save_新建项默认归属_落盘并在分类下可见()
    {
        var store = new LauncherStore(TempFile("launcher.json"));
        store.UpdateCategories(new List<LauncherCategory> { new("games", "游戏", []) });
        var items = new List<LauncherItem>
        {
            new("a", "A", "a", CategoryIds: LauncherData.CategoryIdsForNewItem("games")),
            new("b", "B", "b", CategoryIds: LauncherData.CategoryIdsForNewItem(LauncherData.AllNavId)),
        };
        store.Save(items);

        // 落盘后读回:分类节点下新建 → 默认归属该分类;固定节点下新建 → 空归属(未分类)
        var loaded = new LauncherStore(TempFile("launcher.json")).Load();
        Assert.Equal("games", loaded.Single(i => i.Id == "a").CategoryIds!.Single());
        Assert.Empty(loaded.Single(i => i.Id == "b").CategoryIds!);

        // 数组形式落盘:文件里是 categoryIds,不是 categoryId
        var json = File.ReadAllText(TempFile("launcher.json"));
        Assert.Contains("\"categoryIds\"", json);
        Assert.DoesNotContain("\"categoryId\"", json);

        // 对应分类下可见:新项 a 在「游戏」聚合中,未分类项 b 不在
        Assert.Contains(LauncherData.AggregateSubtree(store.Categories.ToList(), loaded, "games"), i => i.Id == "a");
        Assert.DoesNotContain(LauncherData.AggregateSubtree(store.Categories.ToList(), loaded, "games"), i => i.Id == "b");
    }
}
