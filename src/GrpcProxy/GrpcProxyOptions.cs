namespace GrpcProxy;

public class GrpcProxyOptions
{
    public List<GrpcProxyMapping>? Mappings { get; set; }
}


public class GrpcProxyMapping
{
    public string? ProtoPath { get; set; }

    public string? Address { get; set; }
}

