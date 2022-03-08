namespace GrpcProxy;

public class GrpcProxyOptions
{
    public List<GrpcProxyMapping>? Mappings { get; set; }
}


public class GrpcProxyMapping
{
    public string? ProtoFilePath { get; set; }

    public string? ServiceAddress { get; set; }

    public int? MessagesLimit { get; set; }
}
