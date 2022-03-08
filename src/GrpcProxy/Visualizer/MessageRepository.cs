using System.Collections.Immutable;
using GrpcProxy.Grpc;

namespace GrpcProxy.Visualizer;

public class MessageRepository : IMessageRepository
{
    private const int MaxSize = 1000;
    public event EventHandler<ProxyMessage>? OnMessage;

    public ImmutableArray<ProxyMessage> Messages { get; private set; } = ImmutableArray<ProxyMessage>.Empty;

    public Task AddAsync(ProxyMessage item)
    {
        var temp = Messages.Insert(0, item);
        if (temp.Length > MaxSize)
            temp = temp.RemoveAt(MaxSize);
        Messages = temp;
        OnMessage?.Invoke(this, item);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        Messages = ImmutableArray<ProxyMessage>.Empty;
        OnMessage?.Invoke(this, null!);
    }
}
