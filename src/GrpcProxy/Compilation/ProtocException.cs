namespace GrpcProxy.Compilation;

public class ProtocException : Exception
{
    public ProtocException(string message) : base($"Error compiling proto file: {message}")
    {
    }
}