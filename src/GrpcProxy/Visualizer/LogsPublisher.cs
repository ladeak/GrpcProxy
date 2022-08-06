using GrpcProxy.Data;

namespace GrpcProxy.Visualizer;

public class LogsPublisher : IHostedService
{
    private readonly ILimitedMessageRepository _repository;
    private readonly ILogger<LogsPublisher> _logger;

    public LogsPublisher(ILimitedMessageRepository repository, ILogger<LogsPublisher> logger)
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
            PublisherLogger.RequestMessage(_logger, e);
        else if (e.Direction == MessageDirection.Response)
            PublisherLogger.ResponseMessage(_logger, e);
        else
            PublisherLogger.ProxyErrorMessage(_logger, e);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _repository.OnMessage -= PublishMessage;
        return Task.CompletedTask;
    }
}
