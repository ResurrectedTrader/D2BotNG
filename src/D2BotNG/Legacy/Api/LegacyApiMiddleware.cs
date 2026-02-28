using System.Text;
using System.Text.Json;
using D2BotNG.Data;
using D2BotNG.Legacy.Models;
using Microsoft.Net.Http.Headers;

namespace D2BotNG.Legacy.Api;

public class LegacyApiMiddleware
{
    private readonly RequestDelegate _next;

    public LegacyApiMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    private static bool IsGrpcOrGrpcWeb(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mt))
            return false;

        var mediaType = mt.MediaType.Value; // e.g. "application/grpc-web+proto"
        return mediaType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public async Task InvokeAsync(HttpContext context, SettingsRepository settingsRepository, LegacyApiHandler handler, SessionManager sessionManager, ILogger<LegacyApiMiddleware> logger)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !(await settingsRepository.GetAsync()).LegacyApi.Enabled || IsGrpcOrGrpcWeb(context.Request))
        {
            await _next(context);
            return;
        }

        // Read body
        context.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        if (string.IsNullOrEmpty(body))
        {
            await _next(context);
            return;
        }

        // Try to decode as legacy request (base64 -> JSON)
        LegacyRequest? request;
        try
        {
            var decoded = Convert.FromBase64String(body);
            var json = Encoding.UTF8.GetString(decoded);
            request = JsonSerializer.Deserialize<LegacyRequest>(json);
        }
        catch
        {
            // Not a valid base64/JSON legacy request, pass through
            await _next(context);
            return;
        }

        if (request == null || string.IsNullOrEmpty(request.Func))
        {
            await _next(context);
            return;
        }

        // It's a legacy request - handle it
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "";
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var sessionKey = sessionManager.GetOrCreateSession(clientIp, userAgent);

        logger.LogDebug("Handling request: {request}", request);
        var response = await handler.HandleAsync(request, sessionKey);
        logger.LogDebug("Sending response: {response}", response);

        var responseJson = JsonSerializer.Serialize(response);
        var responseBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(responseJson));
        var responseBytes = Encoding.UTF8.GetBytes(responseBase64);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/plain";
        await context.Response.Body.WriteAsync(responseBytes);
    }
}
