using System.Runtime.InteropServices;

namespace TimeLens.TrayApp;

public sealed class NativeTrayIcon : IDisposable
{
    private const uint WM_USER = 0x0400;
    private const uint WM_APP = 0x8000;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONUP = 0x0202;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1;
    private const uint NIF_ICON = 2;
    private const uint NIF_TIP = 4;
    private const uint NIS_HIDDEN = 8;

    private const uint MF_STRING = 0;
    private const uint TPM_LEFTALIGN = 0;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RETURNCMD = 0x0100;

    // Custom message IDs for menu items
    private const uint ID_OPEN_DASHBOARD = WM_APP + 1;
    private const uint ID_EXIT = WM_APP + 2;

    private IntPtr _hWnd;
    private IntPtr _hMenu;
    private bool _disposed;

    public event Action? OpenDashboardRequested;
    public event Action? ExitRequested;

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

    [DllImport("user32.dll")]
    private static extern bool GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostQuitMessage(int nExitCode);

    private const string WindowClass = "TimeLensHiddenWindow";
    private const uint TrayIconId = 100;

    public void Run()
    {
        var hInstance = GetModuleHandleW(null);

        var wndProc = new WndProc(WindowProcedure);
        var wcex = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = wndProc,
            hInstance = hInstance,
            lpszClassName = WindowClass,
        };

        var atom = RegisterClassExW(ref wcex);
        if (atom == 0)
            throw new InvalidOperationException("Failed to register window class.");

        _hWnd = CreateWindowExW(
            0, WindowClass, "TimeLens",
            0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hWnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create hidden window.");

        var iconPath = Path.Combine(AppContext.BaseDirectory, "TimeLens.ico");
        var hIcon = LoadImageFromFile(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_DEFAULTSIZE | LR_LOADFROMFILE);
        if (hIcon == IntPtr.Zero)
            hIcon = LoadImageW(IntPtr.Zero, new IntPtr(32512), IMAGE_ICON, 0, 0, LR_DEFAULTSIZE); // IDI_APPLICATION

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_USER,
            hIcon = hIcon,
            szTip = "TimeLens",
        };
        if (!Shell_NotifyIconW(NIM_ADD, ref nid))
            throw new InvalidOperationException("Failed to create tray icon.");

        _hMenu = CreatePopupMenu();
        AppendMenuW(_hMenu, MF_STRING, ID_OPEN_DASHBOARD, "Open Dashboard");
        AppendMenuW(_hMenu, MF_STRING, ID_EXIT, "Exit");

        // Message loop
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private IntPtr WindowProcedure(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_USER:
                var trayId = (uint)wParam;
                if (trayId == TrayIconId)
                {
                    var notifyMsg = (uint)lParam;
                    switch (notifyMsg)
                    {
                        case WM_RBUTTONUP:
                            ShowContextMenu();
                            break;
                        case WM_LBUTTONUP:
                            OpenDashboardRequested?.Invoke();
                            break;
                    }
                }
                return IntPtr.Zero;

            case WM_COMMAND:
                var cmdId = (uint)wParam;
                if (cmdId == ID_OPEN_DASHBOARD)
                    OpenDashboardRequested?.Invoke();
                else if (cmdId == ID_EXIT)
                    ExitRequested?.Invoke();
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
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

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hWnd != IntPtr.Zero)
        {
            var nid = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hWnd,
                uID = TrayIconId,
            };
            Shell_NotifyIconW(NIM_DELETE, ref nid);
            DestroyWindow(_hWnd);
        }

        if (_hMenu != IntPtr.Zero)
            DestroyMenu(_hMenu);
    }
}
