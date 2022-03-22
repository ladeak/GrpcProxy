using Microsoft.AspNetCore.Routing.Patterns;

namespace GrpcProxy.Grpc;

internal class MethodEndpointModel
{
    public MethodEndpointModel(RoutePattern pattern, RequestDelegate requestDelegate)
    {
        Pattern = pattern;
        RequestDelegate = requestDelegate;
    }

    public RoutePattern Pattern { get; }
    public RequestDelegate RequestDelegate { get; }
}