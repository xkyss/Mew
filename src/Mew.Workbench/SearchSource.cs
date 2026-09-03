using Aprillz.MewUI;

namespace Mew.Workbench;

/// <summary>
/// 浮层搜索结果:标题 + 副标题 + 图标 + 激活动作。
/// 行渲染由框架统一(图标 + 主行 + 副行),契约不含自定义行渲染。
/// </summary>
public sealed record SearchResult(string Title, string Subtitle, ImageSource? Icon, Action Activate);

/// <summary>
/// 浮层搜索源契约:工具模块经宿主注册;多源结果扁平混排 + 行尾来源标记,
/// 唯一搜索源时浮层行为与单源时代一致。
/// </summary>
public interface ISearchSource
{
    /// <summary>稳定 Id,用于来源标记与后续冲突检测。</summary>
    string Id { get; }

    /// <summary>显示名,多源混排时行尾来源标记使用。</summary>
    string DisplayName { get; }

    /// <summary>按查询返回至多 maxResults 条结果;空查询的放行规则由各源自行决定。</summary>
    IReadOnlyList<SearchResult> Search(string query, int maxResults);
}

/// <summary>聚合后的浮层条目:结果连同其来源,供行渲染打来源标记。</summary>
public sealed record OverlayResultEntry(ISearchSource Source, SearchResult Result);
