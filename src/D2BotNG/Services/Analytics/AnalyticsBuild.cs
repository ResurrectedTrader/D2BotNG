using System.Reflection;

namespace D2BotNG.Services.Analytics;

/// <summary>
/// Build-time analytics configuration. The Aptabase app key is supplied only by CI
/// (<c>-p:AptabaseAppKey=</c> from the APTABASE_APP_KEY secret) and surfaced as assembly
/// metadata, so it is never committed. An absent key means this build reports nothing —
/// which is why it also drives whether the UI offers the opt-out at all.
/// </summary>
internal static class AnalyticsBuild
{
    public static string AppKey { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "AptabaseAppKey")?.Value ?? "";

    public static bool IsConfigured => !string.IsNullOrEmpty(AppKey);
}
