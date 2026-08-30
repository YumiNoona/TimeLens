using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TimeLens.TrayApp;

public sealed class ToastWindow : IDisposable
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int GWLP_USERDATA = -21;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_SHOWNOACTIVATE = 4;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly object ClassLock = new();
    private static readonly WndProc StaticWndProcDelegate = StaticWndProc;
    private static bool _classRegistered;

    private readonly string _title;
    private readonly string _body;
    private readonly Image? _media;
    private readonly bool _animated;
    private readonly EventHandler _animationHandler;
    private readonly Action<ToastWindow>? _closed;
    private readonly int _width = 454;
    private readonly int _height = 118;
    private string _position;
    private IntPtr _hWnd;
    private bool _resourcesDisposed;

    public ToastWindow(
        string title,
        string text,
        string? imagePath = null,
        int stackIndex = 0,
        string position = "left",
        Action<ToastWindow>? closed = null)
    {
        _title = string.IsNullOrWhiteSpace(title) ? "Focus Mode" : title;
        _body = string.IsNullOrWhiteSpace(text) ? "This target is on your focus list." : text;
        _position = position == "right" ? "right" : "left";
        _closed = closed;
        _animationHandler = (_, _) => { if (_hWnd != IntPtr.Zero) InvalidateRect(_hWnd, IntPtr.Zero, false); };
        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            try
            {
                var bytes = File.ReadAllBytes(imagePath);
                using var stream = new MemoryStream(bytes, writable: false);
                using var source = Image.FromStream(stream, false, true);
                _media = (Image)source.Clone();
                _animated = ImageAnimator.CanAnimate(_media);
            }
            catch { _media = null; _animated = false; }
        }

        try
        {
            lock (ClassLock) RegisterClass();
            CreateToast(stackIndex);
            if (_animated && _media is not null) ImageAnimator.Animate(_media, _animationHandler);
        }
        catch
        {
            DisposeMedia();
            throw;
        }
    }

    public bool IsClosed => _hWnd == IntPtr.Zero;

    public void Reposition(int stackIndex, string position)
    {
        if (_hWnd == IntPtr.Zero) return;
        _position = position == "right" ? "right" : "left";
        var (x, y) = PositionFor(stackIndex, _position);
        SetWindowPos(_hWnd, HWND_TOPMOST, x, y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
            hCursor = LoadCursorW(IntPtr.Zero, new IntPtr(32512)),
            hbrBackground = IntPtr.Zero,
            lpszClassName = "TLToast",
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the TimeLens toast window class.");
        _classRegistered = true;
    }

    private void CreateToast(int stackIndex)
    {
        var (x, y) = PositionFor(stackIndex, _position);
        _hWnd = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            "TLToast", "", WS_POPUP,
            x, y, _width, _height,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (_hWnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the TimeLens toast window.");

        var handle = GCHandle.Alloc(this);
        SetWindowLongPtrW(_hWnd, GWLP_USERDATA, GCHandle.ToIntPtr(handle));
        var region = CreateRoundRectRgn(0, 0, _width + 1, _height + 1, 18, 18);
        if (SetWindowRgn(_hWnd, region, true) == 0) DeleteObject(region);
        ShowWindow(_hWnd, SW_SHOWNOACTIVATE);
        SetWindowPos(_hWnd, HWND_TOPMOST, x, y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private (int X, int Y) PositionFor(int stackIndex, string position)
    {
        const int margin = 22;
        const int gap = 12;
        var work = new RECT();
        if (!SystemParametersInfoW(0x0030, 0, ref work, 0))
            work = new RECT { right = GetSystemMetrics(0), bottom = GetSystemMetrics(1) };
        var perColumn = Math.Max(1, (work.bottom - work.top - (2 * margin) + gap) / (_height + gap));
        var column = Math.Max(0, stackIndex) / perColumn;
        var row = Math.Max(0, stackIndex) % perColumn;
        var x = position == "right"
            ? work.right - margin - _width - column * (_width + gap)
            : work.left + margin + column * (_width + gap);
        var y = work.bottom - margin - _height - row * (_height + gap);
        return (x, y);
    }

    private void Paint()
    {
        if (_hWnd == IntPtr.Zero) return;
        var hdc = BeginPaint(_hWnd, out var paint);
        if (hdc == IntPtr.Zero) return;
        try
        {
            using var graphics = Graphics.FromHdc(hdc);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Color.FromArgb(18, 27, 30));
            using var accent = new SolidBrush(Color.FromArgb(131, 215, 216));
            graphics.FillRectangle(accent, 0, 0, 4, _height);
            using var border = new Pen(Color.FromArgb(70, 126, 215, 218), 1);
            graphics.DrawRoundedRectangle(border, new Rectangle(0, 0, _width - 1, _height - 1), 16);

            var textLeft = 20;
            if (_media is not null)
            {
                if (_animated) ImageAnimator.UpdateFrames(_media);
                var destination = new Rectangle(18, 20, 78, 78);
                using var clip = RoundedPath(destination, 11);
                var graphicsState = graphics.Save();
                graphics.SetClip(clip);
                DrawCover(graphics, _media, destination);
                graphics.Restore(graphicsState);
                textLeft = 112;
            }

            using var eyebrowFont = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
            using var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
            using var bodyFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            using var eyebrowBrush = new SolidBrush(Color.FromArgb(131, 215, 216));
            using var titleBrush = new SolidBrush(Color.FromArgb(246, 251, 250));
            using var bodyBrush = new SolidBrush(Color.FromArgb(194, 209, 206));
            using var textFormat = new StringFormat(StringFormat.GenericTypographic)
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoClip
            };
            graphics.DrawString("TIMELENS  ·  NOTIFY", eyebrowFont, eyebrowBrush, new RectangleF(textLeft, 17, _width - textLeft - 48, 16), textFormat);
            graphics.DrawString(_title, titleFont, titleBrush, new RectangleF(textLeft, 36, _width - textLeft - 48, 24), textFormat);
            graphics.DrawString(_body, bodyFont, bodyBrush, new RectangleF(textLeft, 64, _width - textLeft - 24, 42), textFormat);

            using var closeBackground = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            using var closePen = new Pen(Color.FromArgb(205, 225, 222), 1.7f);
            graphics.FillRoundedRectangle(closeBackground, new Rectangle(_width - 40, 11, 28, 28), 8);
            graphics.DrawLine(closePen, _width - 32, 19, _width - 20, 31);
            graphics.DrawLine(closePen, _width - 20, 19, _width - 32, 31);
        }
        finally { EndPaint(_hWnd, ref paint); }
    }

    private static void DrawCover(Graphics graphics, Image image, Rectangle destination)
    {
        var scale = Math.Max((float)destination.Width / image.Width, (float)destination.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        graphics.DrawImage(image, destination.Left + (destination.Width - width) / 2f,
            destination.Top + (destination.Height - height) / 2f, width, height);
    }

    private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var pointer = GetWindowLongPtrW(hWnd, GWLP_USERDATA);
        var toast = pointer == IntPtr.Zero ? null : GCHandle.FromIntPtr(pointer).Target as ToastWindow;
        switch (message)
        {
            case WM_PAINT:
                toast?.Paint();
                return IntPtr.Zero;
            case WM_ERASEBKGND:
                return new IntPtr(1);
            case WM_LBUTTONUP:
                var packed = unchecked((long)lParam);
                var x = unchecked((short)packed);
                var y = unchecked((short)(packed >> 16));
                if (toast is not null && x >= toast._width - 44 && x <= toast._width - 8 && y >= 7 && y <= 43)
                    DestroyWindow(hWnd);
                return IntPtr.Zero;
            case WM_CLOSE:
                DestroyWindow(hWnd);
                return IntPtr.Zero;
            case WM_DESTROY:
                if (pointer != IntPtr.Zero)
                {
                    SetWindowLongPtrW(hWnd, GWLP_USERDATA, IntPtr.Zero);
                    var handle = GCHandle.FromIntPtr(pointer);
                    var target = handle.Target as ToastWindow;
                    handle.Free();
                    target?.OnDestroyed();
                }
                return IntPtr.Zero;
            default:
                return DefWindowProcW(hWnd, message, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (_hWnd != IntPtr.Zero) DestroyWindow(_hWnd);
        else DisposeMedia();
    }

    private void OnDestroyed()
    {
        _hWnd = IntPtr.Zero;
        DisposeMedia();
        _closed?.Invoke(this);
    }

    private void DisposeMedia()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;
        if (_animated && _media is not null) ImageAnimator.StopAnimate(_media, _animationHandler);
        _media?.Dispose();
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT paint);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfoW(uint action, uint param, ref RECT value, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool redraw);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
}

internal static class ToastGraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle rectangle, int radius)
    {
        using var path = Path(rectangle, radius);
        graphics.DrawPath(pen, path);
    }

    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = Path(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath Path(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
