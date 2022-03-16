using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.Compilation;

namespace GrpcProxy.Grpc;

internal class UnTypedServerCallHandler : ProxyDuplexStreamingServerCallHandler<string, string>
{
    public UnTypedServerCallHandler(
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IProxyMessageMediator messageMediator,
        string serviceAddress)
        : base(MethodOptions.Create(new[] { new GrpcServiceOptions() }),
            new Method<string, string>(MethodType.DuplexStreaming, "untyped", "untyped", Marshallers.Create((_, __) => { }, Deserialize), Marshallers.Create((_, __) => { }, Deserialize)),
            httpClientFactory,
            messageMediator,
            loggerFactory,
            serviceAddress)
    {
    }

    private static string Deserialize(DeserializationContext context) => ProtoCompiler.Deserialize(context.PayloadAsReadOnlySequence());
}
