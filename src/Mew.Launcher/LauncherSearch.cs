namespace Mew.Launcher;

/// <summary>
/// 启动项搜索过滤:纯逻辑,不依赖 UI,侧边栏与呼出浮层共用。
/// </summary>
public static class LauncherSearch
{
    /// <summary>
    /// 查询是否匹配启动项:空查询放行全部;否则按名称、命令或简介做大小写不敏感的子串匹配。
    /// </summary>
    public static bool Matches(LauncherItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Command.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (item.Description ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
