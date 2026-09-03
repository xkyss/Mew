using Mew.Launcher;
using Xunit;

namespace Mew.Launcher.Tests;

/// <summary>
/// 数据层纯逻辑:旧格式迁移、分类树子树聚合、未分类聚合、悬空引用归一。
/// </summary>
public class LauncherDataTests
{
    // ── 迁移 ──────────────────────────────────────────────

    [Fact]
    public void MigrateLegacy_中文分类_生成根级分类并引用()
    {
        var legacy = new List<LegacyItem>
        {
            new("vs-code", "VS Code", "code", Category: "开发"),
            new("steam", "Steam", "steam", Category: "游戏"),
            new("github", "GitHub", "https://github.com", Category: "工具"),
        };

        var (categories, items) = LauncherData.MigrateLegacy(legacy);

        Assert.Equal(3, categories.Count);
        Assert.Equal("开发", categories[0].Name);
        Assert.Equal("游戏", categories[1].Name);
        Assert.Equal("工具", categories[2].Name);
        Assert.All(categories, c => Assert.Matches("^[a-z0-9-]+$", c.Id)); // 唯一 slug
        Assert.Equal(categories[0].Id, items[0].CategoryIds!.Single());
        Assert.Equal(categories[1].Id, items[1].CategoryIds!.Single());
        Assert.Equal(categories[2].Id, items[2].CategoryIds!.Single());
    }

    [Fact]
    public void MigrateLegacy_默认与空分类_归未分类()
    {
        var legacy = new List<LegacyItem>
        {
            new("a", "A", "a", Category: "默认"),
            new("b", "B", "b", Category: ""),
        };

        var (categories, items) = LauncherData.MigrateLegacy(legacy);

        Assert.Empty(categories);
        Assert.All(items, i => Assert.Empty(i.CategoryIds!));
    }

    [Fact]
    public void MigrateLegacy_空列表_产出空结构()
    {
        var (categories, items) = LauncherData.MigrateLegacy([]);

        Assert.Empty(categories);
        Assert.Empty(items);
    }

    [Fact]
    public void MigrateLegacy_缺字段_归一默认值()
    {
        var legacy = new List<LegacyItem> { new(null, null, null) };

        var (_, items) = LauncherData.MigrateLegacy(legacy);

        Assert.Single(items);
        Assert.False(string.IsNullOrEmpty(items[0].Id));
        Assert.Equal("", items[0].Name);
        Assert.Equal("", items[0].Command);
        Assert.Empty(items[0].CategoryIds!);
    }

    [Fact]
    public void MigrateLegacy_重复分类_只建一次()
    {
        var legacy = new List<LegacyItem>
        {
            new("a", "A", "a", Category: "开发"),
            new("b", "B", "b", Category: "开发"),
        };

        var (categories, items) = LauncherData.MigrateLegacy(legacy);

        Assert.Single(categories);
        Assert.All(items, i => Assert.Equal(categories[0].Id, i.CategoryIds!.Single()));
    }

    // ── 聚合 ──────────────────────────────────────────────

    [Fact]
    public void AggregateSubtree_父分类_返回子孙所有项()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
        };
        var items = new List<LauncherItem>
        {
            new("launcher", "启动器", "mew", CategoryIds: ["games"]),
            new("steam", "Steam", "steam", CategoryIds: ["games-steam"]),
            new("github", "GitHub", "https://github.com", CategoryIds: null),
        };

        var result = LauncherData.AggregateSubtree(categories, items, "games");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, i => i.Id == "launcher");
        Assert.Contains(result, i => i.Id == "steam");
    }

    [Fact]
    public void AggregateSubtree_叶子分类_只返回直接项()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
        };
        var items = new List<LauncherItem>
        {
            new("launcher", "启动器", "mew", CategoryIds: ["games"]),
            new("steam", "Steam", "steam", CategoryIds: ["games-steam"]),
        };

        var result = LauncherData.AggregateSubtree(categories, items, "games-steam");

        Assert.Single(result);
        Assert.Equal("steam", result[0].Id);
    }

    [Fact]
    public void AggregateSubtree_多归属_命中任一所属节点()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
            new("tools", "工具", []),
        };
        var items = new List<LauncherItem>
        {
            new("steam", "Steam", "steam", CategoryIds: ["games-steam", "tools"]),
        };

        Assert.Contains(LauncherData.AggregateSubtree(categories, items, "games"), i => i.Id == "steam");
        Assert.Contains(LauncherData.AggregateSubtree(categories, items, "tools"), i => i.Id == "steam");
        Assert.DoesNotContain(LauncherData.AggregateSubtree(categories, items, "games-epic"), i => i.Id == "steam");
    }

    [Fact]
    public void Uncategorized_返回归属为空的项()
    {
        var items = new List<LauncherItem>
        {
            new("a", "A", "a", CategoryIds: null),
            new("b", "B", "b", CategoryIds: ["games"]),
            new("c", "C", "c", CategoryIds: [" "]),
        };

        var result = LauncherData.Uncategorized(items);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, i => i.Id == "a");
        Assert.Contains(result, i => i.Id == "c");
    }

    // ── 悬空归一 ──────────────────────────────────────────

    [Fact]
    public void NormalizeCategoryRefs_悬空引用_归未分类()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", []),
        };
        var items = new List<LauncherItem>
        {
            new("a", "A", "a", CategoryIds: ["games"]),
            new("b", "B", "b", CategoryIds: ["ghost"]),
        };

        var result = LauncherData.NormalizeCategoryRefs(categories, items);

        Assert.Equal("games", result[0].CategoryIds!.Single());
        Assert.Empty(result[1].CategoryIds!);
    }

    [Fact]
    public void NormalizeCategoryRefs_部分悬空_剔除保留其余()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", []),
        };
        var items = new List<LauncherItem>
        {
            new("a", "A", "a", CategoryIds: ["games", "ghost"]),
        };

        var result = LauncherData.NormalizeCategoryRefs(categories, items);

        Assert.Equal("games", result[0].CategoryIds!.Single());
    }

    // ── 新建启动项归属 ─────────────────────────────────────

    [Fact]
    public void CategoryIdsForNewItem_固定节点归未分类_分类节点保持()
    {
        Assert.Empty(LauncherData.CategoryIdsForNewItem(LauncherData.AllNavId));
        Assert.Empty(LauncherData.CategoryIdsForNewItem(LauncherData.UncategorizedNavId));
        Assert.Equal("games", LauncherData.CategoryIdsForNewItem("games").Single());
    }

    // ── 侧边栏定位(树序首个归属) ──────────────────────────

    [Fact]
    public void FirstCategoryIdInTreeOrder_按树序取首个而非数组序()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
            new("tools", "工具", []),
        };

        // 数组序 tools 在前,但树序 games 在前 → 取 games
        Assert.Equal("games", LauncherData.FirstCategoryIdInTreeOrder(categories, ["tools", "games"]));
        Assert.Equal("games-steam", LauncherData.FirstCategoryIdInTreeOrder(categories, ["tools", "games-steam"]));
    }

    [Fact]
    public void FirstCategoryIdInTreeOrder_无有效归属_返回null()
    {
        var categories = new List<LauncherCategory> { new("games", "游戏", []) };

        Assert.Null(LauncherData.FirstCategoryIdInTreeOrder(categories, []));
        Assert.Null(LauncherData.FirstCategoryIdInTreeOrder(categories, ["ghost"]));
    }

    // ── 启动类型推导 ───────────────────────────────────────

    [Theory]
    [InlineData("https://github.com", LauncherData.ItemKind.Url)]
    [InlineData("http://example.com", LauncherData.ItemKind.Url)]
    [InlineData("HTTP://EXAMPLE.COM", LauncherData.ItemKind.Url)]
    [InlineData("code", LauncherData.ItemKind.Program)]
    [InlineData("C:\\tools\\run.bat", LauncherData.ItemKind.Program)] // 脚本并入程序
    [InlineData("C:\\tools\\run.cmd", LauncherData.ItemKind.Program)]
    [InlineData("", LauncherData.ItemKind.Program)]
    public void KindOf_命令推导类型(string command, LauncherData.ItemKind expected)
    {
        Assert.Equal(expected, LauncherData.KindOf(command));
    }

    // ── 摘除归属(删除分类) ────────────────────────────────

    [Fact]
    public void DetachCategoryIds_移除指定id_其他归属保留()
    {
        var items = new List<LauncherItem>
        {
            new("a", "A", "a", CategoryIds: ["games", "tools"]),
        };

        var result = LauncherData.DetachCategoryIds(items, ["games"]);

        Assert.Equal("tools", result[0].CategoryIds!.Single());
    }

    [Fact]
    public void DetachCategoryIds_一个不剩_归未分类且永不删项()
    {
        var items = new List<LauncherItem>
        {
            new("a", "A", "a", CategoryIds: ["games"]),
            new("b", "B", "b", CategoryIds: null),
        };

        var result = LauncherData.DetachCategoryIds(items, ["games"]);

        Assert.Equal(2, result.Count); // 永不删项
        Assert.Empty(result[0].CategoryIds!);
        Assert.Null(result[1].CategoryIds); // 原未分类项不受影响
    }

    [Fact]
    public void DetachCategoryIds_子树id集合_一并摘除()
    {
        var items = new List<LauncherItem>
        {
            new("a", "A", "a", CategoryIds: ["games", "games-steam"]),
            new("b", "B", "b", CategoryIds: ["games-steam", "tools"]),
            new("c", "C", "c", CategoryIds: ["tools"]),
        };

        var result = LauncherData.DetachCategoryIds(items, ["games", "games-steam"]);

        Assert.Empty(result[0].CategoryIds!);
        Assert.Equal("tools", result[1].CategoryIds!.Single());
        Assert.Equal("tools", result[2].CategoryIds!.Single());
    }

    // ── 分类搜索过滤 ───────────────────────────────────────

    [Fact]
    public void FilterNavTree_空查询_返回原树()
    {
        var nav = LauncherData.BuildNavTree([new LauncherCategory("games", "游戏", [])]);

        var result = LauncherData.FilterNavTree(nav, "");

        Assert.Equal(nav.Count, result.Count); // 含固定节点
    }

    [Fact]
    public void FilterNavTree_命中节点_整棵子树保留_隐藏固定节点()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
            new("tools", "工具", []),
        };
        var nav = LauncherData.BuildNavTree(categories);

        var result = LauncherData.FilterNavTree(nav, "游");

        Assert.Single(result);
        Assert.Equal("games", result[0].Id);
        Assert.Equal("games-steam", result[0].Children[0].Id); // 命中节点整棵子树保留
    }

    [Fact]
    public void FilterNavTree_命中子分类_父链保留()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
        };
        var nav = LauncherData.BuildNavTree(categories);

        var result = LauncherData.FilterNavTree(nav, "steam");

        Assert.Single(result);
        Assert.Equal("games", result[0].Id);
        Assert.Single(result[0].Children);
        Assert.Equal("games-steam", result[0].Children[0].Id);
    }

    [Fact]
    public void FilterNavTree_无匹配_返回空()
    {
        var nav = LauncherData.BuildNavTree([new LauncherCategory("games", "游戏", [])]);

        var result = LauncherData.FilterNavTree(nav, "不存在");

        Assert.Empty(result);
    }

    // ── 分类树增删改 ───────────────────────────────────────

    [Fact]
    public void RemoveCategoryNode_根分类_连子分类一起删()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
            new("tools", "工具", []),
        };

        var result = LauncherData.RemoveCategoryNode(categories, "games");

        Assert.Single(result);
        Assert.Equal("tools", result[0].Id);
    }

    [Fact]
    public void RemoveCategoryNode_子分类_父分类保留其余子节点()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [
                new LauncherCategory("games-steam", "Steam", []),
                new LauncherCategory("games-epic", "Epic", []),
            ]),
        };

        var result = LauncherData.RemoveCategoryNode(categories, "games-steam");

        Assert.Single(result);
        Assert.Single(result[0].Children!);
        Assert.Equal("games-epic", result[0].Children![0].Id);
    }

    [Fact]
    public void RemoveCategoryNode_不存在的id_原样返回()
    {
        var categories = new List<LauncherCategory> { new("games", "游戏", []) };

        var result = LauncherData.RemoveCategoryNode(categories, "ghost");

        Assert.Single(result);
    }

    [Fact]
    public void AddCategoryNode_加到父分类子节点()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
        };

        var result = LauncherData.AddCategoryNode(categories, "games", new LauncherCategory("games-epic", "Epic", []));

        Assert.Equal(2, result[0].Children!.Count);
        Assert.Equal("games-epic", result[0].Children![1].Id);
    }

    [Fact]
    public void AddCategoryNode_不存在的父_原样返回()
    {
        var categories = new List<LauncherCategory> { new("games", "游戏", []) };

        var result = LauncherData.AddCategoryNode(categories, "ghost", new LauncherCategory("x", "X", []));

        Assert.Single(result);
        Assert.Empty(result[0].Children!); // 原样返回:子节点仍为空
    }

    [Fact]
    public void RenameCategoryNode_改名_不动其他节点()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
        };

        var result = LauncherData.RenameCategoryNode(categories, "games-steam", "Steam 平台");

        Assert.Equal("游戏", result[0].Name);
        Assert.Equal("Steam 平台", result[0].Children![0].Name);
    }

    // ── 分类选择器选项 ─────────────────────────────────────

    [Fact]
    public void FlattenCategoryOptions_未分类置顶_子分类带路径()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
            new("tools", "工具", []),
        };

        var options = LauncherData.FlattenCategoryOptions(categories);

        Assert.Equal(4, options.Count);
        Assert.Null(options[0].Id);
        Assert.Equal("未分类", options[0].Path);
        Assert.Equal("游戏", options[1].Path);
        Assert.Equal("游戏 / Steam", options[2].Path);
        Assert.Equal("工具", options[3].Path);
    }

    // ── 导航树 ─────────────────────────────────────────────

    [Fact]
    public void BuildNavTree_空分类_返回全部与未分类固定节点()
    {
        var nav = LauncherData.BuildNavTree([]);

        Assert.Equal(2, nav.Count);
        Assert.True(nav[0].IsFixed);
        Assert.Equal(LauncherData.AllNavId, nav[0].Id);
        Assert.Equal("全部", nav[0].Name);
        Assert.True(nav[1].IsFixed);
        Assert.Equal(LauncherData.UncategorizedNavId, nav[1].Id);
        Assert.Equal("未分类", nav[1].Name);
    }

    [Fact]
    public void BuildNavTree_多级分类_全部置顶分类居中未分类收尾()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
            new("tools", "工具", []),
        };

        var nav = LauncherData.BuildNavTree(categories);

        Assert.Equal(4, nav.Count);
        Assert.Equal(LauncherData.AllNavId, nav[0].Id);
        Assert.Equal("games", nav[1].Id);
        Assert.False(nav[1].IsFixed);
        Assert.Equal("games-steam", nav[1].Children[0].Id);
        Assert.False(nav[1].Children[0].IsFixed);
        Assert.Equal("tools", nav[2].Id);
        Assert.Equal(LauncherData.UncategorizedNavId, nav[3].Id);
        Assert.True(nav[3].IsFixed);
    }

    [Fact]
    public void AggregateForNav_全部未分类与子树()
    {
        var categories = new List<LauncherCategory>
        {
            new("games", "游戏", [new LauncherCategory("games-steam", "Steam", [])]),
        };
        var items = new List<LauncherItem>
        {
            new("launcher", "启动器", "mew", CategoryIds: ["games"]),
            new("steam", "Steam", "steam", CategoryIds: ["games-steam"]),
            new("github", "GitHub", "https://github.com", CategoryIds: null),
        };

        Assert.Equal(3, LauncherData.AggregateForNav(categories, items, null).Count);
        Assert.Equal(3, LauncherData.AggregateForNav(categories, items, LauncherData.AllNavId).Count);
        Assert.Single(LauncherData.AggregateForNav(categories, items, LauncherData.UncategorizedNavId));
        Assert.Equal(2, LauncherData.AggregateForNav(categories, items, "games").Count);
    }
}
