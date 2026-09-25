using Mew.Workbench;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 热键文本解析与组合等价判定:<see cref="HotkeyParser.IsSameCombo"/> 服务于每项热键捕获的
/// 自身占用屏蔽(重复捕获本项已设组合不算冲突),判定必须与写法顺序无关、语义级(修饰键+键码)相等。
/// </summary>
public class HotkeyParserTests
{
    [Fact]
    public void IsSameCombo_写法顺序与大小写不同_语义等价为真()
    {
        Assert.True(HotkeyParser.IsSameCombo("Ctrl+Shift+1", "Shift+Ctrl+1"));
        Assert.True(HotkeyParser.IsSameCombo("ctrl+alt+f14", "Alt+Ctrl+F14"));
    }

    [Fact]
    public void IsSameCombo_不同组合或单侧非法_为假()
    {
        Assert.False(HotkeyParser.IsSameCombo("Ctrl+Shift+1", "Ctrl+Shift+2"));
        Assert.False(HotkeyParser.IsSameCombo("Ctrl+Shift+1", "Ctrl+Alt+1"));
        Assert.False(HotkeyParser.IsSameCombo("Ctrl+Shift+1", "一键启动")); // 单侧非法
    }

    [Fact]
    public void IsSameCombo_任一侧为空_为假()
    {
        Assert.False(HotkeyParser.IsSameCombo(null, "Ctrl+Shift+1"));
        Assert.False(HotkeyParser.IsSameCombo("Ctrl+Shift+1", ""));
        Assert.False(HotkeyParser.IsSameCombo(" ", "Ctrl+Shift+1"));
        Assert.False(HotkeyParser.IsSameCombo(null, null));
    }
}
