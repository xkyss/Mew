using Mew.Workbench;

namespace Mew.Launcher;

/// <summary>
/// 每项热键的集中注册(票据 07):经宿主中央热键服务(<see cref="IHotkeyService"/>)注册,
/// 冲突检测(含跨模块、与浮层呼出键)与 WM_HOTKEY 分发归服务;本类只负责
/// 「把启动项每项热键全量注销后重建」。变更后由调用方触发 RegisterAll。
/// </summary>
internal sealed class ItemHotkeys
{
    private readonly List<LauncherItem> _items;
    private readonly Action<LauncherItem> _launch;
    private readonly Action<string> _feedback;
    private readonly IHotkeyService _hotkeys;
    private readonly List<string> _registered = [];
    private IntPtr _handle;

    internal ItemHotkeys(List<LauncherItem> items, Action<LauncherItem> launch, Action<string> feedback, IHotkeyService hotkeys)
    {
        _items = items;
        _launch = launch;
        _feedback = feedback;
        _hotkeys = hotkeys;
    }

    internal void Attach(IntPtr windowHandle) => _handle = windowHandle;

    /// <summary>注销上次注册的全部每项热键,再按当前启动项数据重新注册;失败(冲突/被占用)经回调给出反馈。</summary>
    internal void RegisterAll()
    {
        if (_handle == IntPtr.Zero)
        {
            return; // 测试环境无窗口句柄,跳过真实注册
        }

        foreach (var hotkey in _registered)
        {
            _hotkeys.Unregister(hotkey);
        }
        _registered.Clear();

        foreach (var item in _items)
        {
            var hotkey = item.Hotkey;
            if (string.IsNullOrWhiteSpace(hotkey))
            {
                continue;
            }

            var itemCopy = item; // 闭包捕获当前项(后续项可被编辑/删除)
            if (_hotkeys.Register(_handle, hotkey, () => _launch(itemCopy), $"启动项「{itemCopy.Name}」"))
            {
                _registered.Add(hotkey);
            }
            else
            {
                // 与 v0.1.6 一致:冲突提示点名占用方(如浮层呼出键/其他启动项),而非通用文案
                var owner = _hotkeys.FindOwner(hotkey);
                _feedback(owner is null
                    ? $"热键 {hotkey} 注册失败(可能已被其他程序占用),未生效"
                    : $"热键 {hotkey} 与{owner}的已注册热键冲突,未生效");
            }
        }
    }
}
