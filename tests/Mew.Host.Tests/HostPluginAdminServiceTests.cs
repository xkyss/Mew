using Mew.Host;
using Mew.Workbench.Plugins;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 宿主侧插件管理 adapter（ADR-000204 票据 02）：引擎锁与启用锁收进 adapter 内部，
/// Snapshot 沿「引擎锁 → 启用锁」单向取锁，Apply 落盘在锁内、引擎动作在锁外；
/// IPC 与 UI 行动作共用同一条加锁路径。假进程句柄 + 临时启用存储驱动，无真实子进程。
/// </summary>
public class HostPluginAdminServiceTests
{
    // ---- 基建（与 PluginLifecycleEngineTests 同构） ----

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

        public void Crash()
        {
            _running = false;
            Exited?.Invoke();
        }
    }

    private sealed class Harness
    {
        public List<(string Id, FakeHandle Handle)> Launched { get; } = [];
        public PluginLifecycleEngine Engine { get; }

        public Harness()
        {
            Engine = new PluginLifecycleEngine(desc =>
            {
                var handle = new FakeHandle();
                Launched.Add((desc.Id, handle));
                return handle;
            });
        }

        public string StorePath { get; } = Path.Combine(Path.GetTempPath(), "mew-host-tests", Guid.NewGuid().ToString("N") + "-plugins.json");

        /// <summary>新存储实例并从盘读回（验证持久化真实落盘）。</summary>
        public PluginEnableStore ReloadedStore()
        {
            var store = new PluginEnableStore(StorePath);
            store.Load();
            return store;
        }

        public PluginEnableStore Store() => new(StorePath);
    }

    private static PluginDescriptor Exe(string id)
    {
        var manifest = new PluginManifest
        {
            Id = id,
            DisplayName = id + "-disp",
            Version = "1.0.0",
            Entry = new PluginEntry { Type = "exe", Path = "plug.exe" },
            ProtocolVersion = 1,
        };
        return new PluginDescriptor(manifest, $"/tmp/{id}/plugin.json", []);
    }

    private static HostPluginAdminService Service(Harness h, PluginEnableStore store, IEnumerable<string>? knownIds = null)
        => new(h.Engine, store, knownIds ?? ["alpha", "beta"], _ => { });

    // ---- Snapshot ----

    [Fact]
    public void Snapshot_行含描述符与行状态_启用态取自存储()
    {
        var h = new Harness();
        var store = h.Store();
        store.SetEnabled("alpha", false);
        h.Engine.Register(Exe("beta"));
        h.Engine.Register(Exe("alpha"));
        var service = Service(h, store);

        var rows = service.Snapshot();

        Assert.Equal(["beta", "alpha"], rows.Select(r => r.Id).ToArray()); // 注册序；排序归面板
        var alpha = rows.Single(r => r.Id == "alpha");
        Assert.Equal(PluginRowStatus.Disabled, alpha.State.Status);
        Assert.Equal(PluginRowAction.Enable, alpha.State.Action);
        Assert.Equal("alpha-disp (alpha) v1.0.0", alpha.Title);
        var beta = rows.Single(r => r.Id == "beta");
        Assert.Equal(PluginRowStatus.Enabled, beta.State.Status);
        Assert.Equal(PluginRowAction.Disable, beta.State.Action);
    }

    [Fact]
    public void Snapshot_崩溃态进入行模型_动作变重启()
    {
        var h = new Harness();
        var store = h.Store();
        h.Engine.Register(Exe("alpha"));
        h.Engine.ReconcileStartup([Exe("alpha")]);
        h.Launched.Single(l => l.Id == "alpha").Handle.Crash();
        var service = Service(h, store);

        var row = Assert.Single(service.Snapshot());

        Assert.Equal(PluginRowStatus.Crashed, row.State.Status);
        Assert.Equal(PluginRowAction.Restart, row.State.Action);
    }

    // ---- Apply：锁内落盘 + 引擎锁外即时生效 ----

    [Fact]
    public void Apply_Enable_锁内落盘_引擎拉起_持久化生效()
    {
        var h = new Harness();
        var store = h.Store();
        store.SetEnabled("alpha", false);
        h.Engine.Register(Exe("alpha"));
        var service = Service(h, store);

        var result = service.Apply("alpha", PluginRowAction.Enable);

        Assert.Equal(PluginAdminOutcome.Ok, result.Outcome);
        Assert.Contains(h.Launched, l => l.Id == "alpha");
        Assert.True(h.Engine.GetState("alpha", id => store.IsEnabled(id))!.Running);
        // 落盘可被新存储实例读回（真写文件）
        Assert.True(h.ReloadedStore().IsEnabled("alpha"));
    }

    [Fact]
    public void Apply_Disable_锁内落盘_引擎杀进程()
    {
        var h = new Harness();
        var store = h.Store();
        h.Engine.Register(Exe("alpha"));
        h.Engine.ReconcileStartup([Exe("alpha")]);
        var service = Service(h, store);

        var result = service.Apply("alpha", PluginRowAction.Disable);

        Assert.Equal(PluginAdminOutcome.Ok, result.Outcome);
        Assert.True(h.Launched.Single(l => l.Id == "alpha").Handle.Killed);
        Assert.False(h.ReloadedStore().IsEnabled("alpha"));
    }

    [Fact]
    public void Apply_Restart_崩溃行重拉并清崩溃标记()
    {
        var h = new Harness();
        var service = Service(h, h.Store());
        h.Engine.Register(Exe("alpha"));
        h.Engine.ReconcileStartup([Exe("alpha")]);
        h.Launched.Single(l => l.Id == "alpha").Handle.Crash();

        var result = service.Apply("alpha", PluginRowAction.Restart);

        Assert.Equal(PluginAdminOutcome.Ok, result.Outcome);
        Assert.Equal(2, h.Launched.Count(l => l.Id == "alpha"));
        var state = h.Engine.GetState("alpha", _ => true)!;
        Assert.True(state.Running);
        Assert.False(state.Crashed);
    }

    [Fact]
    public void Apply_未注册引擎的id_只落盘意图_不拉起()
    {
        var h = new Harness();
        var service = Service(h, h.Store(), ["alpha", "t2-only"]);

        var result = service.Apply("t2-only", PluginRowAction.Disable);

        Assert.Equal(PluginAdminOutcome.Ok, result.Outcome);
        Assert.False(h.ReloadedStore().IsEnabled("t2-only"));
        Assert.Empty(h.Launched);
    }

    // ---- 拒绝门 ----

    [Fact]
    public void Apply_未知id_拒绝_不落盘不拉起()
    {
        var h = new Harness();
        var store = h.Store();
        h.Engine.Register(Exe("alpha"));
        var service = Service(h, store);

        var result = service.Apply("ghost", PluginRowAction.Enable);

        Assert.Equal(PluginAdminOutcome.Rejected, result.Outcome);
        Assert.Equal("未知插件：ghost", result.Error);
        Assert.Empty(h.Launched);
        Assert.False(File.Exists(store.FilePath)); // 拒绝路径不产生任何持久化
    }

    [Fact]
    public void Apply_保留宿主id_拒绝()
    {
        var h = new Harness();
        var service = Service(h, h.Store(), ["mew-host", "alpha"]);

        var result = service.Apply("mew-host", PluginRowAction.Enable);

        Assert.Equal(PluginAdminOutcome.Rejected, result.Outcome);
        Assert.Equal("未知插件：mew-host", result.Error);
    }

    [Fact]
    public void Apply_无动作行_拒绝路由()
    {
        var h = new Harness();
        var service = Service(h, h.Store());

        var result = service.Apply("alpha", PluginRowAction.None);

        Assert.Equal(PluginAdminOutcome.Rejected, result.Outcome);
        Assert.Empty(h.Launched);
    }

    // ---- 并发纪律：Apply 持启用锁期间不碰引擎（Snapshot 反向取锁才无死锁） ----

    [Fact]
    public async Task Snapshot与Apply并发_双向压测_限期内完成无死锁()
    {
        var h = new Harness();
        var store = h.Store();
        h.Engine.Register(Exe("alpha"));
        h.Engine.Register(Exe("beta"));
        var service = Service(h, store);

        var snapshotTask = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++) service.Snapshot();
        });
        var applyTask = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
                service.Apply("alpha", i % 2 == 0 ? PluginRowAction.Enable : PluginRowAction.Disable);
        });

        // 若 Apply 在启用锁内调引擎（反向嵌套），与 Snapshot 的「引擎锁→启用锁」方向相反，等待必然死锁超时
        await Task.WhenAll(snapshotTask, applyTask).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, service.Snapshot().Count);
    }
}
