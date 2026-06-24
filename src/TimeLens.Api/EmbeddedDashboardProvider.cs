using System.Collections;
using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace TimeLens.Api;

internal sealed class EmbeddedDashboardProvider : IFileProvider
{
    private readonly Assembly _assembly;
    private readonly string _prefix;
    private readonly Dictionary<string, EmbeddedFileInfo> _files;

    public EmbeddedDashboardProvider(Assembly assembly, string prefix = "dashboard/")
    {
        _assembly = assembly;
        _prefix = prefix;
        _files = new(StringComparer.OrdinalIgnoreCase);

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var path = name[prefix.Length..].Replace('\\', '/');
            _files[path] = new EmbeddedFileInfo(assembly, name, path);
        }
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var key = subpath.TrimStart('/').Replace('\\', '/');
        if (_files.TryGetValue(key, out var info))
            return info;
        return new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var dir = subpath.TrimStart('/').Replace('\\', '/').TrimEnd('/');
        var dirPrefix = dir.Length > 0 ? dir + "/" : "";

        var matches = new List<IFileInfo>();
        foreach (var (path, info) in _files)
        {
            if (!path.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = path[dirPrefix.Length..];
            if (!relative.Contains('/'))
                matches.Add(info);
        }

        if (matches.Count > 0)
            return new ListDirectoryContents(matches);
        return NotFoundDirectoryContents.Singleton;
    }

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
}

internal sealed class EmbeddedFileInfo : IFileInfo
{
    private readonly Assembly _assembly;
    private readonly string _resourceName;
    private long? _length;

    public EmbeddedFileInfo(Assembly assembly, string resourceName, string path)
    {
        _assembly = assembly;
        _resourceName = resourceName;
        Name = Path.GetFileName(path);
    }

    public bool Exists => true;
    public bool IsDirectory => false;
    public long Length
    {
        get
        {
            if (_length is null)
            {
                using var s = _assembly.GetManifestResourceStream(_resourceName);
                _length = s?.Length ?? 0;
            }
            return _length.Value;
        }
    }
    public string? PhysicalPath => null;
    public DateTimeOffset LastModified => DateTimeOffset.MinValue;
    public string Name { get; }

    public Stream CreateReadStream()
    {
        return _assembly.GetManifestResourceStream(_resourceName)
            ?? throw new FileNotFoundException($"Embedded resource not found: {_resourceName}");
    }
}

internal sealed class ListDirectoryContents : IDirectoryContents
{
    private readonly List<IFileInfo> _entries;
    public ListDirectoryContents(List<IFileInfo> entries) => _entries = entries;
    public bool Exists => true;
    public IEnumerator<IFileInfo> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();
}
