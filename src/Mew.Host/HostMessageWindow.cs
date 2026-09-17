using System.Runtime.InteropServices;

namespace Mew.Host;

/// <summary>
/// 宿主消息窗口（替代可见主窗口）：message-only 原生窗口，永不显示、无任务栏按钮、无边框标题，
/// 仅为托盘回调与 RegisterHotKey 提供 HWND。宿主进程唯一实例，AOT 安全（静态 WndProc 委托常驻）。
/// </summary>
internal sealed class HostMessageWindow : IDisposable
{
    private const string ClassName = "MewHostMsgWindow";
    private static readonly IntPtr HwndMessage = new(-3); // HWND_MESSAGE
    private static readonly WndProcDelegate _wndProc = WndProc;
    private static HostMessageWindow? _current;

    private readonly Action<uint, IntPtr, IntPtr> _onMessage;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _hInstance;
    private bool _disposed;

    public IntPtr Handle => _hwnd;

    public HostMessageWindow(Action<uint, IntPtr, IntPtr> onMessage)
    {
        _onMessage = onMessage;
        _hInstance = GetModuleHandle(null);
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = _wndProc,
            hInstance = _hInstance,
            lpszClassName = ClassName,
        };
        if (RegisterClassEx(ref wc) == 0)
            throw new InvalidOperationException($"宿主消息窗口类注册失败（Win32={Marshal.GetLastWin32Error()}）");
        _current = this;
        _hwnd = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, _hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            _current = null;
            UnregisterClass(ClassName, _hInstance);
            throw new InvalidOperationException($"宿主消息窗口创建失败（Win32={Marshal.GetLastWin32Error()}）");
        }
    }

    /// <summary>仅路由热键与托盘回调，其余一律默认处理；永不向外抛异常，避免破坏消息泵。</summary>
    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            _current?._onMessage(msg, wParam, lParam);
        }
        catch
        {
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_current == this) _current = null;
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        UnregisterClass(ClassName, _hInstance);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);
}
