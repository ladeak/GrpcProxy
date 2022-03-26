using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
        return new UnTypedServerCallHandler(_httpClientFactory, _mediator, serviceAddress);
    }

    public ProxyServerCallHandlerBase<TRequest, TResponse> CreateUnary<TRequest, TResponse>(Method<TRequest, TResponse> method, ProxyBehaviorOptions options)
        where TRequest : class
        where TResponse : class
    {
        if (TryGetMockResponse(method.Name, options.MockResponses, out TResponse? mockResponse))
            return new ProxyMockServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _mediator, mockResponse);
        return new ProxyUnaryServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _httpClientFactory, _mediator, options.Address);
    }

    public ProxyServerCallHandlerBase<TRequest, TResponse> CreateClientStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ProxyBehaviorOptions options)
        where TRequest : class
        where TResponse : class
    {
        if (TryGetMockResponse(method.Name, options.MockResponses, out TResponse? mockResponse))
            return new ProxyMockServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _mediator, mockResponse);
        return new ProxyClientStreamingServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _httpClientFactory, _mediator, options.Address);
    }

    public ProxyServerCallHandlerBase<TRequest, TResponse> CreateDuplexStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ProxyBehaviorOptions options)
        where TRequest : class
        where TResponse : class
    {
        if (TryGetMockResponse(method.Name, options.MockResponses, out TResponse? mockResponse))
            return new ProxyMockServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _mediator, mockResponse);
        return new ProxyDuplexStreamingServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _httpClientFactory, _mediator, options.Address);
    }

    public ProxyServerCallHandlerBase<TRequest, TResponse> CreateServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ProxyBehaviorOptions options)
        where TRequest : class
        where TResponse : class
    {
        if (TryGetMockResponse(method.Name, options.MockResponses, out TResponse? mockResponse))
            return new ProxyMockServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _mediator, mockResponse);
        return new ProxyServerStreamingServerCallHandler<TRequest, TResponse>(CreateMethodOptions(options), method, _httpClientFactory, _mediator, options.Address);
    }

    private bool TryGetMockResponse<TResponse>(string serviceName, IEnumerable<MockResponse> mockResponses, [NotNullWhen(true)] out TResponse? result)
        where TResponse : class
    {
        result = default;
        foreach (var mockResponse in mockResponses)
            if (TryGetMockResponse(serviceName, mockResponse, out result))
                return true;
        return false;
    }

    private bool TryGetMockResponse<TResponse>(string serviceName, MockResponse mockResponse, [NotNullWhen(true)] out TResponse? result)
        where TResponse : class
    {
        result = default;
        if (string.IsNullOrWhiteSpace(mockResponse.MethodName))
            return false;
        if (serviceName == mockResponse.MethodName)
            result = JsonSerializer.Deserialize<TResponse>(mockResponse.Response);
        return result != null;
    }
}

