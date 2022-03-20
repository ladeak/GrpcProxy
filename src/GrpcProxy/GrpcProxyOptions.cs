namespace GrpcProxy;

public class GrpcProxyOptions
{
    public List<GrpcProxyMapping>? Mappings { get; set; }

    public string? FallbackAddress { get; set; }
}

public class GrpcProxyMapping : ProxyBehaviorOptions
{
    public string ProtoPath { get; set; } = "";
}

public class ProxyBehaviorOptions
{
    public bool EnableDetailedErrors { get; set; }

    public int? MaxMessageSize { get; set; }

    public string Address { get; set; } = "";
}