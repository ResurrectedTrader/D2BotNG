using D2BotNG.Data;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace D2BotNG.Services;

/// <summary>
/// gRPC interceptor that enforces password authentication when configured.
/// </summary>
public class AuthInterceptor : Interceptor
{
    private readonly SettingsRepository _settingsRepository;
    private const string AuthHeader = "x-auth-password";

    public AuthInterceptor(SettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateAuth(context);
        return await continuation(request, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateAuth(context);
        await continuation(request, responseStream, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateAuth(context);
        return await continuation(requestStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateAuth(context);
        await continuation(requestStream, responseStream, context);
    }

    private async Task ValidateAuth(ServerCallContext context)
    {
        var settings = await _settingsRepository.GetAsync();
        var configuredPassword = settings.Server.Password;

        // No password configured = no auth required
        if (string.IsNullOrEmpty(configuredPassword))
        {
            return;
        }

        // Check for auth header
        var authHeader = context.RequestHeaders.GetValue(AuthHeader);
        if (string.IsNullOrEmpty(authHeader))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Authentication required"));
        }

        if (authHeader != configuredPassword)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid password"));
        }
    }
}
