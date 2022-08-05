using GrpcProxy.Grpc;

namespace GrpcProxy.Visualizer;

public class MessageMultiplexer : BackgroundService
{
    private readonly IProxyMessageMediator _mediator;
    private readonly IEnumerable<IMessageRepositoryIngress> _repositories;

    public MessageMultiplexer(IProxyMessageMediator mediator, IEnumerable<IMessageRepositoryIngress> repositories)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        await foreach (var item in _mediator.ChannelReader.ReadAllAsync(token))
        {
            await Task.WhenAll(_repositories.Select(x => x.AddAsync(item)));
        }
    }
}


