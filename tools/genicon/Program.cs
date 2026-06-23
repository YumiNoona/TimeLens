using System.Drawing;
using System.Drawing.Imaging;

var size = 32;
using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
using var g = Graphics.FromImage(bmp);
g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

// Transparent background
g.Clear(Color.Transparent);

// Outer ring (dark green)
using var outerPen = new Pen(Color.FromArgb(0x7E, 0xC8, 0x4A), 2.5f);
g.DrawEllipse(outerPen, 3, 3, 26, 26);

// Inner circle (bright green #C8E86A)
using var innerBrush = new SolidBrush(Color.FromArgb(0xC8, 0xE8, 0x6A));
g.FillEllipse(innerBrush, 6, 6, 20, 20);

// Center dot (dark green)
using var dotBrush = new SolidBrush(Color.FromArgb(0x2E, 0x3F, 0x1A));
g.FillEllipse(dotBrush, 13, 13, 6, 6);

// Save as icon
using var fs = new FileStream(args[0], FileMode.Create);
using var bw = new BinaryWriter(fs);

// ICO header
bw.Write((short)0);    // reserved
bw.Write((short)1);    // type = icon
bw.Write((short)1);    // count = 1

// Directory entry (will be overwritten after we know sizes)
long dirPos = fs.Position;
bw.Write((byte)32);    // width
bw.Write((byte)32);    // height
bw.Write((byte)0);     // colors
bw.Write((byte)0);     // reserved
bw.Write((short)1);    // planes
bw.Write((short)32);   // bpp
bw.Write(0);           // image size (placeholder)
bw.Write(0);           // image offset (placeholder)

// Convert bitmap to BGRA pixel data (top-down for ICO)
long imageStart = fs.Position;
var bmpData = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
var pixels = new byte[size * size * 4];
System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);
bmp.UnlockBits(bmpData);

// ICO XOR mask: BGRA pixels, bottom-up
for (int y = size - 1; y >= 0; y--)
{
    bw.Write(pixels, y * size * 4, size * 4);
}

// ICO AND mask: 1 bit per pixel, 1 = transparent
for (int y = size - 1; y >= 0; y--)
{
    for (int x = 0; x < size; x += 8)
    {
        byte maskByte = 0;
        for (int b = 0; b < 8 && x + b < size; b++)
        {
            int idx = (y * size + x + b) * 4;
            if (pixels[idx + 3] < 128)
                maskByte |= (byte)(1 << (7 - b));
        }
        bw.Write(maskByte);
    }
}

long endPos = fs.Position;
long imageSize = endPos - imageStart;

// Go back and fill in directory entry
fs.Seek(dirPos + 8, SeekOrigin.Begin);
bw.Write((int)imageSize);
bw.Write((int)imageStart);

Console.WriteLine($"Icon created: {args[0]} ({endPos} bytes)");
