using System.Buffers;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GrpcProxy.Compilation;

public class CsCompiler
{
    public async Task CompileAsync(Stream stream, string protoFilePath, string grpcFilePath)
    {
        var protoCode = await File.ReadAllTextAsync(protoFilePath);
        var protoSyntaxTree = CSharpSyntaxTree.ParseText(protoCode);
        var grpcCode = await File.ReadAllTextAsync(grpcFilePath);
        var grpcSyntaxTree = CSharpSyntaxTree.ParseText(grpcCode);

        var references = new List<MetadataReference>();
        var coreLibPath = typeof(object).Assembly.Location;
        references.Add(MetadataReference.CreateFromFile(coreLibPath));
        references.Add(MetadataReference.CreateFromFile(typeof(Google.Protobuf.IMessage).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(IBufferWriter<>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(global::Grpc.Core.Metadata).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(coreLibPath) ?? string.Empty, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location));

        var compilation = CSharpCompilation.Create("GeneratedProtobuf", new[] { protoSyntaxTree, grpcSyntaxTree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new Exception(result.Diagnostics.First().ToString());
        stream.Seek(0, SeekOrigin.Begin);
    }
}