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

        var limitedMessageRepository = new LimitedMessageRepository();
        var groupingMessageRepository = new GroupingMessageRepository();
        builder.Services.TryAddSingleton<ILimitedMessageRepository>(limitedMessageRepository);
        builder.Services.TryAddSingleton<IGroupingMessageRepository>(groupingMessageRepository);
        builder.Services.AddSingleton<IMessageRepositoryIngress>(limitedMessageRepository);
        builder.Services.AddSingleton<IMessageRepositoryIngress>(groupingMessageRepository);


        builder.Services.TryAddSingleton(typeof(ProxyServiceMethodsRegistry));
        builder.Services.AddHttpClient();
        builder.Services.AddHostedService<MessageDispatcher>();
        builder.Services.AddHostedService<LogsPublisher>();
        return builder;
    }
}
