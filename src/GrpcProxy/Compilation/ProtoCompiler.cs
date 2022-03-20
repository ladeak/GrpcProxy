using System.Buffers;
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
        Console.WriteLine($"ProtoCompiler - packagePath: {packagePath}");
        Console.WriteLine($"ProtoCompiler - tempPath: {filePath}");
        Console.WriteLine($"ProtoCompiler - filePath: {tempPath}");
        Console.WriteLine($"ProtoCompiler - proto_path1: {Path.GetDirectoryName(filePath)}");
        Console.WriteLine($"ProtoCompiler - proto_path2: {Environment.CurrentDirectory}");
        Console.WriteLine($"ProtoCompiler - proto_path3: {Path.GetDirectoryName(Environment.ProcessPath)}");
        startInfo.ArgumentList.Add($"--proto_path={Path.GetDirectoryName(filePath)}");
        startInfo.ArgumentList.Add($"--proto_path={Environment.CurrentDirectory}");
        startInfo.ArgumentList.Add($"--proto_path={Path.GetDirectoryName(Environment.ProcessPath)}");
        startInfo.ArgumentList.Add($"--proto_path={packagePath}");
        startInfo.ArgumentList.Add($"--csharp_out={tempPath}");
        startInfo.ArgumentList.Add($"--grpc_out={tempPath}");
        var pluginPath = Path.Combine(packagePath, Environment.OSVersion.Platform == PlatformID.Win32NT ? "grpc_csharp_plugin.exe" : "grpc_csharp_plugin");
        startInfo.ArgumentList.Add($"--plugin=protoc-gen-grpc={pluginPath}");

        startInfo.ArgumentList.Add(filePath);
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        using var process = new Process()
        {
            StartInfo = startInfo,
        };
        Console.WriteLine(process.StartInfo.Arguments);
        Console.WriteLine(process.StartInfo.UseShellExecute);
        Console.WriteLine(process.StartInfo.WorkingDirectory);
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

    public static string Deserialize(ReadOnlySequence<byte> data)
    {
        var packagePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        var startInfo = new ProcessStartInfo()
        {
            FileName = Path.Combine(packagePath, Environment.OSVersion.Platform == PlatformID.Win32NT ? "protoc.exe" : "protoc")
        };

        startInfo.ArgumentList.Add("--decode_raw");
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = true;
        using var process = new Process()
        {
            StartInfo = startInfo,
        };
        process.Start();

        SequencePosition position = data.Start;
        ReadOnlyMemory<byte> buffer;
        while (data.TryGet(ref position, out buffer, true))
        {
            var input = new char[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
                input[i] = (char)buffer.Span[i];
            process.StandardInput.Write(input);
        }
        process.StandardInput.Close();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new ProtocException(error);
        }

        var response = process.StandardOutput.ReadToEnd();
        return response;
    }
}
