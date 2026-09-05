using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Mew.Workbench.Plugins;

/// <summary>插件管理面板一行的展示模型：描述符 + 行状态（ADR-000203 行标签契约）；标题格式由渲染器统一，调用方不再各自拼。</summary>
public sealed record PluginAdminRow(PluginDescriptor Descriptor, PluginRowState State)
{
    public string Id => Descriptor.Id;

    public string Title => $"{Descriptor.Manifest.DisplayName} ({Descriptor.Id}) v{Descriptor.Manifest.Version}";
}

/// <summary>行内动作的结果：成功 / 对端拒绝（含原因）/ 对端不可达。扩展主机经 IPC 时三态皆可出现，宿主进程内只有前两态。</summary>
public enum PluginAdminOutcome
{
    Ok,
    Rejected,
    Unreachable,
}

/// <summary>行内动作的结果值：Outcome + 可选错误说明（Rejected/Unreachable 时为面向用户的提示文案）。</summary>
public sealed record PluginAdminResult(PluginAdminOutcome Outcome, string? Error)
{
    public static PluginAdminResult Ok() => new(PluginAdminOutcome.Ok, null);

    public static PluginAdminResult Rejected(string error) => new(PluginAdminOutcome.Rejected, error);

    public static PluginAdminResult Unreachable(string error) => new(PluginAdminOutcome.Unreachable, error);
}

/// <summary>
/// 插件管理面板的管理缝：行来源 + 行内动作。宿主以进程内 adapter 实现（生命周期引擎 + plugins.json），
/// 扩展主机以 IPC adapter 实现（发现结果 + 本地只读缓存 + 一次性 IPC）。两个真实 adapter 坐实本 seam（ADR-000204）。
/// </summary>
public interface IPluginAdminService
{
    /// <summary>行视图模型集合；顺序由面板统一按 id 排序，adapter 不必关心。</summary>
    IReadOnlyList<PluginAdminRow> Snapshot();

    /// <summary>行内单动作（启用/禁用/重启）。动作语义由 adapter 落到所在进程的写路径。</summary>
    PluginAdminResult Apply(string id, PluginRowAction action);
}

/// <summary>
/// 插件管理面板（CONTEXT.md 词条）：行渲染与动作路由只此一份、完全进程无关（ADR-000204）。
/// 宿主侧列 T3 行、扩展主机侧列 T2 行，差异全部由 <see cref="IPluginAdminService.Snapshot"/> 的行视图模型承载；
/// 行文案/警示配色/单动作按钮/校验文案与 v0.2.3 两侧现状逐项一致。面板级内容（标题、目录管理、退出按钮等）
/// 不进本模块，由所在 exe 自行拼装。
/// </summary>
public sealed class PluginAdminPanel : StackPanel
{
    private readonly IPluginAdminService _service;
    private readonly WorkbenchThemeContext _theme;
    private readonly string _emptyStateText;
    private readonly double _emptyStateFontSize;
    private readonly string? _footerText;
    private readonly Action<string, PluginRowAction, PluginAdminResult>? _onApplied;
    private List<PluginAdminRow> _rows = [];

    /// <param name="service">行来源与动作路由（所在进程的 adapter）。</param>
    /// <param name="theme">五区色板上下文（行前景取编辑器区色）。</param>
    /// <param name="emptyStateText">无行时的空态提示（文案归所在 exe）。</param>
    /// <param name="emptyStateFontSize">空态提示字号。</param>
    /// <param name="footerText">行存在时追加在行尾的说明文字（如宿主的崩溃说明）；null 不渲染，空态也不渲染。</param>
    /// <param name="onApplied">行内动作完成后的回调（扩展主机用它更新通知条）；在重绘前触发。</param>
    public PluginAdminPanel(IPluginAdminService service, WorkbenchThemeContext theme,
        string emptyStateText, double emptyStateFontSize = 11,
        string? footerText = null,
        Action<string, PluginRowAction, PluginAdminResult>? onApplied = null)
    {
        _service = service;
        _theme = theme;
        _emptyStateText = emptyStateText;
        _emptyStateFontSize = emptyStateFontSize;
        _footerText = footerText;
        _onApplied = onApplied;
        Spacing = 4;
    }

    /// <summary>最近一次 <see cref="Refresh"/> 渲染的行数（0 = 空态）。</summary>
    public int RowCount { get; private set; }

    /// <summary>重拉快照并重建行（仅 UI 线程调用）。</summary>
    public void Refresh()
    {
        Clear();
        _rows = [.. _service.Snapshot().OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)];
        RowCount = _rows.Count;
        if (_rows.Count == 0)
        {
            Add(new Label().Text(_emptyStateText).FontSize(_emptyStateFontSize)
                .WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)));
            return;
        }
        foreach (var row in _rows)
            Add(BuildRow(row));
        if (_footerText is not null)
            Add(new Label().Text(_footerText).FontSize(11)
                .WithTheme((_, l) => l.Foreground(_theme.EditorArea.Foreground)));
    }

    /// <summary>
    /// 行内动作的程序化入口（按钮点击与测试共用）：路由 <see cref="IPluginAdminService.Apply"/> 并重绘。
    /// 动作 None（清单错误/ID 重复/需 JIT）不路由——不可参与的插件不提供误导性开关。
    /// </summary>
    public void Activate(string id)
    {
        var row = _rows.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        if (row is null || row.State.Action == PluginRowAction.None) return;
        var result = _service.Apply(id, row.State.Action);
        _onApplied?.Invoke(id, row.State.Action, result);
        Refresh();
    }

    private UIElement BuildRow(PluginAdminRow row)
    {
        var state = row.State;
        var titleColor = state.IsWarning ? ShellIcons.HotkeyWarning : _theme.EditorArea.Foreground;
        var title = new Label().Text(row.Title).WithTheme((_, l) => l.Foreground(titleColor));
        var healthLabel = new Label().Text(state.StatusWord).FontSize(11)
            .WithTheme((_, l) => l.Foreground(state.IsWarning ? ShellIcons.HotkeyWarning : _theme.EditorArea.Foreground));
        var actionButton = new Button().Content(new Label().Text(state.ActionLabel)).CanDrag(false)
            .OnClick(() => Activate(row.Id));
        if (state.Action == PluginRowAction.None)
            actionButton.Content(new Label().Text("—"));
        var children = new List<Element> { title, healthLabel };
        if (state.Hint is not null)
            children.Add(new Label().Text(state.Hint).FontSize(11).WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning)));
        if (row.Descriptor.ValidationErrors.Count > 0)
            children.Add(new Label().Text(string.Join("; ", row.Descriptor.ValidationErrors)).FontSize(11)
                .WithTheme((_, l) => l.Foreground(ShellIcons.HotkeyWarning)));
        children.Add(actionButton);
        return new StackPanel().Spacing(2).Children(children.ToArray());
    }
}
