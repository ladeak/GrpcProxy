using Grpc.Shared.Server;

namespace GrpcProxy.Grpc;

public record class ProxyService(string ServiceAddress, string FilePath, ProxyServiceAssemblyContext AssemblyContext, Type BaseService, List<string> Endpoints);

internal class ProxyServiceRepository : IProxyServiceRepository
{
    private readonly ProxyServerCallHandlerFactory _serverCallHandlerFactory;
    private readonly ProxyServiceMethodsRegistry _serviceMethodsRegistry;
    private readonly Dictionary<string, ProxyService> _services = new Dictionary<string, ProxyService>();

    public ProxyServiceRepository(ProxyServerCallHandlerFactory serverCallHandlerFactory, ProxyServiceMethodsRegistry serviceMethodsRegistry)
    {
        _serverCallHandlerFactory = serverCallHandlerFactory ?? throw new ArgumentNullException(nameof(serverCallHandlerFactory));
        _serviceMethodsRegistry = serviceMethodsRegistry ?? throw new ArgumentNullException(nameof(serviceMethodsRegistry));
    }

    public void AddService(Type baseService, string address, string protoFile, ProxyServiceAssemblyContext context)
    {
        if(_services.ContainsKey(protoFile))
            RemoveService(protoFile);
        var serviceMethodProviderContext = new ProxyServiceMethodProviderContext(_serverCallHandlerFactory, address);
        ServiceMethodDiscovery(serviceMethodProviderContext, baseService);
        foreach (var method in serviceMethodProviderContext.Methods)
            _serviceMethodsRegistry.Methods.AddOrUpdate(method.Pattern.RawText!, method, (_, __) => method);
        _services.Add(protoFile, new ProxyService(address, protoFile, context, baseService, _serviceMethodsRegistry.Methods.Select(x => x.Value.Pattern.RawText!).ToList()));
    }

    public void RemoveService(string protoFile)
    {
        if (!_services.TryGetValue(protoFile, out var service))
            return;
        foreach (var endPoint in service.Endpoints)
            _serviceMethodsRegistry.Methods.TryRemove(endPoint, out _);
        _services.Remove(protoFile);
        service.AssemblyContext.Unload();
        GC.Collect();
    }

    public IReadOnlyCollection<ProxyService> Services => _services.Values;

    private void ServiceMethodDiscovery(ProxyServiceMethodProviderContext context, Type baseService)
    {
        var bindMethodInfo = BindMethodFinder.GetBindMethod(baseService);

        if (bindMethodInfo != null)
        {
            // The second parameter is always the service base type
            var serviceParameter = bindMethodInfo.GetParameters()[1];
            var binder = new ProxyProviderServiceBinder(context, serviceParameter.ParameterType, baseService);
            try
            {
                bindMethodInfo.Invoke(null, new object?[] { binder, null });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error binding gRPC service '{baseService.Name}'.", ex);
            }
        }
    }
}