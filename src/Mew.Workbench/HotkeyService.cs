using System.Runtime.InteropServices;

namespace Mew.Workbench;

/// <summary>
/// 全局热键中央注册表(宿主):浮层呼出键与各模块每项热键统一经此注册,跨注册冲突检测统一
/// (同修饰键+键码即冲突,与文本写法无关);WM_HOTKEY 由宿主消息循环转发,按注册 id 分发回调。
/// 替换票据 06 的 <see cref="ScaffoldHotkeyService"/> 占位与旧的静态 <c>GlobalHotkey</c>。
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private const uint ModNoRepeat = 0x4000; // 按住不连发
    private const int BaseId = 0x1000; // 与 v0.1.6 每项热键 id 起点一致

    /// <summary>WM_HOTKEY</summary>
    public const uint WmHotkey = 0x0312;

    private readonly List<Registration> _registrations = [];
    private int _nextId = BaseId;

    private sealed record Registration(IntPtr Hwnd, string Text, uint Modifiers, uint Vk, int Id, string? Label, Action Callback);

    /// <summary>注册全局热键:格式非法、跨注册冲突(同修饰键+键码)或系统注册失败(已被其他进程占用)时返回 false。</summary>
    public bool Register(IntPtr hwnd, string hotkey, Action callback, string? label = null)
    {
        if (!HotkeyParser.TryParse(hotkey, out var modifiers, out var vk))
        {
            return false; // 格式非法
        }

        if (_registrations.Any(registration => registration.Modifiers == modifiers && registration.Vk == vk))
        {
            return false; // 跨注册冲突(含本服务内其他条目,如浮层键与每项热键)
        }

        var id = _nextId++;
        if (OperatingSystem.IsWindows())
        {
            if (!RegisterHotKey(hwnd, id, modifiers | ModNoRepeat, vk))
                return false; // 系统级失败(已被其他程序占用等)
        }

        _registrations.Add(new Registration(hwnd, hotkey, modifiers, vk, id, label, callback));
        return true;
    }

    /// <summary>注销指定热键;未注册时静默。按解析后的修饰键+键码匹配,文本写法不同也能注销。</summary>
    public void Unregister(string hotkey)
    {
        if (!HotkeyParser.TryParse(hotkey, out var modifiers, out var vk))
        {
            return;
        }

        var index = _registrations.FindIndex(registration => registration.Modifiers == modifiers && registration.Vk == vk);
        if (index < 0)
        {
            return;
        }

        var registration = _registrations[index];
        _registrations.RemoveAt(index);
        if (OperatingSystem.IsWindows())
            UnregisterHotKey(registration.Hwnd, registration.Id);
    }

    /// <summary>该组合键是否已被注册(含本模块与其他模块,以及宿主浮层键)。</summary>
    public bool IsRegistered(string hotkey)
    {
        if (!HotkeyParser.TryParse(hotkey, out var modifiers, out var vk))
        {
            return false;
        }

        return _registrations.Any(registration => registration.Modifiers == modifiers && registration.Vk == vk);
    }

    /// <summary>返回占用指定组合的注册描述(注册时给的 label),未占用返回 null;用于冲突提示点名。</summary>
    public string? FindOwner(string hotkey)
    {
        if (!HotkeyParser.TryParse(hotkey, out var modifiers, out var vk))
        {
            return null;
        }

        return _registrations.FirstOrDefault(registration => registration.Modifiers == modifiers && registration.Vk == vk)?.Label;
    }

    /// <summary>WM_HOTKEY 分发(宿主在窗口消息处理中调用):按注册 id 找到回调并触发;未知 id 返回 false。</summary>
    public bool Dispatch(int hotkeyId)
    {
        var registration = _registrations.FirstOrDefault(candidate => candidate.Id == hotkeyId);
        if (registration is null)
        {
            return false;
        }

        registration.Callback();
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
