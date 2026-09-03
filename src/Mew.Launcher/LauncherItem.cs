namespace Mew.Launcher;

/// <summary>
/// 启动管理器管理的单个可启动条目(程序、脚本或 URL),含名称、命令、简介与可选的参数、工作目录、分类归属、图标与每项热键。
/// CategoryIds 可空或空 = 未分类;分类显示名由数据层按分类树解析。简介(Description)与命令(Command)是不同概念:
/// 命令是实际启动的指令,简介用于列表/卡片展示的说明文字。
/// </summary>
public sealed record LauncherItem(
    string Id,
    string Name,
    string Command,
    string? Args = null,
    string? WorkingDirectory = null,
    List<string>? CategoryIds = null,
    string? Icon = null,
    string? Hotkey = null,
    string? Description = null);
