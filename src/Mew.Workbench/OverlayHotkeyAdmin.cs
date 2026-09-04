namespace Mew.Workbench;

/// <summary>
/// 呼出热键变更决策（宿主侧纯逻辑，无头可测）：校验组合合法性、点名冲突、失败回滚旧键。
/// 持久化（settings.json）与日志由调用方（MewHost）完成；成功时返回的生效态即应落盘的值。
/// </summary>
public static class OverlayHotkeyAdmin
{
    public static (bool Ok, string? Error, string EffectiveHotkey, bool EffectiveEnabled) Apply(
        HotkeyService hotkeys, string current, IntPtr hwnd, Action callback, string label,
        string? requested, bool enabled)
    {
        if (!enabled)
        {
            hotkeys.Unregister(current);
            return (true, null, current, false);
        }

        if (string.IsNullOrWhiteSpace(requested) || !HotkeyParser.TryParse(requested, out _, out _))
            return (false, "不支持的热键组合", current, true);

        var owner = hotkeys.FindOwner(requested);
        if (owner is not null && !string.Equals(owner, label, StringComparison.Ordinal))
            return (false, $"与{owner}的已注册热键冲突", current, true);

        hotkeys.Unregister(current);
        if (!hotkeys.Register(hwnd, requested, callback, label))
        {
            // 回滚旧键后按占用情况报错
            hotkeys.Register(hwnd, current, callback, label);
            var occupant = hotkeys.FindOwner(requested);
            return (false, occupant is null ? "注册失败（可能被其他程序占用）" : $"与{occupant}的已注册热键冲突", current, true);
        }

        return (true, null, requested, true);
    }
}
