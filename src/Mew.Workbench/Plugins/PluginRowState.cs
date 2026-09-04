namespace Mew.Workbench.Plugins;

/// <summary>
/// 设置→插件一行的状态词。策略词（已启用/已禁用）与健康态词（清单错误/ID 重复/需 JIT/已崩溃）
/// 按 ADR-000203 语义合并为单标签：健康态覆盖策略词。
/// </summary>
public enum PluginRowStatus
{
    /// <summary>健康且启用（策略词）。</summary>
    Enabled,

    /// <summary>健康且禁用（策略词）。</summary>
    Disabled,

    /// <summary>健康态覆盖：清单校验失败。</summary>
    InvalidManifest,

    /// <summary>健康态覆盖：id 与其他插件重复。</summary>
    DuplicateId,

    /// <summary>健康态覆盖：需 JIT 扩展主机而当前不可用。</summary>
    NeedsJit,

    /// <summary>健康态覆盖：已崩溃（仅 T3 独立进程语义下出现，动作变重启）。</summary>
    Crashed,
}

/// <summary>行动作。单动作原则：无第二开关；健康行启用/禁用，崩溃行重启，无法参与的插件不提供误导性开关。</summary>
public enum PluginRowAction
{
    /// <summary>无动作（清单错误/ID 重复/需 JIT：不可切换、不可重启）。</summary>
    None,

    /// <summary>切换为启用（当前禁用）。</summary>
    Enable,

    /// <summary>切换为禁用（当前启用）。</summary>
    Disable,

    /// <summary>重新拉起（当前已崩溃，仅 T3）。</summary>
    Restart,
}

/// <summary>设置→插件一行的展示模型：状态词 + 动作 + 副提示，由纯逻辑推导（无 UI 依赖，可无头测试）。</summary>
public sealed record PluginRowState(PluginRowStatus Status, PluginRowAction Action, string? Hint)
{
    /// <summary>动作按钮文案（面向用户）。</summary>
    public string ActionLabel => Action switch
    {
        PluginRowAction.Enable => "启用",
        PluginRowAction.Disable => "禁用",
        PluginRowAction.Restart => "重启",
        _ => "",
    };

    /// <summary>是否标红：健康态覆盖（清单错误/ID 重复）或已崩溃。</summary>
    public bool IsWarning => Status is PluginRowStatus.InvalidManifest or PluginRowStatus.DuplicateId or PluginRowStatus.Crashed;

    /// <summary>状态词。Crashed 单独区分（动作变重启、提示也不同）。</summary>
    public string StatusWord => Status switch
    {
        PluginRowStatus.Enabled => "已启用",
        PluginRowStatus.Disabled => "已禁用",
        PluginRowStatus.InvalidManifest => "清单错误",
        PluginRowStatus.DuplicateId => "ID 重复",
        PluginRowStatus.NeedsJit => "需 JIT 扩展主机",
        PluginRowStatus.Crashed => "已崩溃",
        _ => Status.ToString(),
    };

    /// <summary>
    /// 行状态推导（纯逻辑）：策略（enabled）× 健康态 × 崩溃集合 × 本会话实态 →（状态词、动作、副提示）。
    /// 已崩溃（T3）优先覆盖；清单错误/ID 重复/需 JIT 覆盖策略词且不可切换；
    /// 健康行由策略词给出动作。T2 禁用但本会话仍加载时附加「重启扩展主机后生效」副提示，不谎称已停。
    /// </summary>
    public static PluginRowState Derive(bool enabled, PluginHealth health, bool crashed, bool stillLoaded)
    {
        if (crashed)
            return new PluginRowState(PluginRowStatus.Crashed, PluginRowAction.Restart, "进程异常退出，点击重启重新拉起");

        switch (health)
        {
            case PluginHealth.InvalidManifest:
                return new PluginRowState(PluginRowStatus.InvalidManifest, PluginRowAction.None, null);
            case PluginHealth.DuplicateId:
                return new PluginRowState(PluginRowStatus.DuplicateId, PluginRowAction.None, null);
            case PluginHealth.NeedsJit:
                return new PluginRowState(PluginRowStatus.NeedsJit, PluginRowAction.None, null);
        }

        // 健康：策略词 + 动作
        var status = enabled ? PluginRowStatus.Enabled : PluginRowStatus.Disabled;
        var action = enabled ? PluginRowAction.Disable : PluginRowAction.Enable;
        // 诚实副提示：T2 禁用意图已落但本次会话仍加载（ALC 未卸），下次主界面启动才不再装载
        var hint = !enabled && stillLoaded ? "已禁用，重启扩展主机后生效" : null;
        return new PluginRowState(status, action, hint);
    }
}
