using Aprillz.MewUI;

namespace Mew.Workbench;

/// <summary>
/// 按键名 ↔ MewUI <see cref="Key"/> ↔ Win32 VK 的共享词汇表(单一来源):
/// 热键文本解析(<see cref="HotkeyParser"/>)与按键显示名(宿主捕获改绑的 KeyToName)共用,
/// 避免两份手维护的按键表失步。字母/数字/功能键由算法派生,不在命名表中。
/// </summary>
public static class HotkeyKeys
{
    /// <summary>命名键:显示名(含别名)→ (Win32 VK, MewUI Key, 是否为显示名)。</summary>
    private static readonly (string Name, uint Vk, Key? Key, bool Display)[] NamedKeys =
    [
        ("Space", 0x20, Key.Space, true),
        ("Enter", 0x0D, Key.Enter, true),
        ("Return", 0x0D, Key.Enter, false), // 别名
        ("Escape", 0x1B, Key.Escape, true),
        ("Esc", 0x1B, Key.Escape, false),
        ("Tab", 0x09, Key.Tab, true),
        ("Backspace", 0x08, Key.Backspace, true),
        ("Insert", 0x2D, Key.Insert, true),
        ("Ins", 0x2D, Key.Insert, false),
        ("Delete", 0x2E, Key.Delete, true),
        ("Del", 0x2E, Key.Delete, false),
        ("Home", 0x24, Key.Home, true),
        ("End", 0x23, Key.End, true),
        ("PageUp", 0x21, Key.PageUp, true),
        ("PageDown", 0x22, Key.PageDown, true),
        ("Left", 0x25, Key.Left, true),
        ("Right", 0x27, Key.Right, true),
        ("Up", 0x26, Key.Up, true),
        ("Down", 0x28, Key.Down, true),
        ("PrintScreen", 0x2C, null, true), // 无 MewUI Key 对应,仅解析
        ("Pause", 0x13, null, true),
    ];

    /// <summary>按键名 → VK(不区分大小写,含别名与字母/数字/功能键算法);无法识别返回 false。</summary>
    public static bool TryMapName(string name, out uint vk)
    {
        vk = 0;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.Length == 1)
        {
            var c = char.ToLowerInvariant(name[0]);
            if (c is >= 'a' and <= 'z')
            {
                vk = (uint)(c - 'a' + 0x41);
                return true;
            }
            if (c is >= '0' and <= '9')
            {
                vk = (uint)(c - '0' + 0x30);
                return true;
            }
            return false;
        }

        if (char.ToLowerInvariant(name[0]) == 'f' && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }

        foreach (var entry in NamedKeys)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                vk = entry.Vk;
                return true;
            }
        }

        return false;
    }

    /// <summary>MewUI <see cref="Key"/> → 显示名(捕获改绑场景);无法识别返回空串。</summary>
    public static string NameOf(Key key)
    {
        foreach (var entry in NamedKeys)
        {
            if (entry.Display && entry.Key == key)
            {
                return entry.Name;
            }
        }

        // 算法派生的字母/数字/功能键
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)(key - Key.D0 + '0')).ToString(),
            >= Key.A and <= Key.Z => ((char)(key - Key.A + 'A')).ToString(),
            >= Key.F1 and <= Key.F24 => "F" + (key - Key.F1 + 1),
            _ => "",
        };
    }
}
