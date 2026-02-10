using Grpc.Core;

namespace D2BotNG.Utilities;

public static class RpcExceptions
{
    public static RpcException NotFound(string entityType, string key) =>
        new(new Status(StatusCode.NotFound, $"{entityType} '{key}' not found"));

    public static RpcException AlreadyExists(string entityType, string key) =>
        new(new Status(StatusCode.AlreadyExists, $"{entityType} '{key}' already exists"));

    public static RpcException PermissionDenied(string reason) =>
        new(new Status(StatusCode.PermissionDenied, reason));

    public static RpcException FailedPrecondition(string reason) =>
        new(new Status(StatusCode.FailedPrecondition, reason));
}
