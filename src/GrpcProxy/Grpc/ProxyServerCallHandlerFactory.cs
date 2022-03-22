using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.Grpc.CallHandlers;
using Microsoft.Extensions.Options;

namespace GrpcProxy.Grpc;

/// <summary>
/// Creates server call handlers. Provides a place to get services that call handlers will use.
/// </summary>
internal partial class ProxyServerCallHandlerFactory
{
    private readonly GrpcServiceOptions _globalOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProxyMessageMediator _mediator;

    public ProxyServerCallHandlerFactory(
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IProxyMessageMediator mediator,
        IOptions<GrpcServiceOptions> globalOptions)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _globalOptions = globalOptions.Value;
    }

    internal MethodOptions CreateMethodOptions(ProxyBehaviorOptions options)
    {
        return MethodOptions.Create(new[] { _globalOptions, new GrpcServiceOptions() { EnableDetailedErrors = options.EnableDetailedErrors, MaxReceiveMessageSize = options.MaxMessageSize } });
    }

    public UnTypedServerCallHandler CreateUnTyped(string serviceAddress)
    {
        return new UnTypedServerCallHandler(_loggerFactory, _httpClientFactory, _mediator, serviceAddress);
    }

    public ProxyUnaryServerCallHandler<TRequest, TResponse> CreateUnary<TRequest, TResponse>(Method<TRequest, TResponse> method, ProxyBehaviorOptions options)
        where TRequest : class
        where TResponse : class
    {
        return new ProxyUnaryServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _loggerFactory, _httpClientFactory, _mediator, options.Address);
    }

    public ProxyClientStreamingServerCallHandler<TRequest, TResponse> CreateClientStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ProxyBehaviorOptions options)
        where TRequest : class
        where TResponse : class
    {
        return new ProxyClientStreamingServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _httpClientFactory, _mediator, _loggerFactory, options.Address);
    }

    public ProxyDuplexStreamingServerCallHandler<TRequest, TResponse> CreateDuplexStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ProxyBehaviorOptions options)
        where TRequest : class
        where TResponse : class
    {
       return new ProxyDuplexStreamingServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _httpClientFactory, _mediator, _loggerFactory, options.Address);
    }

    public ProxyServerStreamingServerCallHandler<TRequest, TResponse> CreateServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ProxyBehaviorOptions options)
        where TRequest : class
        where TResponse : class
    {
        return new ProxyServerStreamingServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _httpClientFactory, _mediator, _loggerFactory, options.Address);
    }
}

