namespace GrpcProxy.Tests;

public class TestData
{
    private readonly ReadOnlyMemory<byte> _content;
    public ReadOnlySpan<byte> Span => _content.Span;

    public TestData(ReadOnlyMemory<byte> content)
    {
        _content = content;
    }
}
