using System.Collections.Immutable;
using GrpcProxy.Grpc;

namespace GrpcProxy.Visualizer;

public interface IMessageRepository
{
    public Task AddAsync(ProxyMessage item);

    public ImmutableArray<ProxyMessage> Messages { get; }

    public event EventHandler<ProxyMessage> OnMessage;

    public void Clear();
}
