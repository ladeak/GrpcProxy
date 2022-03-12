using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.AspNetCore.Routing.Patterns;

namespace GrpcProxy.Grpc;

internal class ProxyServiceRouteBuilder
{
    private readonly ProxyServiceMethodsRegistry _serviceMethodsRegistry;

    public ProxyServiceRouteBuilder(ProxyServiceMethodsRegistry serviceMethodsRegistry)
    {
        _serviceMethodsRegistry = serviceMethodsRegistry ?? throw new ArgumentNullException(nameof(serviceMethodsRegistry));
    }

    internal List<IEndpointConventionBuilder> Build(IEndpointRouteBuilder endpointRouteBuilder, UnTypedServerCallHandler? fallbackHandler)
    {
        var endpointConventionBuilders = new List<IEndpointConventionBuilder>();
        CreateGenericEndpoint(endpointRouteBuilder, endpointConventionBuilders, fallbackHandler);
        return endpointConventionBuilders;
    }

    internal void CreateGenericEndpoint(IEndpointRouteBuilder endpointRouteBuilder, List<IEndpointConventionBuilder> endpointConventionBuilders, UnTypedServerCallHandler? fallbackHandler)
    {
        endpointConventionBuilders.Add(CreateGenericEndpoint(endpointRouteBuilder, "{genericService}/{genericMethod}", "Generic service", CreateGenericHandler(fallbackHandler)));
    }

    private IEndpointConventionBuilder CreateGenericEndpoint(IEndpointRouteBuilder endpointRouteBuilder, string pattern, string displayName, RequestDelegate requestDelegate)
    {
        var routePattern = RoutePatternFactory.Parse(pattern, defaults: null, new { contentType = new ProxyGrpcUnimplementedConstraint() });
        var endpointBuilder = endpointRouteBuilder.Map(routePattern, requestDelegate);

        endpointBuilder.Add(ep =>
        {
            ep.DisplayName = $"gRPC Proxy {displayName}";
        });

        return endpointBuilder;
    }

    private RequestDelegate CreateGenericHandler(UnTypedServerCallHandler? fallbackHandler)
    {
        return httpContext =>
        {
            if (_serviceMethodsRegistry.Methods.TryGetValue(httpContext.Request.Path.Value!, out var methodToServe))
                return methodToServe.RequestDelegate(httpContext);

            // CORS preflight request should be handled by CORS middleware.
            // If it isn't then return 404 from endpoint request delegate.
            if (GrpcProtocolHelpers.IsCorsPreflightRequest(httpContext))
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            if (fallbackHandler is { })
                return fallbackHandler.HandleCallAsync(httpContext);

            GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);
            var genericService = httpContext.Request.RouteValues["genericService"]?.ToString();
            var genericMethodName = httpContext.Request.RouteValues["genericMethod"]?.ToString();
            GrpcProtocolHelpers.SetStatus(GrpcProtocolHelpers.GetTrailersDestination(httpContext.Response), new Status(StatusCode.Unimplemented, $"No proxy available for {genericService}/{genericMethodName}"));
            return Task.CompletedTask;

        };
    }

    private class ProxyGrpcUnimplementedConstraint : IRouteConstraint
    {
        public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
        {
            if (httpContext == null)
                return false;

            // Constraint needs to be valid when a CORS preflight request is received so that CORS middleware will run
            if (GrpcProtocolHelpers.IsCorsPreflightRequest(httpContext))
                return true;

            if (!HttpMethods.IsPost(httpContext.Request.Method))
                return false;

            return CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcContentType, httpContext.Request.ContentType) ||
                CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcWebContentType, httpContext.Request.ContentType) ||
                CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcWebTextContentType, httpContext.Request.ContentType);
        }
    }
}
