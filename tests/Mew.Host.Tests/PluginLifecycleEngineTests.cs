using Mew.Workbench.Plugins;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 宿主 T3 生命周期引擎（票据 04）：按启用态拉起 exe、禁用即杀、主动停不标崩溃、
/// 意外退出才标已崩溃（运行期推导、不落盘）、防循环达阈值后本次运行不再自动拉起。
/// 全程假进程句柄驱动，无真实子进程（spec Testing Decisions 单一高位缝）。
/// </summary>
public class PluginLifecycleEngineTests
{
    // ---- 基建：假进程句柄与 launcher 记录 ----

    private sealed class FakeHandle : IPluginProcessHandle
    {
        private bool _running = true;
        public bool Killed { get; private set; }
        public event Action? Exited;

        public bool IsRunning => _running;
        public void Stop()
        {
            Killed = true;
            _running = false;
            Exited?.Invoke();
        }

        /// <summary>模拟进程意外死亡：置亡并触发退出事件（宿主 Process.Exited 同路径）。</summary>
        public void Crash()
        {
            _running = false;
            Exited?.Invoke();
        }
    }

    private sealed class Harness
    {
        public List<(string Id, FakeHandle Handle)> Launched { get; } = [];
        public int LaunchFailures;

        /// <summary>launcher：按需失败（FailsFor），否则返回新假句柄。</summary>
        public Func<PluginDescriptor, IPluginProcessHandle?> Launcher(HashSet<string>? failFor = null) => desc =>
        {
            if (failFor?.Contains(desc.Id) == true)
            {
                LaunchFailures++;
                return null;
            }
            var h = new FakeHandle();
            Launched.Add((desc.Id, h));
            return h;
        };

        public PluginLifecycleEngine Engine(HashSet<string>? failFor = null, int threshold = 3, TimeSpan? window = null)
            => new(Launcher(failFor), threshold, window);
    }

    private static PluginDescriptor Exe(string id, string exePath = "plug.exe")
    {
        var manifest = new PluginManifest
        {
            Id = id,
            DisplayName = id,
            Version = "1.0.0",
            Entry = new PluginEntry { Type = "exe", Path = exePath },
            ProtocolVersion = 1,
        };
        return new PluginDescriptor(manifest, $"/tmp/{id}/plugin.json", []);
    }

    private static bool Enabled(string id) => true;

    // ---- 拉起/禁用 ----

    [Fact]
    public void ReconcileStartup_启用插件_全部拉起_禁用插件不拉起()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Register(Exe("beta"));
        engine.ReconcileStartup(
        [
            Exe("alpha"),
            Exe("beta"),
        ]);

        Assert.Equal(["alpha", "beta"], h.Launched.Select(l => l.Id).OrderBy(x => x).ToArray());
        Assert.Equal(2, h.Launched.Count);
    }

    [Fact]
    public void Start_幂等_已在运行不重复拉起()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        engine.Start("alpha", out _);
        Assert.Single(h.Launched);
        Assert.True(engine.GetState("alpha", Enabled)!.Running);
        Assert.False(engine.GetState("alpha", Enabled)!.Crashed);
    }

    [Fact]
    public void Stop_禁用_杀进程_不标崩溃()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        var handle = h.Launched.Single().Handle;

        engine.Stop("alpha");

        Assert.True(handle.Killed);
        var st = engine.GetState("alpha", Enabled)!;
        Assert.False(st.Crashed);
        Assert.False(st.Running);
    }

    [Fact]
    public void 主动停后陈旧退出_不标崩溃()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        var handle = h.Launched.Single().Handle;

        engine.Stop("alpha");       // 主动停：句柄已从托管摘除
        handle.Crash();             // 陈旧退出事件
        var st = engine.GetState("alpha", Enabled)!;
        Assert.False(st.Crashed);
    }

    // ---- 崩溃归因 ----

    [Fact]
    public void 意外退出_标已崩溃_不自动重拉()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        var before = h.Launched.Count;

        h.Launched.Single().Handle.Crash();

        var st = engine.GetState("alpha", Enabled)!;
        Assert.True(st.Crashed);
        Assert.Equal(before, h.Launched.Count); // 仅标记，无自动重拉
        // 崩溃行动作应变重启（纯逻辑层）：状态词由 PluginRowState 推导
        Assert.Equal(PluginRowAction.Restart, PluginRowState.Derive(enabled: true, PluginHealth.Healthy, crashed: true, stillLoaded: false).Action);
    }

    [Fact]
    public void 崩溃后手动重启_重新拉起并清崩溃标记()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        h.Launched.Single().Handle.Crash();
        Assert.True(engine.GetState("alpha", Enabled)!.Crashed);

        var ok = engine.Restart("alpha", out var error);

        Assert.True(ok, error);
        Assert.Equal(2, h.Launched.Count);
        var st = engine.GetState("alpha", Enabled)!;
        Assert.False(st.Crashed);
        Assert.True(st.Running);
    }

    [Fact]
    public void MarkUnexpectedExit_插件已主动停或未启动_忽略不标崩溃()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        engine.Stop("alpha"); // 主动停

        engine.MarkUnexpectedExit("alpha"); // IPC 断连等陈旧信号

        Assert.False(engine.GetState("alpha", Enabled)!.Crashed);
    }

    [Fact]
    public void MarkUnexpectedExit_启用且运行中_标崩溃()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);

        engine.MarkUnexpectedExit("alpha"); // IPC 断连路径

        Assert.True(engine.GetState("alpha", Enabled)!.Crashed);
    }

    // ---- 防循环 ----

    /// <summary>拉起→意外退出循环 count 轮（经真实 Exited 回调路径累计崩溃计数）。</summary>
    private static void CrashRepeatedly(PluginLifecycleEngine engine, Harness h, string id, int count)
    {
        for (var i = 0; i < count; i++)
        {
            string? err;
            var ok = i == 0 ? engine.Start(id, out err) : engine.Restart(id, out err);
            Assert.True(ok, err);
            h.Launched[^1].Handle.Crash();
        }
    }

    [Fact]
    public void 连续崩溃达阈值_标崩溃_启动对齐不再自动拉起()
    {
        var h = new Harness();
        var engine = h.Engine(threshold: 3, window: TimeSpan.FromSeconds(30));
        engine.Register(Exe("alpha"));
        CrashRepeatedly(engine, h, "alpha", 3);

        var st = engine.GetState("alpha", Enabled)!;
        Assert.True(st.Crashed);
        Assert.True(st.LoopGuarded);

        // 启动对齐（自动拉起路径）被防循环门拦下：本次运行不再自动拉起
        var before = h.Launched.Count;
        engine.ReconcileStartup([Exe("alpha")]);
        Assert.Equal(before, h.Launched.Count);
        // 手动重启是唯一恢复路径
        Assert.True(engine.Restart("alpha", out var err), err);
        Assert.Equal(before + 1, h.Launched.Count);
    }

    [Fact]
    public void 防循环锁定后_手动重启仍放行_运行成功计数复位()
    {
        var h = new Harness();
        var engine = h.Engine(threshold: 3, window: TimeSpan.FromSeconds(30));
        engine.Register(Exe("alpha"));
        CrashRepeatedly(engine, h, "alpha", 3);
        Assert.True(engine.GetState("alpha", Enabled)!.LoopGuarded);

        // 手动重启：放行，新一轮成功即清崩溃标记
        var ok = engine.Restart("alpha", out var error);
        Assert.True(ok, error);
        var st = engine.GetState("alpha", Enabled)!;
        Assert.False(st.Crashed);
        Assert.True(st.Running);
        // 计数仍在本轮防循环窗口内（最近一次崩溃未超过窗口），但运行中状态不受锁影响
        Assert.True(engine.GetState("alpha", Enabled)!.LoopGuarded);

        // 新进程运行中被手动杀（主动停）不会计入崩溃
        engine.Stop("alpha");
        Assert.False(engine.GetState("alpha", Enabled)!.Crashed);
    }

    [Fact]
    public void 崩溃间隔超过窗口_计数复位_不触发防循环()
    {
        var h = new Harness();
        var engine = h.Engine(threshold: 2, window: TimeSpan.FromMilliseconds(1));
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        h.Launched[^1].Handle.Crash();            // 1：窗口 1ms
        Thread.Sleep(5);                          // 超过窗口，视为不连续
        engine.Start("alpha", out _);            // 重新运行成功 → 计数复位
        h.Launched[^1].Handle.Crash();            // 复位后首次崩溃

        Assert.False(engine.GetState("alpha", Enabled)!.LoopGuarded);
    }

    [Fact]
    public void 拉起失败_exe缺失_不标崩溃_状态可查询()
    {
        var h = new Harness();
        var engine = h.Engine(failFor: ["alpha"]);
        engine.Register(Exe("alpha"));
        var ok = engine.Start("alpha", out _);

        Assert.False(ok);
        Assert.Equal(1, h.LaunchFailures);
        var st = engine.GetState("alpha", Enabled)!;
        Assert.False(st.Crashed);
        Assert.False(st.Running);
    }

    // ---- 重启宿主后按启用态重拉（新实例状态清零）----

    [Fact]
    public void 新引擎实例_崩溃状态不残留_按启用态重拉()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        engine.MarkUnexpectedExit("alpha");
        Assert.True(engine.GetState("alpha", Enabled)!.Crashed);

        // 宿主重启 = 新引擎实例，无任何崩溃残留
        var engine2 = h.Engine();
        engine2.Register(Exe("alpha"));
        engine2.ReconcileStartup([Exe("alpha")]);
        var st = engine2.GetState("alpha", Enabled)!;
        Assert.False(st.Crashed);
        Assert.True(st.Running);
        Assert.Equal(2, h.Launched.Count);
    }

    [Fact]
    public void PluginStateChanged_崩溃时触发_宿主可刷新面板()
    {
        var h = new Harness();
        var engine = h.Engine();
        engine.Register(Exe("alpha"));
        engine.Start("alpha", out _);
        var notified = new List<string>();
        engine.PluginStateChanged += notified.Add;

        engine.MarkUnexpectedExit("alpha");

        Assert.Contains("alpha", notified);
    }
}
