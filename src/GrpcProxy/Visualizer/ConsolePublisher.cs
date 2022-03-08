using GrpcProxy.Grpc;

namespace GrpcProxy.Visualizer;

public class ConsolePublisher : IHostedService
{
    private readonly IMessageRepository _repository;
    private readonly ILogger<ConsolePublisher> _logger;

    public ConsolePublisher(IMessageRepository repository, ILogger<ConsolePublisher> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _repository.OnMessage += PublishMessage;
        return Task.CompletedTask;
    }

    private void PublishMessage(object? sender, ProxyMessage e)
    {
        if (e == null)
            return;
        if (e.Direction == MessageDirection.Request)
            PulisherLogger.RequestMessage(_logger, e);
        else
            PulisherLogger.ResponseMessage(_logger, e);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _repository.OnMessage -= PublishMessage;
        return Task.CompletedTask;
    }
}
