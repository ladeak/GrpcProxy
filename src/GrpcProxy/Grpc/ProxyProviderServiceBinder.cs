using System.Reflection;
using Grpc.Core;
using Microsoft.AspNetCore.Routing;

namespace GrpcProxy.Grpc;

internal class ProxyProviderServiceBinder : ServiceBinderBase
{
    private readonly ProxyServiceMethodProviderContext _context;
    private readonly Type _declaringType;
    private readonly Type _serviceType;

    internal ProxyProviderServiceBinder(ProxyServiceMethodProviderContext context, Type declaringType, Type serviceType)
    {
        _context = context;
        _declaringType = declaringType;
        _serviceType = serviceType;
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TRequest, TResponse> handler)
        where TRequest : class
        where TResponse : class
    {
        var metadata = CreateModelCore<ClientStreamingServerMethod<TRequest, TResponse>>(
            method.Name,
            new[] { typeof(IAsyncStreamReader<TRequest>), typeof(ServerCallContext) });

        //_context.AddClientStreamingMethod(method, metadata, invoker);
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
        var metadata = CreateModelCore<UnaryServerMethod<TRequest, TResponse>>(
            method.Name,
            new[] { typeof(TRequest), typeof(ServerCallContext) });

        _context.AddUnaryMethod(method, metadata);
    }

    private List<object> CreateModelCore<TDelegate>(string methodName, Type[] methodParameters) where TDelegate : Delegate
    {
        var handlerMethod = GetMethod(methodName, methodParameters);

        if (handlerMethod == null)
        {
            throw new InvalidOperationException($"Could not find '{methodName}' on {_serviceType}.");
        }

        //var invoker = (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), handlerMethod);

        var metadata = new List<object>();
        // Add type metadata first so it has a lower priority
        metadata.AddRange(_serviceType.GetCustomAttributes(inherit: true));
        // Add method metadata last so it has a higher priority
        metadata.AddRange(handlerMethod.GetCustomAttributes(inherit: true));

        // Accepting CORS preflight means gRPC will allow requests with OPTIONS + preflight headers.
        // If CORS middleware hasn't been configured then the request will reach gRPC handler.
        // gRPC will return 405 response and log that CORS has not been configured.
        metadata.Add(new HttpMethodMetadata(new[] { "POST" }, acceptCorsPreflight: true));

        return metadata;
    }

    private MethodInfo? GetMethod(string methodName, Type[] methodParameters)
    {
        Type? currentType = _serviceType;
        while (currentType != null)
        {
            // Specify binding flags explicitly because we don't want to match static methods.
            var matchingMethod = currentType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: methodParameters,
                modifiers: null);

            if (matchingMethod == null)
            {
                return null;
            }

            // Validate that the method overrides the virtual method on the base service type.
            // If there is a method with the same name it will hide the base method. Ignore it,
            // and continue searching on the base type.
            if (matchingMethod.IsVirtual)
            {
                var baseDefinitionMethod = matchingMethod.GetBaseDefinition();
                if (baseDefinitionMethod != null && baseDefinitionMethod.DeclaringType == _declaringType)
                {
                    return matchingMethod;
                }
            }

            currentType = currentType.BaseType;
        }

        return null;
    }
}
