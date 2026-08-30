using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace TimeLens.TrayApp;

public sealed class ToastWindow : IDisposable
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int ULW_ALPHA = 2;
    private const int AC_SRC_OVER = 0;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_TIMER = 0x0113;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private IntPtr _hWnd;
    private readonly string _title;
    private readonly string _body;
    private readonly Image? _image;
    private readonly int _width = 420;
    private readonly int _height = 108;
    private readonly int _dismissMs = 6500;
    private int _x;
    private int _y;
    private const int DISMISS_TIMER_ID = 1;

    private static readonly uint BgColor = 0xFF1A1A1A;
    private static readonly uint AccentColor = 0xFFC8E86A;
    private static readonly uint TextColor = 0xFFFFFFFF;
    private static readonly uint SubTextColor = 0xFFAAAAAA;
    private const int FW_SEMIBOLD = 600;
    private const int TRANSPARENT = 1;

    private static bool _classRegistered;
    private static readonly object _classLock = new();
    private static readonly WndProc StaticWndProcDelegate = StaticWndProc;

    public ToastWindow(string title, string text, string? imagePath = null)
    {
        _title = title;
        _body = text;
        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            try
            {
                // Clone from memory so replacing/removing the selected image never leaves
                // the file locked while an older toast is still visible.
                var bytes = File.ReadAllBytes(imagePath);
                using var stream = new MemoryStream(bytes, writable: false);
                using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                _image = new Bitmap(source);
            }
            catch { _image = null; }
        }
        try
        {
            lock (_classLock) RegisterClass();
            CreateToast();
        }
        catch
        {
            _image?.Dispose();
            throw;
        }
    }

    private static void RegisterClass()
    {
        if (_classRegistered) return;

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 3,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(StaticWndProcDelegate),
            hInstance = GetModuleHandleW(null),
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszClassName = "TLToast",
        };
        var atom = RegisterClassExW(ref wc);
        if (atom == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the TimeLens toast window class.");
        _classRegistered = true;
    }

    private void CreateToast()
    {
        const int margin = 22;
        var workArea = new RECT();
        if (!SystemParametersInfoW(0x0030, 0, ref workArea, 0)) // SPI_GETWORKAREA
            workArea = new RECT { right = GetSystemMetrics(0), bottom = GetSystemMetrics(1) };
        _x = workArea.left + margin;
        _y = workArea.bottom - _height - margin;

        _hWnd = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_NOACTIVATE,
            "TLToast", "", WS_POPUP,
            _x, _y, _width, _height,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);

        if (_hWnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the TimeLens toast window.");

        // Rounded corners
        var rgn = CreateRoundRectRgn(0, 0, _width + 1, _height + 1, 16, 16);
        if (SetWindowRgn(_hWnd, rgn, 1) == 0) DeleteObject(rgn);

        SetWindowLongPtrW(_hWnd, GWLP_USERDATA, GCHandle.ToIntPtr(GCHandle.Alloc(this)));

        Paint(220);
        ShowWindow(_hWnd, 4); // SW_SHOWNOACTIVATE
        SetTimer(_hWnd, DISMISS_TIMER_ID, (uint)_dismissMs, IntPtr.Zero);
    }

    private void Paint(byte alpha)
    {
        if (_hWnd == IntPtr.Zero) return;

        var hdcScreen = GetDC(IntPtr.Zero);
        var hdcMem = CreateCompatibleDC(hdcScreen);
        var hBitmap = CreateCompatibleBitmap(hdcScreen, _width, _height);
        var oldBitmap = SelectObject(hdcMem, hBitmap);

        // Background
        var bgBrush = CreateSolidBrush(BgColor);
        var bgRect = new RECT { right = _width, bottom = _height };
        FillRect(hdcMem, ref bgRect, bgBrush);
        DeleteObject(bgBrush);

        // Accent bar
        var accentBrush = CreateSolidBrush(AccentColor);
        var accentRect = new RECT { right = 4, bottom = _height };
        FillRect(hdcMem, ref accentRect, accentBrush);
        DeleteObject(accentBrush);

        SetBkMode(hdcMem, TRANSPARENT);

        var textLeft = _image is null ? 22 : 104;
        if (_image is not null)
        {
            using var graphics = Graphics.FromHdc(hdcMem);
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var destination = new Rectangle(20, 20, 68, 68);
            using var clip = new GraphicsPath();
            const int imageRadius = 10;
            var arc = imageRadius * 2;
            clip.AddArc(destination.Left, destination.Top, arc, arc, 180, 90);
            clip.AddArc(destination.Right - arc, destination.Top, arc, arc, 270, 90);
            clip.AddArc(destination.Right - arc, destination.Bottom - arc, arc, arc, 0, 90);
            clip.AddArc(destination.Left, destination.Bottom - arc, arc, arc, 90, 90);
            clip.CloseFigure();
            graphics.SetClip(clip);

            var sourceRatio = (float)_image.Width / _image.Height;
            var destinationRatio = (float)destination.Width / destination.Height;
            RectangleF source;
            if (sourceRatio > destinationRatio)
            {
                var sourceWidth = _image.Height * destinationRatio;
                source = new RectangleF((_image.Width - sourceWidth) / 2f, 0, sourceWidth, _image.Height);
            }
            else
            {
                var sourceHeight = _image.Width / destinationRatio;
                source = new RectangleF(0, (_image.Height - sourceHeight) / 2f, _image.Width, sourceHeight);
            }
            graphics.DrawImage(_image, destination, source, GraphicsUnit.Pixel);
            graphics.ResetClip();
        }

        // Title
        var titleFont = CreateFontW(17, 0, 0, 0, FW_SEMIBOLD, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
        var oldFont = SelectObject(hdcMem, titleFont);
        SetTextColor(hdcMem, TextColor);
        var tr = new RECT { left = textLeft, top = 17, right = _width - 20, bottom = 42 };
        DrawTextW(hdcMem, _title, -1, ref tr, 0x0020 | 0x0800 | 0x8000);
        SelectObject(hdcMem, oldFont);
        DeleteObject(titleFont);

        // Body
        var bodyFont = CreateFontW(14, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
        var oldFont2 = SelectObject(hdcMem, bodyFont);
        SetTextColor(hdcMem, SubTextColor);
        var br = new RECT { left = textLeft, top = 47, right = _width - 20, bottom = _height - 16 };
        DrawTextW(hdcMem, _body, -1, ref br, 0x0010 | 0x0800 | 0x8000);
        SelectObject(hdcMem, oldFont2);
        DeleteObject(bodyFont);

        // Update layered
        var dst = new POINT { x = _x, y = _y };
        var sz = new SIZE { cx = _width, cy = _height };
        var src = new POINT();
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = 0 };
        UpdateLayeredWindow(_hWnd, hdcScreen, ref dst, ref sz, hdcMem, ref src, 0, ref blend, ULW_ALPHA);
        // SWP_SHOWWINDOW is required because CreateWindowEx creates the popup hidden.
        SetWindowPos(_hWnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0040 | 0x0010 | 0x0002 | 0x0001);

        SelectObject(hdcMem, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_LBUTTONDOWN)
        {
            DestroyWindow(hWnd);
            return IntPtr.Zero;
        }
        if (msg == WM_TIMER && (int)wParam == DISMISS_TIMER_ID)
        {
            KillTimer(hWnd, DISMISS_TIMER_ID);
            DestroyWindow(hWnd);
            return IntPtr.Zero;
        }
        if (msg == 0x0002) // WM_DESTROY
        {
            var p = GetWindowLongPtrW(hWnd, GWLP_USERDATA);
            if (p != IntPtr.Zero)
            {
                var handle = GCHandle.FromIntPtr(p);
                if (handle.Target is ToastWindow toast) toast.OnDestroyed();
                handle.Free();
            }
            SetWindowLongPtrW(hWnd, GWLP_USERDATA, IntPtr.Zero);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose() { if (_hWnd != IntPtr.Zero) { DestroyWindow(_hWnd); _hWnd = IntPtr.Zero; } }

    private void OnDestroyed()
    {
        _hWnd = IntPtr.Zero;
        _image?.Dispose();
    }

    // P/Invoke declarations
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint c);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int m);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint c);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFontW(int h, int w, int e, int o, int wt, int i, int u, int s, int cs, int op, int cp, int q, int pi, string f);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(IntPtr hdc, string t, int l, ref RECT r, uint f);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
    [DllImport("gdi32.dll")] private static extern int DeleteDC(IntPtr hdc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(int ex, string cn, string wn, int st, int x, int y, int w, int h, IntPtr hp, IntPtr hm, IntPtr hi, IntPtr pv);
    [DllImport("user32.dll")] private static extern int DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern int SetWindowPos(IntPtr hWnd, IntPtr hAfter, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] private static extern int UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pd, ref SIZE ps, IntPtr hdcSrc, ref POINT pSrc, int crKey, ref BLENDFUNCTION blend, int f);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, int bRedraw);
    [DllImport("user32.dll")] private static extern int SetTimer(IntPtr hWnd, int id, uint ms, IntPtr tp);
    [DllImport("user32.dll")] private static extern int KillTimer(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int n);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfoW(uint action, uint param, ref RECT value, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int i);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? n);

    private struct RECT { public int left, top, right, bottom; }
    private struct POINT { public int x, y; }
    private struct SIZE { public int cx, cy; }
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW { public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public IntPtr hIconSm; }
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private const int GWLP_USERDATA = -21;
}
