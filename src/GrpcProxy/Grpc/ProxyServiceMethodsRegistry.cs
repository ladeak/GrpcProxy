using System.Collections.Concurrent;

namespace GrpcProxy.Grpc;

internal class ProxyServiceMethodsRegistry
{
    public ConcurrentDictionary<string, MethodEndpointModel> Methods { get; } = new ConcurrentDictionary<string, MethodEndpointModel>();
}
