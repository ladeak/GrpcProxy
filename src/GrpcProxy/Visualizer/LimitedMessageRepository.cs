using System.Collections.Immutable;
using GrpcProxy.Data;

namespace GrpcProxy.Visualizer;

public interface ILimitedMessageRepository : IMessageRepository<ProxyMessage>
{
}

public class LimitedMessageRepository : ILimitedMessageRepository
{
    private const int MaxSize = 1000;
    public event EventHandler<ProxyMessage>? OnMessage;

    private ImmutableArray<ProxyMessage> _messages = ImmutableArray<ProxyMessage>.Empty;

    public ICollection<ProxyMessage> Messages => _messages;

    public bool IsEnabled { get; private set; } = true;

    public Task AddAsync(ProxyMessage item)
    {
        if (!IsEnabled)
            return Task.CompletedTask;

        var temp = _messages.Insert(0, item);
        if (temp.Length > MaxSize)
            temp = temp.RemoveAt(MaxSize);
        _messages = temp;
        OnMessage?.Invoke(this, item);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _messages = ImmutableArray<ProxyMessage>.Empty;
        OnMessage?.Invoke(this, null!);
    }

    public void Disable()
    {
        IsEnabled = false;
    }

    public void Enable()
    {
        IsEnabled = true;
    }
}
