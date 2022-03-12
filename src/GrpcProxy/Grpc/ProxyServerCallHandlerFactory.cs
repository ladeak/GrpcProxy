using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Shared.Server;
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

    internal MethodOptions CreateMethodOptions()
    {
        return MethodOptions.Create(new[] { _globalOptions });
    }

    public UnTypedServerCallHandler CreateUnTyped(string serviceAddress)
    {
        return new UnTypedServerCallHandler(_loggerFactory, _httpClientFactory, _mediator, serviceAddress);
    }

    public ProxyUnaryServerCallHandler<TRequest, TResponse> CreateUnary<TRequest, TResponse>(Method<TRequest, TResponse> method, string serviceAddress)
        where TRequest : class
        where TResponse : class
    {
        return new ProxyUnaryServerCallHandler<TRequest, TResponse>(CreateMethodOptions(), method, _loggerFactory, _httpClientFactory, _mediator, serviceAddress);
    }

    //public ClientStreamingServerCallHandler<TRequest, TResponse> CreateClientStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method)
    //    where TRequest : class
    //    where TResponse : class
    //{
    //    var options = CreateMethodOptions();
    //    throw new NotImplementedException();
    //    //var methodInvoker = new ClientStreamingServerMethodInvoker<TService, TRequest, TResponse>(invoker, method, options, _serviceActivator);

    //    //return new ClientStreamingServerCallHandler<TService, TRequest, TResponse>(methodInvoker, _loggerFactory);
    //}

    //public DuplexStreamingServerCallHandler<TRequest, TResponse> CreateDuplexStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method)
    //    where TRequest : class
    //    where TResponse : class
    //{
    //    var options = CreateMethodOptions();
    //    throw new NotImplementedException();
    //    //var methodInvoker = new DuplexStreamingServerMethodInvoker<TService, TRequest, TResponse>(invoker, method, options, _serviceActivator);

    //    //return new DuplexStreamingServerCallHandler<TService, TRequest, TResponse>(methodInvoker, _loggerFactory);
    //}

    //public ServerStreamingServerCallHandler<TRequest, TResponse> CreateServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method)
    //    where TRequest : class
    //    where TResponse : class
    //{
    //    var options = CreateMethodOptions();
    //    throw new NotImplementedException();
    //    //var methodInvoker = new ServerStreamingServerMethodInvoker<TService, TRequest, TResponse>(invoker, method, options, _serviceActivator);

    //    //return new ServerStreamingServerCallHandler<TService, TRequest, TResponse>(methodInvoker, _loggerFactory);
    //}
}

