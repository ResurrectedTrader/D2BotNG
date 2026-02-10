using System.Reflection;

namespace D2BotNG.Utilities;

public static class EmbeddedResourceLoader
{
    private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

    public static byte[] LoadBytes(string resourceName)
    {
        using var stream = Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static Stream? LoadStream(string resourceName)
    {
        return Assembly.GetManifestResourceStream(resourceName);
    }
}
