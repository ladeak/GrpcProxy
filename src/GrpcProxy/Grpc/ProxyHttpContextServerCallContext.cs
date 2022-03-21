using System.Diagnostics;
using System.Net.Sockets;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http.Features;

namespace GrpcProxy.Grpc;

internal sealed class ProxyHttpContextServerCallContext : ServerCallContext, IServerCallContextFeature
{
    private static readonly AuthContext UnauthenticatedContext = new AuthContext(null!, new Dictionary<string, List<AuthProperty>>());
    private string? _peer;
    private Metadata? _requestHeaders;
    private Metadata? _responseTrailers;
    private Status _status;
    private AuthContext? _authContext;
    private Activity? _activity;
    private DefaultDeserializationContext? _requestDeserializationContext;
    private DefaultDeserializationContext? _responseDeserializationContext;
    private HttpResponseMessage _proxiedResponse;

    internal ProxyHttpContextServerCallContext(HttpContext httpContext, MethodOptions options, Type requestType, Type responseType, ILogger logger)
    {
        HttpContext = httpContext;
        Options = options;
        RequestType = requestType;
        ResponseType = responseType;
        Logger = logger;
    }

    internal ILogger Logger { get; }
    internal HttpContext HttpContext { get; }
    internal MethodOptions Options { get; }
    internal Type RequestType { get; }
    internal Type ResponseType { get; }
    internal string? RequestGrpcEncoding
    {
        get
        {
            if (HttpContext.Request.Headers.TryGetValue(GrpcProtocolConstants.MessageEncodingHeader, out var values))
            {
                return values;
            }
            return null;
        }
    }
    internal string? ResponseGrpcEncoding
    {
        get
        {
            if (_proxiedResponse.Headers.TryGetValues(GrpcProtocolConstants.MessageEncodingHeader, out var values))
            {
                return values.First();
            }
            return null;
        }
    }

    internal DefaultDeserializationContext RequestDeserializationContext
    {
        get => _requestDeserializationContext ??= new DefaultDeserializationContext();
    }
    internal DefaultDeserializationContext ResponseDeserializationContext
    {
        get => _responseDeserializationContext ??= new DefaultDeserializationContext();
    }

    internal bool HasResponseTrailers => _responseTrailers != null;

    protected override string MethodCore => HttpContext.Request.Path.Value!;

    protected override string HostCore => HttpContext.Request.Host.Value;

    protected override string PeerCore
    {
        get
        {
            // Follows the standard at https://github.com/grpc/grpc/blob/master/doc/naming.md
            if (_peer == null)
            {
                _peer = BuildPeer();
            }

            return _peer;
        }
    }

    private string BuildPeer()
    {
        var connection = HttpContext.Connection;
        if (connection.RemoteIpAddress != null)
        {
            switch (connection.RemoteIpAddress.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    return $"ipv4:{connection.RemoteIpAddress}:{connection.RemotePort}";
                case AddressFamily.InterNetworkV6:
                    return $"ipv6:[{connection.RemoteIpAddress}]:{connection.RemotePort}";
                default:
                    // TODO(JamesNK) - Test what should be output when used with UDS and named pipes
                    return $"unknown:{connection.RemoteIpAddress}:{connection.RemotePort}";
            }
        }
        else
        {
            return "unknown"; // Match Grpc.Core
        }
    }

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore
    {
        get
        {
            if (_requestHeaders == null)
            {
                _requestHeaders = new Metadata();

                foreach (var header in HttpContext.Request.Headers)
                {
                    if (GrpcProtocolHelpers.ShouldSkipHeader(header.Key))
                    {
                        continue;
                    }

                    if (header.Key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        _requestHeaders.Add(header.Key, GrpcProtocolHelpers.ParseBinaryHeader(header.Value));
                    }
                    else
                    {
                        _requestHeaders.Add(header.Key, header.Value);
                    }
                }
            }

            return _requestHeaders;
        }
    }

    internal Task ProcessHandlerErrorAsync(Exception ex, string method)
    {
        ProcessHandlerError(ex, method);
        return Task.CompletedTask;
    }

    private void ProcessHandlerError(Exception ex, string method)
    {
        if (ex is RpcException rpcException)
        {
            // RpcException is thrown by client code to modify the status returned from the server.
            // Log the status, detail and debug exception (if present).
            // Don't log the RpcException itself to reduce log verbosity. All of its information is already captured.
            GrpcServerLog.RpcConnectionError(Logger, rpcException.StatusCode, rpcException.Status.Detail, rpcException.Status.DebugException);

            // There are two sources of metadata entries on the server-side:
            // 1. serverCallContext.ResponseTrailers
            // 2. trailers in RpcException thrown by user code in server side handler.
            // As metadata allows duplicate keys, the logical thing to do is
            // to just merge trailers from RpcException into serverCallContext.ResponseTrailers.
            foreach (var entry in rpcException.Trailers)
            {
                ResponseTrailers.Add(entry);
            }

            _status = rpcException.Status;
        }
        else
        {
            GrpcServerLog.ErrorExecutingServiceMethod(Logger, method, ex);

            var message = ErrorMessageHelper.BuildErrorMessage("Exception was thrown by handler.", ex, Options.EnableDetailedErrors);

            // Note that the exception given to status won't be returned to the client.
            // It is still useful to set in case an interceptor accesses the status on the server.
            _status = new Status(StatusCode.Unknown, message, ex);
        }
        HttpContext.Response.ConsolidateTrailers(this);
        LogCallEnd();
    }

    // If there is a deadline then we need to have our own cancellation token.
    // Deadline will call CompleteAsync, then Reset/Abort. This order means RequestAborted
    // is not raised, so deadlineCts will be triggered instead.
    protected override CancellationToken CancellationTokenCore => HttpContext.RequestAborted;

    protected override Metadata ResponseTrailersCore
    {
        get
        {
            if (_responseTrailers == null)
            {
                _responseTrailers = new Metadata();
            }

            return _responseTrailers;
        }
    }

    protected override Status StatusCore
    {
        get => _status;
        set => _status = value;
    }

    internal Task EndCallAsync()
    {
        EndCallCore();
        return Task.CompletedTask;
    }

    private void EndCallCore()
    {
        HttpContext.Response.ConsolidateTrailers(this);
        LogCallEnd();
    }

    private void LogCallEnd()
    {
        if (_activity != null)
        {
            _activity.AddTag(GrpcServerConstants.ActivityStatusCodeTag, _status.StatusCode.ToTrailerString());
        }
        if (_status.StatusCode != StatusCode.OK)
        {
            GrpcEventSource.Log.CallFailed(_status.StatusCode);
        }
        GrpcEventSource.Log.CallStop();
    }

#pragma warning disable CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
    protected override WriteOptions? WriteOptionsCore { get; set; }
#pragma warning restore CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).

    protected override AuthContext AuthContextCore
    {
        get
        {
            if (_authContext == null)
            {
                var clientCertificate = HttpContext.Connection.ClientCertificate;
                if (clientCertificate == null)
                {
                    _authContext = UnauthenticatedContext;
                }
                else
                {
                    _authContext = GrpcProtocolHelpers.CreateAuthContext(clientCertificate);
                }
            }

            return _authContext;
        }
    }

    public ServerCallContext ServerCallContext => this;

    protected override IDictionary<object, object> UserStateCore => HttpContext.Items!;

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
    {
        // https://github.com/grpc/grpc-dotnet/issues/40
        throw new NotImplementedException("CreatePropagationToken will be implemented in a future version.");
    }

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
    {
        throw new NotImplementedException("Response writing should be stream copy.");
    }

    public void Initialize()
    {
        _activity = GetHostActivity();
        if (_activity != null)
            _activity.AddTag(GrpcServerConstants.ActivityMethodTag, MethodCore);

        GrpcEventSource.Log.CallStart(MethodCore);
    }

    public void SetProxiedResponse(HttpResponseMessage proxiedResponse)
    {
        if (_proxiedResponse == null)
            _proxiedResponse = proxiedResponse;
    }

    private Activity? GetHostActivity()
    {
        // Feature always returns the host activity
        var feature = HttpContext.Features.Get<IHttpActivityFeature>();
        if (feature != null)
        {
            return feature.Activity;
        }
        return null;
    }
}

internal static class HttpResponseExtensions
{
    public static void ConsolidateTrailers(this HttpResponse httpResponse, ProxyHttpContextServerCallContext context)
    {
        var trailersDestination = GrpcProtocolHelpers.GetTrailersDestination(httpResponse);

        if (context.HasResponseTrailers)
        {
            foreach (var trailer in context.ResponseTrailers)
            {
                var value = (trailer.IsBinary) ? Convert.ToBase64String(trailer.ValueBytes) : trailer.Value;
                trailersDestination.Append(trailer.Key, value);
            }
        }

        // Append status trailers, these overwrite any existing status trailers set via ServerCallContext.ResponseTrailers
        GrpcProtocolHelpers.SetStatus(trailersDestination, context.Status);
    }
}