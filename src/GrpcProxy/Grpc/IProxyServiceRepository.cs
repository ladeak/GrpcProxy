namespace GrpcProxy.Grpc;

public interface IProxyServiceRepository
{
    public IReadOnlyCollection<ProxyService> Services { get; }
    public void AddService(Type baseService, GrpcProxyMapping mapping, ProxyServiceAssemblyContext context);
    public void RemoveService(string protoFile);
}
