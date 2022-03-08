using System.Reflection;
using System.Runtime.Loader;

namespace GrpcProxy;

public class ProxyServiceAssemblyContext : AssemblyLoadContext
{
    public ProxyServiceAssemblyContext() : base(true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        return base.Load(assemblyName);
    }
}