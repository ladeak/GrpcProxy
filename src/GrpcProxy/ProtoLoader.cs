using System.Reflection;
using GrpcProxy.Compilation;
using GrpcProxy.Grpc;

namespace GrpcProxy;

public static class ProtoLoader
{
    public static async Task LoadProtoFileAsync(IProxyServiceRepository repo, GrpcProxyMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.Address) || string.IsNullOrWhiteSpace(mapping.ProtoPath))
            return;
        string protoFile = string.Empty;
        string grpcFile = string.Empty;
        try
        {
            var protoCompiler = new ProtoCompiler();
            (protoFile, grpcFile) = await protoCompiler.CompileAsync(mapping.ProtoPath);
            var stream = new MemoryStream();
            var csCompiler = new CsCompiler();
            await csCompiler.CompileAsync(stream, protoFile, grpcFile);

            var context = new ProxyServiceAssemblyContext();
            var assembly = context.LoadFromStream(stream);

            var service = assembly.GetTypes().Where(x => x.GetCustomAttributes().OfType<global::Grpc.Core.BindServiceMethodAttribute>().Any()).FirstOrDefault();
            if (service == null)
                throw new InvalidProgramException("No GRPC service type found");
            repo.AddService(service, mapping, context);
        }
        finally
        {
            if (File.Exists(protoFile))
                File.Delete(protoFile);
            if (File.Exists(grpcFile))
                File.Delete(grpcFile);
        }
    }
}
