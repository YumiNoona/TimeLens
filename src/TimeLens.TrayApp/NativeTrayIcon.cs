using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace TimeLens.TrayApp;

public sealed class NativeTrayIcon : IDisposable
{
    private const uint WM_USER = 0x0400;
    private const uint WM_APP = 0x8000;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_TIMER = 0x0113;
    private const uint NIN_SELECT = WM_USER;
    private const uint NIN_KEYSELECT = WM_USER + 1;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint NIF_MESSAGE = 1;
    private const uint NIF_ICON = 2;
    private const uint NIF_TIP = 4;
    private const uint NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x01;
    private const uint NIIF_WARNING = 0x02;
    private const uint NIS_HIDDEN = 8;

    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly UIntPtr TrayRetryTimerId = new(1);

    private const uint MF_STRING = 0;
    private const uint TPM_LEFTALIGN = 0;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RETURNCMD = 0x0100;

    // Custom message IDs for menu items
    private const uint ID_OPEN_DASHBOARD = WM_APP + 1;
    private const uint ID_INSTALL_EXTENSION = WM_APP + 2;
    private const uint ID_EXIT = WM_APP + 3;

    private const uint WM_STARTUP = WM_APP + 100;

    private IntPtr _hWnd;
    private IntPtr _hMenu;
    private IntPtr _hIcon;
    private uint _taskbarCreatedMessage;
    private WndProc? _wndProc;
    private bool _iconAdded;
    private bool _disposed;
    private ExceptionDispatchInfo? _callbackError;
    private readonly string _iconPath;

    public NativeTrayIcon(string? iconPath = null)
    {
        _iconPath = iconPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeLens", "runtime", "TimeLens.ico");
    }

    public event Action? OpenDashboardRequested;
    public event Action? InstallExtensionRequested;
    public event Action? ExitRequested;
    public event Action? StartupRequested;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint cmd, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern IntPtr TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImageFromFile(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    private const uint IMAGE_ICON = 1;
    private const uint LR_DEFAULTSIZE = 0x0040;
    private const uint LR_LOADFROMFILE = 0x0010;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint interval, IntPtr timerProc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hWnd, UIntPtr id);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    private const string WindowClass = "TimeLensHiddenWindow";
    private const uint TrayIconId = 100;

    public void Run()
    {
        var hInstance = GetModuleHandleW(null);

        _wndProc = new WndProc(WindowProcedure);
        var wcex = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = WindowClass,
        };

        var atom = RegisterClassExW(ref wcex);
        if (atom == 0)
            throw new InvalidOperationException("Failed to register window class.");

        // A message-only window never receives the TaskbarCreated broadcast. Use an
        // invisible top-level tool window so Explorer restarts restore our icon.
        _hWnd = CreateWindowExW(
            WS_EX_TOOLWINDOW, WindowClass, "TimeLens",
            0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hWnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create hidden window.");

        _hIcon = LoadImageFromFile(IntPtr.Zero, _iconPath, IMAGE_ICON, 0, 0, LR_DEFAULTSIZE | LR_LOADFROMFILE);
        if (_hIcon == IntPtr.Zero)
            _hIcon = LoadImageW(IntPtr.Zero, new IntPtr(32512), IMAGE_ICON, 0, 0, LR_DEFAULTSIZE); // IDI_APPLICATION

        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        AddTrayIcon();

        _hMenu = CreatePopupMenu();
        AppendMenuW(_hMenu, MF_STRING, ID_OPEN_DASHBOARD, "Open Dashboard");
        AppendMenuW(_hMenu, MF_STRING, ID_INSTALL_EXTENSION, "Install Browser Extension");
        AppendMenuW(_hMenu, MF_STRING, ID_EXIT, "Exit");

        // Post startup message — processed inside the message loop so watchers
        // start after the message pump is running (required for WinEvent hooks).
        PostMessageW(_hWnd, WM_STARTUP, IntPtr.Zero, IntPtr.Zero);

        // Message loop
        int result;
        while ((result = GetMessageW(out var msg, IntPtr.Zero, 0, 0)) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        _callbackError?.Throw();
        if (result == -1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The tray message loop failed.");
    }

    public void ShowBalloon(string title, string text, bool warning = false)
    {
        try
        {
            _ = new ToastWindow(title, text);
        }
        catch { }
    }

    public void Close()
    {
        if (_hWnd != IntPtr.Zero)
            PostMessageW(_hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private void AddTrayIcon()
    {
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_USER,
            hIcon = _hIcon,
            szTip = "TimeLens",
        };
        if (!Shell_NotifyIconW(NIM_ADD, ref nid))
        {
            // Explorer may not be ready yet during sign-in. Keep the message pump
            // alive and retry without blocking startup or throwing across WndProc.
            if (SetTimer(_hWnd, TrayRetryTimerId, 2000, IntPtr.Zero) == UIntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not schedule tray icon recovery.");
            return;
        }
        _iconAdded = true;
        KillTimer(_hWnd, TrayRetryTimerId);

        nid.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref nid);
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded || _hWnd == IntPtr.Zero) return;
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
        };
        Shell_NotifyIconW(NIM_DELETE, ref nid);
        _iconAdded = false;
    }

    private IntPtr WindowProcedure(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return HandleMessage(hWnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Managed exceptions must not unwind through the native callback. Rethrow
            // after the loop so Program can write the crash log and show an error.
            _callbackError ??= ExceptionDispatchInfo.Capture(ex);
            PostQuitMessage(1);
            return IntPtr.Zero;
        }
    }

    private IntPtr HandleMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            _iconAdded = false;
            AddTrayIcon();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WM_USER:
                var rawNotification = unchecked((ulong)lParam.ToInt64());
                var version4Id = (uint)((rawNotification >> 16) & 0xFFFF);
                var trayId = version4Id != 0 ? version4Id : unchecked((uint)wParam.ToInt64());
                if (trayId == TrayIconId)
                {
                    var notifyMsg = version4Id != 0
                        ? (uint)(rawNotification & 0xFFFF)
                        : unchecked((uint)lParam.ToInt64());
                    switch (notifyMsg)
                    {
                        case WM_RBUTTONUP:
                        case WM_CONTEXTMENU:
                            ShowContextMenu();
                            break;
                        case WM_LBUTTONUP when version4Id == 0:
                        case NIN_SELECT:
                        case NIN_KEYSELECT:
                            OpenDashboardRequested?.Invoke();
                            break;
                    }
                }
                return IntPtr.Zero;

            case WM_COMMAND:
                var cmdId = (uint)wParam;
                if (cmdId == ID_OPEN_DASHBOARD)
                    OpenDashboardRequested?.Invoke();
                else if (cmdId == ID_INSTALL_EXTENSION)
                    InstallExtensionRequested?.Invoke();
                else if (cmdId == ID_EXIT)
                    ExitRequested?.Invoke();
                return IntPtr.Zero;

            case WM_TIMER when unchecked((ulong)wParam.ToInt64()) == TrayRetryTimerId.ToUInt64():
                if (!_iconAdded) AddTrayIcon();
                return IntPtr.Zero;

            case WM_DESTROY:
                KillTimer(hWnd, TrayRetryTimerId);
                RemoveTrayIcon();
                PostQuitMessage(0);
                return IntPtr.Zero;

            case WM_CLOSE:
                RemoveTrayIcon();
                DestroyWindow(hWnd);
                _hWnd = IntPtr.Zero;
                return IntPtr.Zero;

            case WM_STARTUP:
                StartupRequested?.Invoke();
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        SetForegroundWindow(_hWnd);
        GetCursorPos(out var pt);
        TrackPopupMenu(_hMenu, TPM_LEFTALIGN | TPM_BOTTOMALIGN, pt.x, pt.y, 0, _hWnd, IntPtr.Zero);
        PostMessageW(_hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_NULL = 0x0000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hWnd != IntPtr.Zero)
        {
            RemoveTrayIcon();
            DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }

        if (_hMenu != IntPtr.Zero)
            DestroyMenu(_hMenu);

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        _wndProc = null;
    }
}
