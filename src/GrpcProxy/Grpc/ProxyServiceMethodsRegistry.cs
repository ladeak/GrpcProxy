using System.Collections.Concurrent;
using Grpc.AspNetCore.Server.Model.Internal;

namespace GrpcProxy.Grpc;

internal class ProxyServiceMethodsRegistry
{
    public ConcurrentDictionary<string, MethodModel> Methods { get; } = new ConcurrentDictionary<string, MethodModel>();
}
