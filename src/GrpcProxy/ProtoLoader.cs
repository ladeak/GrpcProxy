using System.Reflection;
using GrpcProxy.Compilation;
using GrpcProxy.Grpc;

namespace GrpcProxy;

public static class ProtoLoader
{
    public static async Task LoadProtoFileAsync(IProxyServiceRepository repo, string serviceAddress, string protoFilePath)
    {
        var protoCompiler = new ProtoCompiler();
        var (protoFile, grpcFile) = await protoCompiler.CompileAsync(protoFilePath);
        var stream = new MemoryStream();
        var csCompiler = new CsCompiler();
        await csCompiler.CompileAsync(stream, protoFile, grpcFile);

        File.Delete(protoFile);
        File.Delete(grpcFile);

        var context = new ProxyServiceAssemblyContext();
        var assembly = context.LoadFromStream(stream);

        var service = assembly.GetTypes().Where(x => x.GetCustomAttributes().OfType<global::Grpc.Core.BindServiceMethodAttribute>().Any()).FirstOrDefault();
        if (service == null)
            throw new InvalidProgramException("No GRPC service type found");
        repo.AddService(service, serviceAddress, protoFilePath, context);
    }
}
