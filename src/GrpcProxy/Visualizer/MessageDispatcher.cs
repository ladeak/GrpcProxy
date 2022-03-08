using GrpcProxy.Grpc;

namespace GrpcProxy.Visualizer;

public class MessageDispatcher : BackgroundService
{
    private readonly IProxyMessageMediator _mediator;
    private readonly IMessageRepository _repository;

    public MessageDispatcher(IProxyMessageMediator mediator, IMessageRepository repository)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        await foreach (var item in _mediator.ChannelReader.ReadAllAsync(token))
        {
            await _repository.AddAsync(item);
        }
    }
}


