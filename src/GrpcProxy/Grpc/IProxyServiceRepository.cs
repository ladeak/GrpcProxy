namespace GrpcProxy.Grpc;

public interface IProxyServiceRepository
{
    public IReadOnlyCollection<ProxyService> Services { get; }

    public void AddService(Type baseService, string address, string protoFile, ProxyServiceAssemblyContext context);

    public void RemoveService(string protoFile);
}
