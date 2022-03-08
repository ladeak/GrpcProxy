namespace GrpcProxy.Grpc;

public static class ProxyGrpcEndpointRouteBuilderExtensions
{
    public static GrpcServiceEndpointConventionBuilder MapGrpcService(this IEndpointRouteBuilder builder)
    {
        _ = builder ?? throw new ArgumentNullException(nameof(builder));

        ValidateServicesRegistered(builder.ServiceProvider);

        var serviceRouteBuilder = builder.ServiceProvider.GetRequiredService<ProxyServiceRouteBuilder>();
        var endpointConventionBuilders = serviceRouteBuilder.Build(builder);

        return new GrpcServiceEndpointConventionBuilder(endpointConventionBuilders);
    }

    private static void ValidateServicesRegistered(IServiceProvider serviceProvider)
    {
        var routeBuilder = serviceProvider.GetService(typeof(ProxyServiceRouteBuilder));
        if (routeBuilder == null)
            throw new InvalidOperationException("Please add all the required services by calling 'IServiceCollection.AddGrpc().AddProxy()' in the application startup code.");
    }
}
