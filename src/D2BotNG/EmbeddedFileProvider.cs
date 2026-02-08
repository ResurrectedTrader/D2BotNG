using System.Collections;
using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace D2BotNG;

/// <summary>
/// File provider that serves files from embedded resources.
/// Resources are expected to be embedded with the "wwwroot" prefix.
/// </summary>
public class EmbeddedResourceFileProvider : IFileProvider
{
    private readonly Assembly _assembly;
    private readonly string _baseNamespace;
    private readonly Dictionary<string, string> _resourceMap;

    public EmbeddedResourceFileProvider(Assembly assembly, string baseNamespace = "wwwroot")
    {
        _assembly = assembly;
        _baseNamespace = $"{assembly.GetName().Name}.{baseNamespace}";
        _resourceMap = BuildResourceMap();
    }

    private Dictionary<string, string> BuildResourceMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resources = _assembly.GetManifestResourceNames();
        var prefix = _baseNamespace + ".";

        foreach (var resource in resources)
        {
            if (resource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Convert resource name back to path
                // e.g., "D2BotNG.wwwroot.assets.index.js" -> "/assets/index.js"
                var relativePath = resource.Substring(prefix.Length);
                var path = "/" + ConvertResourceNameToPath(relativePath);
                map[path] = resource;
            }
        }

        return map;
    }

    private static string ConvertResourceNameToPath(string resourceName)
    {
        // Resource names use dots as separators, but we need to preserve dots in filenames
        // The last segment with a dot before the extension is the filename
        // e.g., "assets.index-ABC123.js" -> "assets/index-ABC123.js"

        var parts = resourceName.Split('.');
        if (parts.Length <= 1)
            return resourceName;

        // Find where the filename starts (look for common file extensions)
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "html", "htm", "js", "css", "json", "png", "jpg", "jpeg", "gif", "svg",
            "ico", "woff", "woff2", "ttf", "eot", "otf", "map", "txt", "xml",
            "dc6", "dat", "PL2", "pl2"
        };

        // Work backwards to find the extension
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (extensions.Contains(parts[i]))
            {
                // Everything before index i-1 is the path, i-1 is filename, i is extension
                var pathParts = parts.Take(i).ToList();
                var filename = parts[i - 1] + "." + parts[i];

                // Handle multiple extensions like .min.js
                if (i < parts.Length - 1)
                {
                    filename = string.Join(".", parts.Skip(i - 1));
                }

                if (pathParts.Count > 0)
                {
                    pathParts[^1] = filename;
                    return string.Join("/", pathParts.Take(pathParts.Count - 1).Append(filename));
                }
                return filename;
            }
        }

        // Fallback: treat last part as extension, second-to-last as filename
        if (parts.Length >= 2)
        {
            var path = string.Join("/", parts.Take(parts.Length - 2));
            var filename = parts[^2] + "." + parts[^1];
            return string.IsNullOrEmpty(path) ? filename : path + "/" + filename;
        }

        return resourceName.Replace('.', '/');
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        if (string.IsNullOrEmpty(subpath))
            return new NotFoundFileInfo(subpath);

        var normalizedPath = "/" + subpath.TrimStart('/').Replace('\\', '/');

        if (_resourceMap.TryGetValue(normalizedPath, out var resourceName))
        {
            return new EmbeddedResourceFileInfo(_assembly, resourceName, GetFileName(normalizedPath));
        }

        return new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var normalizedPath = "/" + subpath.TrimStart('/').Replace('\\', '/').TrimEnd('/');
        if (normalizedPath == "/")
            normalizedPath = "";

        var files = _resourceMap
            .Where(kvp => kvp.Key.StartsWith(normalizedPath + "/", StringComparison.OrdinalIgnoreCase) ||
                          (string.IsNullOrEmpty(normalizedPath) && kvp.Key.StartsWith("/")))
            .Select(kvp => new EmbeddedResourceFileInfo(_assembly, kvp.Value, GetFileName(kvp.Key)))
            .Cast<IFileInfo>()
            .ToList();

        return files.Count > 0
            ? new EnumerableDirectoryContents(files)
            : NotFoundDirectoryContents.Singleton;
    }

    public IChangeToken Watch(string filter)
    {
        // Embedded resources don't change at runtime
        return NullChangeToken.Singleton;
    }

    private static string GetFileName(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
    }
}

internal class EmbeddedResourceFileInfo : IFileInfo
{
    private readonly Assembly _assembly;
    private readonly string _resourceName;

    public EmbeddedResourceFileInfo(Assembly assembly, string resourceName, string name)
    {
        _assembly = assembly;
        _resourceName = resourceName;
        Name = name;

        using var stream = _assembly.GetManifestResourceStream(_resourceName);
        Length = stream?.Length ?? 0;
        Exists = stream != null;
    }

    public bool Exists { get; }
    public long Length { get; }
    public string? PhysicalPath => null;
    public string Name { get; }
    public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
    public bool IsDirectory => false;

    public Stream CreateReadStream()
    {
        var stream = _assembly.GetManifestResourceStream(_resourceName);
        if (stream == null)
            throw new FileNotFoundException($"Embedded resource not found: {_resourceName}");
        return stream;
    }
}

internal class EnumerableDirectoryContents : IDirectoryContents
{
    private readonly IEnumerable<IFileInfo> _files;

    public EnumerableDirectoryContents(IEnumerable<IFileInfo> files)
    {
        _files = files;
    }

    public bool Exists => true;

    public IEnumerator<IFileInfo> GetEnumerator() => _files.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
