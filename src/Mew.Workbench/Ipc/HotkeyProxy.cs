namespace Mew.Workbench.Ipc;

/// <summary>
/// 热键代理：插件侧 IHotkeyService 实现，转发至宿主 IpcServer 集中注册。
/// </summary>
public sealed class HotkeyProxy : IHotkeyService
{
    private readonly IpcServer _server;
    private readonly string _pluginId;
    private readonly PluginCapabilitiesDto? _capabilities;

    public HotkeyProxy(IpcServer server, string pluginId, PluginCapabilitiesDto? capabilities)
    {
        _server = server;
        _pluginId = pluginId;
        _capabilities = capabilities;
    }

    public bool Register(IntPtr hwnd, string hotkey, Action callback, string? label = null)
    {
        // 通过宿主集中注册，回调在测试中直接触发（管道模式下经 hotkeyTriggered 通知）
        var ack = _server.TryRegisterHotkey(_pluginId, hotkey, label ?? hotkey, _capabilities);
        return ack.Ok;
    }

    public void Unregister(string hotkey) => _server.HotkeyUnregister(hotkey);

    public bool IsRegistered(string hotkey) => _server.IsHotkeyRegistered(hotkey);

    public string? FindOwner(string hotkey) => _server.FindHotkeyOwner(hotkey);
}
