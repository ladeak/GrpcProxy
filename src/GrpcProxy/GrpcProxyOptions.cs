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

    public List<MockResponse> MockResponses { get; set; } = new List<MockResponse>();
}

public class MockResponse
{
    public string MethodName { get; set; } = string.Empty;

    public string Response { get; set; } = string.Empty;
}