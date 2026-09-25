using Aprillz.MewUI;
using Mew.Workbench;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 按键捕获状态机(v0.3.2 票据 01 自 PluginHostApp 抽取):归约去向、修饰键收集、
/// HotkeyParser 校验与 findOwner 冲突点名。呼出热键页(Mew.PluginHost)与启动项每项热键
/// (Mew.Launcher)共用,捕获逻辑只此一份。KeyEventArgs 为 MewUI public 可构造类型,
/// 修饰键布尔值自构造参数派生,可无头驱动。
/// </summary>
public class HotkeyCaptureTests
{
    private static KeyEventArgs KeyDown(Key key, bool ctrl = false, bool alt = false, bool shift = false, bool win = false) =>
        new(key, 0,
            (ctrl ? ModifierKeys.Control : ModifierKeys.None)
            | (alt ? ModifierKeys.Alt : ModifierKeys.None)
            | (shift ? ModifierKeys.Shift : ModifierKeys.None)
            | (win ? ModifierKeys.Meta : ModifierKeys.None),
            isRepeat: false);

    [Fact]
    public void 未捕获态直通_不消费按键()
    {
        var capture = new HotkeyCapture(_ => "浮层呼出键");

        var outcome = capture.Process(KeyDown(Key.A, ctrl: true));

        Assert.Equal(HotkeyCaptureDisposition.PassThrough, outcome.Disposition);
        Assert.Null(outcome.Hotkey);
        Assert.Null(outcome.Notice);
        Assert.False(capture.IsCapturing);
    }

    [Fact]
    public void Esc_取消捕获并退出捕获态()
    {
        var capture = new HotkeyCapture(_ => null);
        capture.Begin();

        var outcome = capture.Process(KeyDown(Key.Escape));

        Assert.Equal(HotkeyCaptureDisposition.Cancelled, outcome.Disposition);
        Assert.Null(outcome.Notice);
        Assert.False(capture.IsCapturing);
    }

    [Fact]
    public void 不可映射主键_提示继续捕获()
    {
        var capture = new HotkeyCapture(_ => null);
        capture.Begin();

        // MewUI Key 无修饰键成员:纯修饰键按下 e.Key 为 None,同「不可映射主键」路径
        var outcome = capture.Process(KeyDown(Key.None, ctrl: true));

        Assert.Equal(HotkeyCaptureDisposition.Notice, outcome.Disposition);
        Assert.Equal("请按字母/数字/功能键组合", outcome.Notice);
        Assert.True(capture.IsCapturing); // 仍在捕获态,可继续按
    }

    [Fact]
    public void 裸主键无修饰键_提示需要修饰键()
    {
        var capture = new HotkeyCapture(_ => null);
        capture.Begin();

        var outcome = capture.Process(KeyDown(Key.A));

        Assert.Equal(HotkeyCaptureDisposition.Notice, outcome.Disposition);
        Assert.Equal("需要至少一个修饰键", outcome.Notice);
        Assert.True(capture.IsCapturing);
    }

    [Fact]
    public void 合法组合_修饰键按固定顺序捕获成功并单发退出()
    {
        var capture = new HotkeyCapture(_ => null);
        capture.Begin();

        // 按下顺序 Shift→Ctrl 无关:产出固定序 Ctrl,Alt,Shift,Win
        var outcome = capture.Process(KeyDown(Key.D1, shift: true, ctrl: true));

        Assert.Equal(HotkeyCaptureDisposition.Captured, outcome.Disposition);
        Assert.Equal("Ctrl+Shift+1", outcome.Hotkey);
        Assert.Null(outcome.Notice);
        Assert.False(capture.IsCapturing); // 单发:捕获成功即退出
    }

    [Fact]
    public void Win组合_映射为Win修饰键()
    {
        var capture = new HotkeyCapture(_ => null);
        capture.Begin();

        var outcome = capture.Process(KeyDown(Key.F5, win: true));

        Assert.Equal(HotkeyCaptureDisposition.Captured, outcome.Disposition);
        Assert.Equal("Win+F5", outcome.Hotkey);
    }

    [Fact]
    public void 冲突组合_点名占用方_继续捕获()
    {
        var service = new HotkeyService();
        Assert.True(service.Register(IntPtr.Zero, "Ctrl+Alt+F20", () => { }, "浮层呼出键"));
        var capture = new HotkeyCapture(service.FindOwner);
        capture.Begin();

        var outcome = capture.Process(KeyDown(Key.F20, ctrl: true, alt: true));

        Assert.Equal(HotkeyCaptureDisposition.Notice, outcome.Disposition);
        Assert.Equal("与浮层呼出键的已注册热键冲突", outcome.Notice);
        Assert.True(capture.IsCapturing);
    }

    [Fact]
    public void 自身占用被调用方屏蔽后_同一组合可再次捕获()
    {
        // Launcher 每项热键语义:重复捕获本项已设组合不算冲突(findOwner 包一层 IsSameCombo 屏蔽自身)
        var service = new HotkeyService();
        Assert.True(service.Register(IntPtr.Zero, "Ctrl+Alt+F21", () => { }, "启动项「记事本」"));
        string? current = "Ctrl+Alt+F21";
        var capture = new HotkeyCapture(hotkey =>
            HotkeyParser.IsSameCombo(hotkey, current) ? null : service.FindOwner(hotkey));
        capture.Begin();

        var outcome = capture.Process(KeyDown(Key.F21, ctrl: true, alt: true));

        Assert.Equal(HotkeyCaptureDisposition.Captured, outcome.Disposition);
        Assert.Equal("Ctrl+Alt+F21", outcome.Hotkey);
    }
}
