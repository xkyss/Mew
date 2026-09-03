namespace Mew.Launcher;

/// <summary>
/// 分类树节点:唯一 slug id + 显示名称 + 子分类。启动项通过 <see cref="LauncherItem.CategoryIds"/> 引用节点(可多挂),
/// 移动/重命名分类不影响启动项数据。Children 为 null(手写 JSON 缺省)由数据层归一为空表。
/// </summary>
public sealed record LauncherCategory(
    string Id,
    string Name,
    List<LauncherCategory>? Children = null);
