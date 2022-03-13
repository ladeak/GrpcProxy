using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Routing.Patterns;

namespace GrpcProxy.Grpc;

public class ProxyServiceMethodProviderContext
{
    private readonly ProxyServerCallHandlerFactory _serverCallHandlerFactory;
    private readonly string _serviceAddress;

    internal ProxyServiceMethodProviderContext(ProxyServerCallHandlerFactory serverCallHandlerFactory, string serviceAddress)
    {
        _serverCallHandlerFactory = serverCallHandlerFactory ?? throw new ArgumentNullException(nameof(serverCallHandlerFactory));
        _serviceAddress = serviceAddress ?? throw new ArgumentNullException(nameof(serviceAddress));
        Methods = new List<MethodModel>();
    }

    internal List<MethodModel> Methods { get; }

    public void AddUnaryMethod<TRequest, TResponse>(Method<TRequest, TResponse> method)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateUnary(method, _serviceAddress);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), callHandler.HandleCallAsync);
    }

    //public void AddServerStreamingMethod<TService, TRequest, TResponse>(Method<TRequest, TResponse> method, IList<object> metadata, ServerStreamingServerMethod<TService, TRequest, TResponse> invoker)
    //    where TRequest : class
    //    where TResponse : class
    //    where TService : class
    //{
    //    var callHandler = _serverCallHandlerFactory.CreateServerStreaming(method, invoker);
    //    AddMethod(method, RoutePatternFactory.Parse(method.FullName), metadata, callHandler.HandleCallAsync);
    //}

    public void AddClientStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateClientStreaming(method, _serviceAddress);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), callHandler.HandleCallAsync);
    }

    //public void AddDuplexStreamingMethod<TService, TRequest, TResponse>(Method<TRequest, TResponse> method, IList<object> metadata, DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker)
    //    where TRequest : class
    //    where TResponse : class
    //    where TService : class
    //{
    //    var callHandler = _serverCallHandlerFactory.CreateDuplexStreaming(method, invoker);
    //    AddMethod(method, RoutePatternFactory.Parse(method.FullName), metadata, callHandler.HandleCallAsync);
    //}

    public void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, RoutePattern pattern, RequestDelegate invoker)
        where TRequest : class
        where TResponse : class
    {
        var methodModel = new MethodModel(method, pattern, new List<object>(), invoker);
        Methods.Add(methodModel);
    }
}