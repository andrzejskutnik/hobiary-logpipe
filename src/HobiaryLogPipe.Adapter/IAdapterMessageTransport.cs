namespace HobiaryLogPipe.Adapter;

internal interface IAdapterMessageTransport : IAsyncDisposable
{
    ConnectionStatus ConnectionStatus { get; }

    Task StartAsync(
        Func<IncomingAnnounceMessage, Task<MessageDisposition>> announceHandler,
        Func<IncomingLogMessage, Task<MessageDisposition>> dataHandler,
        Func<IncomingDeadLetterMessage, Task<DeadLetterDecision>> deadLetterHandler,
        Func<CancellationToken, Task> connectionReady,
        CancellationToken cancellationToken);

    Task BindDataQueueAsync(
        string pluginId,
        Func<IncomingLogMessage, Task<MessageDisposition>> dataHandler);

    Task StopAsync();
}

internal sealed record RabbitMqConnectionCallbacks(
    Func<CancellationToken, Task> ConfigureConnection,
    Func<Task> DisposeChannels);

internal sealed record RabbitMqAdapterHandlers(
    Func<IncomingAnnounceMessage, Task<MessageDisposition>> Announce,
    Func<IncomingLogMessage, Task<MessageDisposition>> Data,
    Func<IncomingDeadLetterMessage, Task<DeadLetterDecision>> DeadLetter,
    Func<CancellationToken, Task> ConnectionReady);

internal sealed class RabbitMqAdapterMessageTransport : IAdapterMessageTransport
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqConsumerTransport _consumerTransport;

    public RabbitMqAdapterMessageTransport(
        RabbitMqConnectionManager connectionManager,
        RabbitMqConsumerTransport consumerTransport)
    {
        _connectionManager = connectionManager;
        _consumerTransport = consumerTransport;
    }

    public ConnectionStatus ConnectionStatus => _connectionManager.ConnectionStatus;

    public async Task StartAsync(
        Func<IncomingAnnounceMessage, Task<MessageDisposition>> announceHandler,
        Func<IncomingLogMessage, Task<MessageDisposition>> dataHandler,
        Func<IncomingDeadLetterMessage, Task<DeadLetterDecision>> deadLetterHandler,
        Func<CancellationToken, Task> connectionReady,
        CancellationToken cancellationToken)
    {
        var handlers = new RabbitMqAdapterHandlers(
            announceHandler,
            dataHandler,
            deadLetterHandler,
            connectionReady);
        var callbacks = new RabbitMqConnectionCallbacks(
            token => ConfigureConnectionAsync(handlers, token),
            _consumerTransport.DisposeChannelsAsync);

        await _connectionManager.StartAsync(callbacks, cancellationToken);
    }

    public Task BindDataQueueAsync(
        string pluginId,
        Func<IncomingLogMessage, Task<MessageDisposition>> dataHandler)
        => _consumerTransport.BindDataQueueAsync(pluginId, dataHandler);

    public async Task StopAsync()
    {
        await _consumerTransport.DisposeChannelsAsync();
        await _connectionManager.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _connectionManager.DisposeAsync();
    }

    private async Task ConfigureConnectionAsync(
        RabbitMqAdapterHandlers handlers,
        CancellationToken cancellationToken)
    {
        await _consumerTransport.ConfigureAsync(
            _connectionManager.Connection!,
            handlers.Announce,
            handlers.Data,
            handlers.DeadLetter,
            cancellationToken);
        await handlers.ConnectionReady(cancellationToken);
    }
}