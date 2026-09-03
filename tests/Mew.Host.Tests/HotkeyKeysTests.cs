using Aprillz.MewUI;
using Mew.Workbench;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 按键词汇表(评审修复):解析(HotkeyParser)与显示名(宿主捕获改绑)共用单一来源,
/// 保证「捕获按键 → 显示文本 → 解析回 VK」往返一致,不再各维护一份按键表。
/// </summary>
public class HotkeyKeysTests
{
    [Fact]
    public void NameOf与TryMapName_命名键往返一致()
    {
        var keys = new[]
        {
            Key.Space, Key.Enter, Key.Escape, Key.Tab, Key.Backspace,
            Key.Insert, Key.Delete, Key.Home, Key.End, Key.PageUp, Key.PageDown,
            Key.Left, Key.Right, Key.Up, Key.Down,
        };

        foreach (var key in keys)
        {
            var name = HotkeyKeys.NameOf(key);
            Assert.NotEqual("", name);
            Assert.True(HotkeyKeys.TryMapName(name, out var vk), $"{name} 应可解析回 VK");
            Assert.NotEqual(0u, vk);
        }
    }

    [Fact]
    public void NameOf与TryMapName_字母数字功能键往返一致()
    {
        Assert.Equal("A", HotkeyKeys.NameOf(Key.A));
        Assert.True(HotkeyKeys.TryMapName("a", out var aVk) && aVk == 0x41);

        Assert.Equal("5", HotkeyKeys.NameOf(Key.D5));
        Assert.True(HotkeyKeys.TryMapName("5", out var fiveVk) && fiveVk == 0x35);

        Assert.Equal("F12", HotkeyKeys.NameOf(Key.F12));
        Assert.True(HotkeyKeys.TryMapName("f12", out var f12Vk) && f12Vk == 0x7B);
    }

    [Fact]
    public void TryMapName_别名与大小写不敏感_未知返回false()
    {
        Assert.True(HotkeyKeys.TryMapName("ESC", out var escVk) && escVk == 0x1B);
        Assert.True(HotkeyKeys.TryMapName("return", out var returnVk) && returnVk == 0x0D);
        Assert.True(HotkeyKeys.TryMapName("DEL", out var delVk) && delVk == 0x2E);
        Assert.True(HotkeyKeys.TryMapName("printscreen", out var psVk) && psVk == 0x2C);
        Assert.True(HotkeyKeys.TryMapName("pause", out var pauseVk) && pauseVk == 0x13);

        Assert.False(HotkeyKeys.TryMapName("不是键", out _));
        Assert.False(HotkeyKeys.TryMapName("", out _));
        Assert.False(HotkeyKeys.TryMapName("F25", out _)); // 功能键上限 24
    }
}
