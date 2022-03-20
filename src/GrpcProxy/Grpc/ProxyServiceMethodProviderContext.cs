using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Routing.Patterns;

namespace GrpcProxy.Grpc;

public class ProxyServiceMethodProviderContext
{
    private readonly ProxyServerCallHandlerFactory _serverCallHandlerFactory;
    private readonly ProxyBehaviorOptions _options;

    internal ProxyServiceMethodProviderContext(ProxyServerCallHandlerFactory serverCallHandlerFactory, ProxyBehaviorOptions options)
    {
        _serverCallHandlerFactory = serverCallHandlerFactory ?? throw new ArgumentNullException(nameof(serverCallHandlerFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Methods = new List<MethodModel>();
    }

    internal List<MethodModel> Methods { get; }

    public void AddUnaryMethod<TRequest, TResponse>(Method<TRequest, TResponse> method)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateUnary(method, _options);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), callHandler.HandleCallAsync);
    }

    public void AddServerStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateServerStreaming(method, _options);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), callHandler.HandleCallAsync);
    }

    public void AddClientStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateClientStreaming(method, _options);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), callHandler.HandleCallAsync);
    }

    public void AddDuplexStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateDuplexStreaming(method, _options);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), callHandler.HandleCallAsync);
    }

    public void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, RoutePattern pattern, RequestDelegate invoker)
        where TRequest : class
        where TResponse : class
    {
        var methodModel = new MethodModel(method, pattern, new List<object>(), invoker);
        Methods.Add(methodModel);
    }
}