using GrpcProxy.Grpc;

namespace GrpcProxy.Visualizer;

internal static class PulisherLogger
{
    private static readonly Action<ILogger, DateTime, string, string, string, string, Guid, Exception?> _requestMessage =
        LoggerMessage.Define<DateTime, string, string, string, string, Guid>(LogLevel.Information, new EventId(11, "GrpcRequest"), "{At} Source: {From} Path: {Path} {MethodType} Message: {Message} ProxyId:{ProxyCallId}");

    private static readonly Action<ILogger, DateTime, string, string, string, int, Guid, Exception?> _responseMessage =
        LoggerMessage.Define<DateTime, string, string, string, int, Guid>(LogLevel.Information, new EventId(11, "GrpcResponse"), "{At} Target: {From} {MethodType} Message: {Message} StatusCode: {Status} ProxyId: {ProxyCallId}");

    public static void RequestMessage(ILogger logger, ProxyMessage request) =>
        _requestMessage(logger, request.Timestamp, request.Endpoint, request.MethodType, request.Path, request.Message.ToString() ?? string.Empty, request.ProxyCallId, null);

    public static void ResponseMessage(ILogger logger, ProxyMessage response) =>
        _responseMessage(logger, response.Timestamp, response.Endpoint, response.MethodType, response.Message.ToString() ?? string.Empty, (int?)response.StatusCode ?? 0, response.ProxyCallId, null);
}