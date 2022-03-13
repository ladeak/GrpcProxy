using Grpc.Core;

namespace GrpcProxy.Grpc;

internal class ProxyProviderServiceBinder : ServiceBinderBase
{
    private readonly ProxyServiceMethodProviderContext _context;

    internal ProxyProviderServiceBinder(ProxyServiceMethodProviderContext context)
    {
        _context = context;
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TRequest, TResponse> handler)
        where TRequest : class
        where TResponse : class
    {
        _context.AddClientStreamingMethod(method);
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TRequest, TResponse> handler)
        where TRequest : class
        where TResponse : class
    {
        //var (invoker, metadata) = CreateModelCore<DuplexStreamingServerMethod<TService, TRequest, TResponse>>(
        //    method.Name,
        //    new[] { typeof(IAsyncStreamReader<TRequest>), typeof(IServerStreamWriter<TResponse>), typeof(ServerCallContext) });

        //_context.AddDuplexStreamingMethod(method, metadata, invoker);
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TRequest, TResponse> handler)
        where TRequest : class
        where TResponse : class
    {
        //var (invoker, metadata) = CreateModelCore<ServerStreamingServerMethod<TService, TRequest, TResponse>>(
        //    method.Name,
        //    new[] { typeof(TRequest), typeof(IServerStreamWriter<TResponse>), typeof(ServerCallContext) });

        //_context.AddServerStreamingMethod(method, metadata, invoker);
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TRequest, TResponse> handler)
        where TRequest : class
        where TResponse : class
    {
        _context.AddUnaryMethod(method);
    }
}
