using Aprillz.MewUI;

namespace Mew.Workbench;

/// <summary>捕获态按键的归约去向。</summary>
public enum HotkeyCaptureDisposition
{
    /// <summary>未处于捕获态,按键不归捕获逻辑处理。</summary>
    PassThrough,

    /// <summary>Esc 取消,已退出捕获态。</summary>
    Cancelled,

    /// <summary>按键未构成合法组合,仍在捕获态,提示见 <see cref="HotkeyCaptureOutcome.Notice"/>。</summary>
    Notice,

    /// <summary>捕获到合法且无冲突的组合键,已退出捕获态(单发)。</summary>
    Captured,
}

/// <summary>一次捕获态按键的归约结果:去向 + 捕获到的组合键 / 提示文案。</summary>
public readonly record struct HotkeyCaptureOutcome(HotkeyCaptureDisposition Disposition, string? Hotkey, string? Notice);

/// <summary>
/// 按键捕获状态机(v0.3.2 票据 01 自 PluginHostApp 抽取共享):捕获态下把按键事件归约为
/// 取消/提示/捕获结果,修饰键收集、按键名映射(<see cref="HotkeyKeys.NameOf"/>)、
/// <see cref="HotkeyParser"/> 校验与冲突点名(findOwner 委托)在此收敛。
/// 呼出热键页(Mew.PluginHost)与启动项每项热键(Mew.Launcher)共用,捕获逻辑只此一份;
/// 捕获成功的落点(落盘/IPC/重注册)由调用方自定。
/// </summary>
public sealed class HotkeyCapture
{
    private readonly Func<string, string?> _findOwner;

    /// <param name="findOwner">组合键冲突点名(如 <see cref="IHotkeyService.FindOwner"/>);调用方可包一层屏蔽自身占用的语义。</param>
    public HotkeyCapture(Func<string, string?> findOwner) => _findOwner = findOwner;

    public bool IsCapturing { get; private set; }

    /// <summary>进入捕获态。</summary>
    public void Begin() => IsCapturing = true;

    /// <summary>退出捕获态(取消/捕获成功由 <see cref="Process"/> 自动调用)。</summary>
    public void End() => IsCapturing = false;

    /// <summary>按键归约:未捕获态直通;Esc 取消;修饰键+主键合法且无冲突时捕获成功并退出捕获态,否则提示并继续捕获。</summary>
    public HotkeyCaptureOutcome Process(KeyEventArgs e)
    {
        if (!IsCapturing)
        {
            return new HotkeyCaptureOutcome(HotkeyCaptureDisposition.PassThrough, null, null);
        }

        if (e.Key == Key.Escape)
        {
            End();
            return new HotkeyCaptureOutcome(HotkeyCaptureDisposition.Cancelled, null, null);
        }

        var parts = new List<string>();
        if (e.ControlKey) parts.Add("Ctrl");
        if (e.AltKey) parts.Add("Alt");
        if (e.ShiftKey) parts.Add("Shift");
        if (e.MetaKey) parts.Add("Win");
        var name = HotkeyKeys.NameOf(e.Key);
        if (name.Length == 0)
        {
            return new HotkeyCaptureOutcome(HotkeyCaptureDisposition.Notice, null, "请按字母/数字/功能键组合");
        }

        if (parts.Count == 0)
        {
            return new HotkeyCaptureOutcome(HotkeyCaptureDisposition.Notice, null, "需要至少一个修饰键");
        }

        parts.Add(name);
        var hotkey = string.Join("+", parts);
        if (!HotkeyParser.TryParse(hotkey, out _, out _))
        {
            return new HotkeyCaptureOutcome(HotkeyCaptureDisposition.Notice, null, "不支持的组合");
        }

        var owner = _findOwner(hotkey);
        if (owner is not null)
        {
            return new HotkeyCaptureOutcome(HotkeyCaptureDisposition.Notice, null, $"与{owner}的已注册热键冲突");
        }

        End();
        return new HotkeyCaptureOutcome(HotkeyCaptureDisposition.Captured, hotkey, null);
    }
}
