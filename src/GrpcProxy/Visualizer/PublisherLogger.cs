using GrpcProxy.Data;

namespace GrpcProxy.Visualizer;

internal static class PublisherLogger
{
    private static readonly Action<ILogger, DateTime, string, string, string, string, Guid, Exception?> _requestMessage =
        LoggerMessage.Define<DateTime, string, string, string, string, Guid>(LogLevel.Information, new EventId(11, "GrpcRequest"), "{At} Source: {From} Path: {Path} {MethodType} Message: {Message} ProxyId:{ProxyCallId}");

    private static readonly Action<ILogger, DateTime, string, string, string, int, Guid, Exception?> _responseMessage =
        LoggerMessage.Define<DateTime, string, string, string, int, Guid>(LogLevel.Information, new EventId(12, "GrpcResponse"), "{At} Target: {From} {MethodType} Message: {Message} StatusCode: {Status} ProxyId: {ProxyCallId}");

    private static readonly Action<ILogger, DateTime, string, string, Exception?> _proxyErrorMessage =
        LoggerMessage.Define<DateTime, string, string>(LogLevel.Error, new EventId(13, "GrpcProxyError"), "Grpc Proxy Error, Failed to proxy {At} Target: {From} {MethodType}");

    public static void RequestMessage(ILogger logger, ProxyMessage request) =>
        _requestMessage(logger, request.Timestamp, request.Endpoint, request.MethodType, request.Path, request.Message.ToString() ?? string.Empty, request.ProxyCallId, null);

    public static void ResponseMessage(ILogger logger, ProxyMessage response) =>
        _responseMessage(logger, response.Timestamp, response.Endpoint, response.MethodType, response.Message.ToString() ?? string.Empty, (int?)response.StatusCode ?? 0, response.ProxyCallId, null);

    public static void ProxyErrorMessage(ILogger logger, ProxyMessage errorMessage) =>
        _proxyErrorMessage(logger, errorMessage.Timestamp, errorMessage.Endpoint, errorMessage.MethodType, errorMessage.ProxyError);
}