using Grpc.AspNetCore.Server;
using GrpcProxy.Visualizer;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GrpcProxy.Grpc;

public static partial class ProxyGrpcServiceExtensions
{
    public static IGrpcServerBuilder AddProxy(this IGrpcServerBuilder builder)
    {
        builder.Services.TryAddSingleton(typeof(ProxyServerCallHandlerFactory));
        builder.Services.TryAddSingleton(typeof(ProxyServiceRouteBuilder));
        builder.Services.TryAddSingleton<IProxyServiceRepository, ProxyServiceRepository>();
        builder.Services.TryAddSingleton<IProxyMessageMediator, ProxyMessageMediator>();
        builder.Services.TryAddSingleton<IMessageRepository, MessageRepository>();
        
        builder.Services.TryAddSingleton(typeof(ProxyServiceMethodsRegistry));
        builder.Services.AddHttpClient();
        builder.Services.AddHostedService<MessageDispatcher>();
        builder.Services.AddHostedService<LogsPublisher>();
        return builder;
    }
}
