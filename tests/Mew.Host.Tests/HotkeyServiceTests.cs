using Mew.Workbench;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 全局热键中央注册表(票据 07):注册/注销、跨模块冲突判定与 WM_HOTKEY 分发。
/// 以 <see cref="IntPtr.Zero"/> 句柄注册 = 线程级热键(仅占用本测试线程,不产生窗口消息),
/// 组合键选用系统级少用的组合,降低与其他进程冲突的概率;未显式注销的线程热键随测试线程消亡。
/// </summary>
public class HotkeyServiceTests
{
    [Fact]
    public void Register_格式非法_返回false且未注册()
    {
        var service = new HotkeyService();

        Assert.False(service.Register(IntPtr.Zero, "一键启动", () => { }));
        Assert.False(service.IsRegistered("一键启动"));
    }

    [Fact]
    public void Register_注销后可重新注册()
    {
        var service = new HotkeyService();

        Assert.True(service.Register(IntPtr.Zero, "Ctrl+Alt+F9", () => { }));
        Assert.True(service.IsRegistered("Ctrl+Alt+F9"));

        service.Unregister("Ctrl+Alt+F9");

        Assert.False(service.IsRegistered("Ctrl+Alt+F9"));
        Assert.True(service.Register(IntPtr.Zero, "Ctrl+Alt+F9", () => { }));
    }

    [Fact]
    public void Register_同组合冲突_第二注册失败_与文本写法无关()
    {
        var service = new HotkeyService();
        Assert.True(service.Register(IntPtr.Zero, "Ctrl+Shift+F10", () => { }));

        // 同组合不同文本写法(修饰键顺序不同)也判冲突
        Assert.False(service.Register(IntPtr.Zero, "Shift+Ctrl+F10", () => { }));
        // 跨模块语义:两次注册分属两个模块,冲突由中央注册表统一判定
        Assert.False(service.Register(IntPtr.Zero, "Ctrl+Shift+F10", () => { }));
    }

    [Fact]
    public void Unregister_未注册热键_静默()
    {
        var service = new HotkeyService();

        service.Unregister("Ctrl+Alt+F11"); // 不抛
        Assert.False(service.IsRegistered("Ctrl+Alt+F11"));
    }

    [Fact]
    public void Dispatch_按注册id触发回调_未知id与注销后不再触发()
    {
        var service = new HotkeyService();
        var first = 0;
        var second = 0;
        Assert.True(service.Register(IntPtr.Zero, "Ctrl+Alt+F12", () => first++));
        Assert.True(service.Register(IntPtr.Zero, "Ctrl+Alt+F13", () => second++));

        // id 分配自 0x1000 起(与 v0.1.6 每项热键 id 起点一致):首个注册 = 0x1000,次个 = 0x1001
        Assert.True(service.Dispatch(0x1000));
        Assert.True(service.Dispatch(0x1000));
        Assert.Equal(2, first);
        Assert.Equal(0, second); // 分发按 id 路由,互不串扰

        Assert.False(service.Dispatch(0x7FFF)); // 未知 id
        Assert.Equal(2, first);

        service.Unregister("Ctrl+Alt+F12");
        Assert.False(service.Dispatch(0x1000)); // 注销后 id 不再分发
        Assert.Equal(2, first);
    }

    [Fact]
    public void FindOwner_返回占用方label_未注册与注销后返回null()
    {
        var service = new HotkeyService();
        Assert.True(service.Register(IntPtr.Zero, "Ctrl+Alt+F14", () => { }, "启动项「记事本」"));

        // 文本写法不同(修饰键顺序/大小写)也能定位占用方
        Assert.Equal("启动项「记事本」", service.FindOwner("Alt+Ctrl+F14"));
        Assert.Equal("启动项「记事本」", service.FindOwner("ctrl+alt+f14"));

        Assert.Null(service.FindOwner("Ctrl+Alt+F15")); // 未注册
        Assert.Null(service.FindOwner("一键启动")); // 格式非法

        service.Unregister("Ctrl+Alt+F14");
        Assert.Null(service.FindOwner("Ctrl+Alt+F14")); // 注销后不再占用
    }
}
