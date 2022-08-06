using System.Collections.Immutable;
using GrpcProxy.Data;

namespace GrpcProxy.Visualizer;

public record struct ProxyMessageChain(Guid Id, ImmutableArray<ProxyMessage> Chain) : ISearchable
{
    public bool Contains(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        return Chain.Any(x=>x.Contains(text));
    }
}
