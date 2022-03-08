using System.Diagnostics;
using System.Reflection;

namespace GrpcProxy.Compilation;

public class ProtoCompiler
{
    public async Task<(string, string)> CompileAsync(string filePath)
    {
        var packagePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        var startInfo = new ProcessStartInfo()
        {
            FileName = Path.Combine(packagePath, Environment.OSVersion.Platform == PlatformID.Win32NT ? "protoc.exe" : "protoc")
        };
        var tempPath = Path.GetTempPath();
        startInfo.ArgumentList.Add($"--proto_path={Path.GetDirectoryName(filePath)}");
        startInfo.ArgumentList.Add($"--proto_path={Environment.CurrentDirectory}");
        startInfo.ArgumentList.Add($"--proto_path={Path.GetDirectoryName(Environment.ProcessPath)}");
        startInfo.ArgumentList.Add($"--csharp_out={tempPath}");
        startInfo.ArgumentList.Add($"--grpc_out={tempPath}");
        startInfo.ArgumentList.Add($"--plugin=protoc-gen-grpc=grpc_csharp_plugin.exe");

        startInfo.ArgumentList.Add(filePath);
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        using var process = new Process()
        {
            StartInfo = startInfo,
        };
        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new ProtocException(error);
        }

        var outputPath = Path.Combine(tempPath, Path.ChangeExtension(Path.GetFileNameWithoutExtension(filePath), ".cs"));
        if (!File.Exists(outputPath))
            throw new ProtocException("No output file generated.");

        var outputGrpcPath = Path.Combine(tempPath, Path.ChangeExtension($"{Path.GetFileNameWithoutExtension(filePath)}Grpc", ".cs"));
        if (!File.Exists(outputGrpcPath))
            throw new ProtocException("No Grpc output file generated.");

        return (outputPath, outputGrpcPath);
    }
}
