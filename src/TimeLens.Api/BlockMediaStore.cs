using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace TimeLens.Api;

internal static class BlockMediaStore
{
    [SupportedOSPlatform("windows6.1")]
    public static void SaveBlockImage(byte[] bytes, string destinationPath)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var source = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
        ValidateImageDimensions(source);

        const int size = 192;
        using var output = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            var scale = Math.Min((double)size / source.Width, (double)size / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = destinationPath + ".new";
        try
        {
            output.Save(temporaryPath, ImageFormat.Png);
            File.Move(temporaryPath, destinationPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static string? MediaTypeFromDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return null;
        foreach (var mediaType in new[] { "image/png", "image/jpeg", "image/gif", "video/mp4", "video/webm" })
            if (dataUrl.StartsWith($"data:{mediaType};base64,", StringComparison.OrdinalIgnoreCase)) return mediaType;
        return null;
    }

    public static byte[] DecodeDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0) throw new FormatException();
        return Convert.FromBase64String(dataUrl[(comma + 1)..]);
    }

    public static string BlockMediaPath(string directory, string? mediaType) => Path.Combine(directory, mediaType switch
    {
        "image/jpeg" => "block-notification-media.jpg",
        "image/gif" => "block-notification-media.gif",
        "video/mp4" => "block-notification-media.mp4",
        "video/webm" => "block-notification-media.webm",
        _ => "block-notification-media.png"
    });

    [SupportedOSPlatform("windows6.1")]
    public static void SaveBlockMedia(byte[] bytes, string mediaType, string directory, byte[]? posterBytes)
    {
        if (mediaType.StartsWith("image/", StringComparison.Ordinal))
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            ValidateImageDimensions(source);
        }
        else if (mediaType == "video/mp4")
        {
            if (bytes.Length < 12 || !System.Text.Encoding.ASCII.GetString(bytes, 4, 4).Equals("ftyp", StringComparison.Ordinal))
                throw new ArgumentException("The selected MP4 file is invalid");
        }
        else if (mediaType == "video/webm")
        {
            if (bytes.Length < 4 || bytes[0] != 0x1A || bytes[1] != 0x45 || bytes[2] != 0xDF || bytes[3] != 0xA3)
                throw new ArgumentException("The selected WebM file is invalid");
        }

        Directory.CreateDirectory(directory);
        var destination = BlockMediaPath(directory, mediaType);
        var temporary = destination + ".new";
        var posterDestination = Path.Combine(directory, "block-notification-poster.png");
        var posterTemporary = Path.Combine(directory, "block-notification-poster.pending.png");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (posterBytes is not null) SaveBlockImage(posterBytes, posterTemporary);
            DeleteBlockMediaFiles(directory);
            File.Move(temporary, destination, true);
            if (posterBytes is not null) File.Move(posterTemporary, posterDestination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(posterTemporary)) File.Delete(posterTemporary);
        }
    }

    public static void DeleteBlockMediaFiles(string directory)
    {
        foreach (var name in new[]
        {
            "block-notification.png", "block-notification-media.png", "block-notification-media.jpg",
            "block-notification-media.gif", "block-notification-media.mp4", "block-notification-media.webm",
            "block-notification-poster.png"
        })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [SupportedOSPlatform("windows6.1")]
    private static void ValidateImageDimensions(Image source)
    {
        if (source.Width < 1 || source.Height < 1 || source.Width > 4096 || source.Height > 4096 ||
            (long)source.Width * source.Height > 16_000_000)
            throw new ArgumentException("Image dimensions must be between 1 and 4096 pixels");
    }
}
