using System.Drawing;
using System.Runtime.InteropServices;

namespace Mew.Workbench;

/// <summary>
/// 托盘常驻图标:单击打开主界面（只唤出，多次点击也不隐藏），右键菜单打开/重启主界面或退出；热键打开/隐藏浮层。
/// 回调消息(WM_APP)经宿主消息窗口路由，主界面显隐由宿主经 Win32 句柄切换（关=隐藏，进程常驻）。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001; // WM_APP + 1
    public const uint WmCallback = CallbackMessage;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    public const int MenuQuit = 1;
    public const int MenuOpenWorkspace = 2;
    public const int MenuRestartWorkspace = 3;

    private readonly IntPtr _windowHandle;
    private readonly Action _quit;
    private readonly Action? _openWorkspace;
    private readonly Action? _restartWorkspace;
    private readonly Action? _singleClick;
    private readonly IntPtr _menu;
    private readonly IntPtr _icon;
    private NotifyIconData _nid;

    public TrayIcon(IntPtr windowHandle, Action quit, Action? openWorkspace = null, Action? restartWorkspace = null, Action? singleClick = null, string? tip = null)
    {
        _windowHandle = windowHandle;
        _quit = quit;
        _openWorkspace = openWorkspace;
        _restartWorkspace = restartWorkspace;
        _singleClick = singleClick;

        using var sourceIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        _icon = CopyIcon(sourceIcon.Handle);

        _nid = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = windowHandle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = tip ?? "Mew Launcher",
        };

        _menu = CreatePopupMenu();
        AppendMenu(_menu, 0, (UIntPtr)MenuOpenWorkspace, "打开主界面");
        AppendMenu(_menu, 0, (UIntPtr)MenuRestartWorkspace, "重启主界面");
        AppendMenu(_menu, 0, (UIntPtr)MenuQuit, "退出");
    }

    public void Add() => Shell_NotifyIconW(NimAdd, ref _nid);

    /// <summary>托盘气球告警（宿主窗口永不亮出后的提示出口）：NIM_MODIFY + NIF_INFO 即时弹出，静默失败不抛。</summary>
    public void ShowBalloon(string title, string text)
    {
        var (balloonTitle, balloonText) = BuildBalloonText(title, text);
        var nid = _nid;
        nid.uFlags = NifInfo;
        nid.szInfo = balloonText;
        nid.szInfoTitle = balloonTitle;
        nid.dwInfoFlags = NiifWarning;
        Shell_NotifyIconW(NimModify, ref nid);
    }

    /// <summary>气球文本整形（纯逻辑，可单测）：按 NOTIFYICONDATA 容量截断超长标题/正文。</summary>
    public static (string Title, string Text) BuildBalloonText(string title, string text) => (
        title.Length <= 63 ? title : title[..63],
        text.Length <= 255 ? text : text[..255]);

    public bool HandleCallback(uint wParam, uint lParam)
    {
        if (wParam != _nid.uID)
        {
            return false;
        }

        switch (lParam)
        {
            case WmLButtonUp:
                DispatchSingleClick(_singleClick);
                return true;
            case WmLButtonDblClk:
                return true; // 双击无动作：只唤出逻辑已由单击承担，避免切换显隐
            case WmRButtonUp:
                ShowMenu();
                return true;
            default:
                return true;
        }
    }

    private void ShowMenu()
    {
        GetCursorPos(out var pos);
        var command = (int)TrackPopupMenu(_menu, TpmReturnCmd | TpmRightAlign | TpmBottomAlign, pos.X, pos.Y, 0, _windowHandle, IntPtr.Zero);
        HandleMenuCommand(command);
    }

    /// <summary>单击分发（纯逻辑，可单测）：有自定义动作则执行并返回真，否则返回假。</summary>
    public static bool DispatchSingleClick(Action? singleClick)
    {
        if (singleClick == null) return false;
        singleClick();
        return true;
    }

    /// <summary>托盘菜单分发（纯逻辑，可单测）：左键与菜单项只调对应动作，不附带拉起等副作用。</summary>
    public void HandleMenuCommand(int command) => TryDispatchMenu(command, _quit, _openWorkspace, _restartWorkspace);

    /// <summary>菜单分发表：未知 id 返回 false，已知 id 执行对应动作（动作为空时跳过）。</summary>
    public static bool TryDispatchMenu(int command, Action quit, Action? openWorkspace, Action? restartWorkspace)
    {
        switch (command)
        {
            case MenuOpenWorkspace:
                openWorkspace?.Invoke();
                return true;
            case MenuRestartWorkspace:
                restartWorkspace?.Invoke();
                return true;
            case MenuQuit:
                quit();
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        Shell_NotifyIconW(NimDelete, ref _nid);
        DestroyMenu(_menu);
        DestroyIcon(_icon);
    }

    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifInfo = 0x10;
    private const uint NimAdd = 0x0;
    private const uint NimModify = 0x1;
    private const uint NimDelete = 0x2;
    private const uint NiifWarning = 0x2;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightAlign = 0x0008;
    private const uint TpmBottomAlign = 0x0020;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point pos);
}
