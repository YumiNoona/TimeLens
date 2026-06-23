using System.Drawing;
using System.Drawing.Imaging;

var sourcePath = args[0];
var outDir = args[1];

var sizes = new[] { 16, 48, 128 };

using var src = new Bitmap(sourcePath);
foreach (var size in sizes)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
    g.DrawImage(src, 0, 0, size, size);
    bmp.Save(Path.Combine(outDir, $"icon-{size}.png"), ImageFormat.Png);
}
Console.WriteLine($"Generated {sizes.Length} icons in {outDir}");
