namespace Mew.Workbench;

/// <summary>
/// 每项热键文本(Ctrl+Shift+1 形式)解析为 Win32 修饰键与虚拟键;至少要求一个修饰键,避免裸键劫持普通输入。
/// </summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModWin = 0x8;

    public static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win" or "windows" or "meta":
                    modifiers |= ModWin;
                    break;
                default:
                    return false;
            }
        }

        return HotkeyKeys.TryMapName(parts[^1], out vk);
    }
}
