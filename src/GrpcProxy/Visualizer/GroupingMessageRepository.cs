using System.Collections.Immutable;
using GrpcProxy.Grpc;

namespace GrpcProxy.Visualizer;

public record struct ProxyMessageChain(Guid Id, ImmutableArray<ProxyMessage> Chain);

public interface IGroupingMessageRepository : IMessageRepository<ProxyMessageChain>
{
}

public class GroupingMessageRepository : IGroupingMessageRepository
{
    public event EventHandler<ProxyMessage>? OnMessage;

    private ImmutableList<ProxyMessageChain> _chains = ImmutableList<ProxyMessageChain>.Empty;

    public static int MaxSize { get; } = 1000;

    public ICollection<ProxyMessageChain> Messages
    {
        get
        {
            var result = new List<ProxyMessageChain>();
            var temp = _chains;
            foreach (var chain in temp)
            {
                result.Add(chain);
            }
            return result;
        }
    }

    public Task AddAsync(ProxyMessage item)
    {
        var temp = _chains;
        var chain = temp.Find(x => x.Id == item.ProxyCallId);
        temp = temp.Remove(chain);

        ImmutableArray<ProxyMessage> currentChain;
        if (chain == default(ProxyMessageChain))
            currentChain = ImmutableArray<ProxyMessage>.Empty;
        else
            currentChain = chain.Chain;

        currentChain = currentChain.Add(item);
        chain = chain with { Id = item.ProxyCallId, Chain = currentChain };

        temp = temp.Insert(0, chain);

        if (temp.Count > MaxSize)
            temp = temp.RemoveAt(MaxSize);

        _chains = temp;
        OnMessage?.Invoke(this, item);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _chains = _chains.Clear();
        OnMessage?.Invoke(this, null!);
    }
}
