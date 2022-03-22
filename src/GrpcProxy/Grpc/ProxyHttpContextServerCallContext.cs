using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

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
    private Pipe? _requestPipe;
    private Pipe? _reponsePipe;
    private DefaultDeserializationContext? _requestDeserializationContext;
    private DefaultDeserializationContext? _responseDeserializationContext;
    private HttpResponseMessage? _proxiedResponse;

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
            if (HttpContext.Request.Headers.TryGetValue(HeaderNames.GrpcEncoding, out var values))
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
            if (_proxiedResponse?.Headers.TryGetValues(HeaderNames.GrpcEncoding, out var values) ?? false)
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

    internal Pipe RequestPipe
    {
        get => _requestPipe ??= new Pipe();
    }

    internal Pipe ResponsePipe
    {
        get => _reponsePipe ??= new Pipe();
    }

    internal HttpResponseMessage? ProxiedResponseMessage
    {
        get => _proxiedResponse;
        set
        {
            if (_proxiedResponse != null)
                throw new InvalidOperationException("Response can only be set once");
            _proxiedResponse = value;
        }
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
            var message = "Exception was thrown by handler.";
            if (Options.EnableDetailedErrors ?? false)
            {
                message = message + " " + CommonGrpcProtocolHelpers.ConvertToRpcExceptionMessage(ex);
            }
            // Note that the exception given to status won't be returned to the client.
            // It is still useful to set in case an interceptor accesses the status on the server.
            _status = new Status(StatusCode.Unknown, message, ex);
        }
        ConsolidateTrailers(HttpContext.Response);
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
        ConsolidateTrailers(HttpContext.Response);
        return Task.CompletedTask;
    }

    protected override WriteOptions? WriteOptionsCore { get; set; }

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
            _activity.AddTag("proxy-grpc-request", MethodCore);
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

    private void ConsolidateTrailers(HttpResponse httpResponse)
    {
        var trailersDestination = GrpcProtocolHelpers.GetTrailersDestination(httpResponse);

        if (HasResponseTrailers)
        {
            foreach (var trailer in ResponseTrailers)
            {
                var value = (trailer.IsBinary) ? Convert.ToBase64String(trailer.ValueBytes) : trailer.Value;
                trailersDestination.Append(trailer.Key, value);
            }
        }

        // Append status trailers, these overwrite any existing status trailers set via ServerCallContext.ResponseTrailers
        GrpcProtocolHelpers.SetStatus(trailersDestination, Status);
    }
}
