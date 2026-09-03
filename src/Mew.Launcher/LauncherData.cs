namespace Mew.Launcher;

/// <summary>
/// 数据层纯逻辑(不依赖 UI 与文件):旧格式迁移、分类树子树聚合、未分类聚合、悬空引用归一。
/// 是 LauncherStore 与单元测试共用的唯一逻辑出口。
/// </summary>
public static class LauncherData
{
    /// <summary>
    /// 迁移旧平铺结构:旧分类文本 → 根级分类(按首次出现顺序生成唯一 slug id),「默认」/空分类 → 未分类(null);
    /// 输入为旧模型快照 <see cref="LegacyItem"/>(含分类文本字段)。
    /// </summary>
    public static (List<LauncherCategory> Categories, List<LauncherItem> Items) MigrateLegacy(List<LegacyItem> legacy)
    {
        var categories = new List<LauncherCategory>();
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in legacy)
        {
            var name = item.Category?.Trim();
            if (string.IsNullOrEmpty(name) || name == "默认")
            {
                continue;
            }

            if (!byName.ContainsKey(name))
            {
                byName[name] = $"cat-{categories.Count + 1}";
                categories.Add(new LauncherCategory(byName[name], name, []));
            }
        }

        var items = legacy
            .Select(item =>
            {
                var name = item.Category?.Trim();
                var categoryId = !string.IsNullOrEmpty(name) && byName.TryGetValue(name, out var id) ? id : null;
                return new LauncherItem(
                    item.Id ?? "item-" + Guid.NewGuid().ToString("N")[..8],
                    item.Name ?? "",
                    item.Command ?? "",
                    item.Args,
                    item.WorkingDirectory,
                    categoryId is not null ? [categoryId] : [],
                    item.Icon,
                    item.Hotkey);
            })
            .ToList();

        return (categories, items);
    }

    /// <summary>
    /// 子树聚合:返回归属指定分类或其任一子孙分类的启动项(不含未分类)。
    /// </summary>
    public static List<LauncherItem> AggregateSubtree(
        List<LauncherCategory> categories, List<LauncherItem> items, string categoryId)
    {
        var ids = SubtreeIds(categories, categoryId);
        return items.Where(i => i.CategoryIds is { Count: > 0 } && ids.Overlaps(i.CategoryIds)).ToList();
    }

    /// <summary>未分类聚合:返回归属列表为空(null / 空数组 / 全空白)的启动项。</summary>
    public static List<LauncherItem> Uncategorized(List<LauncherItem> items) =>
        items.Where(i => i.CategoryIds is not { Count: > 0 } || i.CategoryIds.All(string.IsNullOrWhiteSpace)).ToList();

    /// <summary>归一悬空引用:剔除指向不存在的分类的 id;null / 空 / 全部悬空 → 空数组(归未分类)。</summary>
    public static List<LauncherItem> NormalizeCategoryRefs(
        List<LauncherCategory> categories, List<LauncherItem> items)
    {
        var known = new HashSet<string>(FlattenIds(categories), StringComparer.Ordinal);
        return items
            .Select(i => i with { CategoryIds = (i.CategoryIds ?? []).Where(known.Contains).ToList() })
            .ToList();
    }

    /// <summary>
    /// 摘除归属:从所有启动项上移除指定分类 id 集合(删除分类时为该分类整棵子树的 id);
    /// 项还有其他归属则保留,一个归属都不剩则保持空归属(归未分类)。永不删项。
    /// </summary>
    public static List<LauncherItem> DetachCategoryIds(List<LauncherItem> items, IEnumerable<string> categoryIds)
    {
        var removed = new HashSet<string>(categoryIds, StringComparer.Ordinal);
        return items
            .Select(i => i.CategoryIds is { Count: > 0 } && i.CategoryIds.Any(removed.Contains)
                ? i with { CategoryIds = i.CategoryIds.Where(id => !removed.Contains(id)).ToList() }
                : i)
            .ToList();
    }

    /// <summary>分类树中所有节点 id(扁平,含子孙)。</summary>
    public static IEnumerable<string> FlattenIds(List<LauncherCategory> categories)
    {
        foreach (var category in categories)
        {
            yield return category.Id;
            if (category.Children is not null)
            {
                foreach (var id in FlattenIds(category.Children))
                {
                    yield return id;
                }
            }
        }
    }

    /// <summary>指定分类及其所有子孙的 id 集合。</summary>
    public static HashSet<string> SubtreeIds(List<LauncherCategory> categories, string categoryId)
    {
        var node = Find(categories, categoryId);
        if (node is null)
        {
            return [];
        }

        return new HashSet<string>(FlattenIds([node]), StringComparer.Ordinal);
    }

    /// <summary>按 id 查找分类节点(深度优先,整个树)。</summary>
    public static LauncherCategory? Find(List<LauncherCategory> categories, string categoryId)
    {
        foreach (var category in categories)
        {
            if (category.Id == categoryId)
            {
                return category;
            }

            if (category.Children is not null && Find(category.Children, categoryId) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>分类 id → 显示名(UI 兼容字段填充用);找不到返回 null。</summary>
    public static string? CategoryName(List<LauncherCategory> categories, string? categoryId)
    {
        if (string.IsNullOrEmpty(categoryId))
        {
            return null;
        }

        return Find(categories, categoryId)?.Name;
    }

    /// <summary>归一分类树:Children 为 null 的节点补空表(手写 JSON 缺省)。</summary>
    public static List<LauncherCategory> NormalizeTree(List<LauncherCategory> categories) =>
        categories
            .Select(c => c with { Children = NormalizeTree(c.Children ?? []) })
            .ToList();

    /// <summary>导航树固定节点 id:「全部」与「未分类」。</summary>
    public const string AllNavId = "__all__";
    public const string UncategorizedNavId = "__uncat__";

    /// <summary>构建侧边栏导航树:「全部」置顶、分类树居中(按树结构映射)、「未分类」收尾;固定节点不可管理。</summary>
    public static List<CategoryTreeNode> BuildNavTree(List<LauncherCategory> categories)
    {
        var nodes = categories.Select(ToNavNode).ToList();
        return
        [
            new CategoryTreeNode(AllNavId, "全部", true, []),
            .. nodes,
            new CategoryTreeNode(UncategorizedNavId, "未分类", true, []),
        ];
    }

    /// <summary>按导航节点聚合:null/「全部」→ 所有项;「未分类」→ 无分类项;分类 id → 子树聚合(含子孙)。</summary>
    public static List<LauncherItem> AggregateForNav(
        List<LauncherCategory> categories, List<LauncherItem> items, string? navId)
    {
        if (navId is null or AllNavId)
        {
            return items.ToList();
        }

        return navId == UncategorizedNavId
            ? Uncategorized(items)
            : AggregateSubtree(categories, items, navId);
    }

    /// <summary>新建启动项的归属决策:在「全部」/「未分类」下新建 → 空数组(未分类);在分类节点下新建 → 该分类 id。</summary>
    public static List<string> CategoryIdsForNewItem(string navId) =>
        navId is AllNavId or UncategorizedNavId ? [] : [navId];

    /// <summary>「在侧边栏定位」的归属决策:按树序(深度优先先序)取启动项所属的第一个分类节点 id;无有效归属返回 null(定位「未分类」)。</summary>
    public static string? FirstCategoryIdInTreeOrder(List<LauncherCategory> categories, IEnumerable<string> categoryIds)
    {
        var owned = new HashSet<string>(categoryIds, StringComparer.Ordinal);
        foreach (var category in categories)
        {
            if (FirstOwnedIdInSubtree(category, owned) is { } id)
            {
                return id;
            }
        }

        return null;
    }

    private static string? FirstOwnedIdInSubtree(LauncherCategory category, HashSet<string> owned)
    {
        if (owned.Contains(category.Id))
        {
            return category.Id;
        }

        foreach (var child in category.Children ?? [])
        {
            if (FirstOwnedIdInSubtree(child, owned) is { } id)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>启动类型维度:URL / 程序,由命令推导,不落盘存储;命令是唯一事实来源。</summary>
    public enum ItemKind
    {
        Url,
        Program,
    }

    /// <summary>推导启动类型:http(s):// 开头的命令为 URL,其余(含 .bat/.cmd 脚本)为程序。</summary>
    public static ItemKind KindOf(string command) =>
        command.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || command.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? ItemKind.Url
            : ItemKind.Program;

    /// <summary>分类搜索过滤:空查询返回原树;非空时隐藏固定节点(「全部」「未分类」),保留匹配节点(整棵子树)及其父链。</summary>
    public static List<CategoryTreeNode> FilterNavTree(List<CategoryTreeNode> nav, string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return nav;
        }

        var result = new List<CategoryTreeNode>();
        foreach (var node in nav)
        {
            if (node.IsFixed)
            {
                continue;
            }

            if (FilterNode(node, query) is { } filtered)
            {
                result.Add(filtered);
            }
        }

        return result;
    }

    private static CategoryTreeNode? FilterNode(CategoryTreeNode node, string query)
    {
        if (node.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return node; // 命中:整棵子树保留
        }

        var children = new List<CategoryTreeNode>();
        foreach (var child in node.Children)
        {
            if (FilterNode(child, query) is { } filtered)
            {
                children.Add(filtered);
            }
        }

        return children.Count > 0 ? node with { Children = children } : null;
    }

    /// <summary>连根删:移除指定分类节点(含其整棵子树),其余节点原样。</summary>
    public static List<LauncherCategory> RemoveCategoryNode(List<LauncherCategory> categories, string categoryId)
    {
        var result = new List<LauncherCategory>();
        foreach (var category in categories)
        {
            if (category.Id == categoryId)
            {
                continue;
            }

            result.Add(category.Children is { Count: > 0 }
                ? category with { Children = RemoveCategoryNode(category.Children, categoryId) }
                : category);
        }

        return result;
    }

    /// <summary>在指定父分类下追加子分类;父分类不存在时原样返回。</summary>
    public static List<LauncherCategory> AddCategoryNode(
        List<LauncherCategory> categories, string parentId, LauncherCategory newNode)
    {
        var result = new List<LauncherCategory>();
        foreach (var category in categories)
        {
            if (category.Id == parentId)
            {
                var children = new List<LauncherCategory>(category.Children ?? []) { newNode };
                result.Add(category with { Children = children });
                continue;
            }

            result.Add(category.Children is { Count: > 0 }
                ? category with { Children = AddCategoryNode(category.Children, parentId, newNode) }
                : category);
        }

        return result;
    }

    /// <summary>重命名指定分类节点,其余节点不动。</summary>
    public static List<LauncherCategory> RenameCategoryNode(
        List<LauncherCategory> categories, string categoryId, string newName)
    {
        var result = new List<LauncherCategory>();
        foreach (var category in categories)
        {
            if (category.Id == categoryId)
            {
                result.Add(category with { Name = newName });
            }
            else
            {
                result.Add(category.Children is { Count: > 0 }
                    ? category with { Children = RenameCategoryNode(category.Children, categoryId, newName) }
                    : category);
            }
        }

        return result;
    }

    /// <summary>详情表单分类选择器的选项:未分类置顶,分类节点按「父 / 子」路径展示。</summary>
    public static List<CategoryOption> FlattenCategoryOptions(List<LauncherCategory> categories)
    {
        var options = new List<CategoryOption> { new(null, "未分类") };
        foreach (var category in categories)
        {
            FlattenInto(category, "", options);
        }

        return options;
    }

    private static void FlattenInto(LauncherCategory category, string prefix, List<CategoryOption> options)
    {
        var path = prefix.Length == 0 ? category.Name : prefix + " / " + category.Name;
        options.Add(new CategoryOption(category.Id, path));
        foreach (var child in category.Children ?? [])
        {
            FlattenInto(child, path, options);
        }
    }

    private static CategoryTreeNode ToNavNode(LauncherCategory category) => new(
        category.Id,
        category.Name,
        false,
        (category.Children ?? []).Select(ToNavNode).ToList());
}

/// <summary>侧边栏导航树节点:固定节点(「全部」「未分类」)或分类节点(映射自分类树)。</summary>
public sealed record CategoryTreeNode(string Id, string Name, bool IsFixed, List<CategoryTreeNode> Children);

/// <summary>详情表单分类选择器选项:id 为 null 表示未分类。</summary>
public sealed record CategoryOption(string? Id, string Path);

/// <summary>旧模型快照(v0.1.x 平铺数组启动项,含分类文本字段),仅作为迁移输入。</summary>
public sealed record LegacyItem(
    string? Id,
    string? Name,
    string? Command,
    string? Args = null,
    string? WorkingDirectory = null,
    string? Category = null,
    string? Icon = null,
    string? Hotkey = null);
