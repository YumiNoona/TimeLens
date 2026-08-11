using System.IO.Compression;
using System.Reflection;

namespace TimeLens.Api;

internal static class ExtensionPackageProvider
{
    public static byte[] CreateZip(Assembly assembly, string family)
    {
        var prefix = $"extensions/{family}/";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resources.Length == 0)
            throw new FileNotFoundException($"Embedded {family} extension package was not found.");

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var resourceName in resources)
            {
                var entryName = resourceName[prefix.Length..].Replace('\\', '/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var source = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new FileNotFoundException($"Embedded extension file is missing: {resourceName}");
                using var target = entry.Open();
                source.CopyTo(target);
            }
        }
        return output.ToArray();
    }
}
