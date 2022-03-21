using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared.Server;
using Microsoft.Net.Http.Headers;

namespace GrpcProxy.Grpc;

internal abstract class ProxyServerCallHandlerBase<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private const string LoggerName = nameof(ProxyServerCallHandlerBase<TRequest, TResponse>);
    protected readonly MethodOptions _options;
    protected readonly Method<TRequest, TResponse> _method;
    protected ILogger _logger { get; }

    protected ProxyServerCallHandlerBase(MethodOptions options, Method<TRequest, TResponse> method, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(LoggerName);
        _options = options;
        _method = method;
    }

    public Task HandleCallAsync(HttpContext httpContext)
    {
        if (GrpcProtocolHelpers.IsInvalidContentType(httpContext, out var error))
            return ProcessInvalidContentTypeRequest(httpContext, error);

        if (!GrpcProtocolConstants.IsHttp2(httpContext.Request.Protocol) && !GrpcProtocolConstants.IsHttp3(httpContext.Request.Protocol))
            return ProcessNonHttp2Request(httpContext);

        var serverCallContext = new ProxyHttpContextServerCallContext(httpContext, _options, typeof(TRequest), typeof(TResponse), _logger);
        httpContext.Features.Set<IServerCallContextFeature>(serverCallContext);

        GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

        try
        {
            serverCallContext.Initialize();
            var handleCallTask = HandleCallAsyncCore(httpContext, serverCallContext);
            return AwaitHandleCall(serverCallContext, handleCallTask);
        }
        catch (Exception ex)
        {
            return serverCallContext.ProcessHandlerErrorAsync(ex, "Unable to proxy");
        }

        static async Task AwaitHandleCall(ProxyHttpContextServerCallContext serverCallContext, Task handleCall)
        {
            try
            {
                await handleCall;
                await serverCallContext.EndCallAsync();
            }
            catch (Exception ex)
            {
                await serverCallContext.ProcessHandlerErrorAsync(ex, "Unable to proxy request");
            }
        }
    }

    protected abstract Task HandleCallAsyncCore(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext);

    private Task ProcessNonHttp2Request(HttpContext httpContext)
    {
        GrpcServerLog.UnsupportedRequestProtocol(_logger, httpContext.Request.Protocol);
        GrpcProtocolHelpers.BuildHttpErrorResponse(httpContext.Response, StatusCodes.Status426UpgradeRequired, StatusCode.Internal, $"Request protocol '{httpContext.Request.Protocol}' is not supported.");
        httpContext.Response.Headers[HeaderNames.Upgrade] = GrpcProtocolConstants.Http2Protocol;
        return Task.CompletedTask;
    }

    private Task ProcessInvalidContentTypeRequest(HttpContext httpContext, string error)
    {
        // This might be a CORS preflight request and CORS middleware hasn't been configured
        if (GrpcProtocolHelpers.IsCorsPreflightRequest(httpContext))
        {
            GrpcServerLog.UnhandledCorsPreflightRequest(_logger);
            GrpcProtocolHelpers.BuildHttpErrorResponse(httpContext.Response, StatusCodes.Status405MethodNotAllowed, StatusCode.Internal, "Unhandled CORS preflight request received. CORS may not be configured correctly in the application.");
            httpContext.Response.Headers[HeaderNames.Allow] = HttpMethods.Post;
            return Task.CompletedTask;
        }
        else
        {
            GrpcServerLog.UnsupportedRequestContentType(_logger, httpContext.Request.ContentType);
            GrpcProtocolHelpers.BuildHttpErrorResponse(httpContext.Response, StatusCodes.Status415UnsupportedMediaType, StatusCode.Internal, error);
            return Task.CompletedTask;
        }
    }
}
