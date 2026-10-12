using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HobiaryLogPipe.Adapter;

internal sealed class RabbitMqConnectionManager : IAsyncDisposable
{
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private readonly RabbitMqOptions _options;
    private readonly PipeMetrics _metrics;
    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _reconnectLock = new();
    private Task? _reconnectTask;
    private volatile ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;

    public RabbitMqConnectionManager(
        ILogger<RabbitMqConnectionManager> logger,
        IOptions<RabbitMqOptions> options,
        PipeMetrics metrics,
        IRabbitMqConnectionFactory connectionFactory)
    {
        _logger = logger;
        _options = options.Value;
        _metrics = metrics;
        _connectionFactory = connectionFactory;
    }

    public IConnection? Connection { get; private set; }

    public ConnectionStatus ConnectionStatus => _connectionStatus;

    public Task StartAsync(RabbitMqConnectionCallbacks callbacks, CancellationToken cancellationToken)
        => ConnectAsync(callbacks, cancellationToken);

    public async Task StopAsync()
    {
        Task? reconnectTask;
        lock (_reconnectLock)
        {
            _connectionStatus = ConnectionStatus.Disconnected;
            _lifetimeCancellation.Cancel();
            reconnectTask = _reconnectTask;
        }

        if (reconnectTask is not null)
            await reconnectTask;

        if (Connection is not null)
        {
            await Connection.DisposeAsync();
            Connection = null;
        }
    }

    private async Task ConnectAsync(
        RabbitMqConnectionCallbacks callbacks,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        Connection = connection;
        connection.ConnectionShutdownAsync += (sender, eventArgs)
            => OnConnectionShutdownAsync(sender, eventArgs, callbacks);

        try
        {
            await callbacks.ConfigureConnection(cancellationToken);
            _connectionStatus = ConnectionStatus.Connected;
        }
        catch
        {
            await DisposeConnectionAsync(callbacks.DisposeChannels);
            throw;
        }
    }

    private Task OnConnectionShutdownAsync(
        object sender,
        ShutdownEventArgs eventArgs,
        RabbitMqConnectionCallbacks callbacks)
    {
        lock (_reconnectLock)
        {
            if (_lifetimeCancellation.IsCancellationRequested ||
                _reconnectTask is { IsCompleted: false })
                return Task.CompletedTask;

            _connectionStatus = ConnectionStatus.Reconnecting;
            _logger.LogWarning(
                "RabbitMQ connection lost ({Reason}). Scheduling reconnect...",
                eventArgs.ReplyText);
            _reconnectTask = Task.Run(
                () => ReconnectLoopAsync(callbacks, _lifetimeCancellation.Token),
                CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    private async Task ReconnectLoopAsync(
        RabbitMqConnectionCallbacks callbacks,
        CancellationToken cancellationToken)
    {
        _metrics.IncrementReconnects();
        var delaySeconds = (double)_options.ReconnectDelaySeconds;
        var maxDelaySeconds = (double)_options.ReconnectMaxDelaySeconds;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var jitter = delaySeconds * _options.ReconnectJitterFactor * (Random.Shared.NextDouble() * 2 - 1);
                var actualDelay = Math.Clamp(delaySeconds + jitter, 1.0, maxDelaySeconds);
                _logger.LogWarning("Reconnect in {Delay:F1}s...", actualDelay);
                await Task.Delay(TimeSpan.FromSeconds(actualDelay), cancellationToken);
                await DisposeConnectionAsync(callbacks.DisposeChannels);
                await ConnectAsync(callbacks, cancellationToken);
                _logger.LogInformation("Reconnected to RabbitMQ successfully.");
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reconnect failed.");
                delaySeconds = Math.Min(delaySeconds * _options.ReconnectBackoffMultiplier, maxDelaySeconds);
            }
        }
    }

    private async Task DisposeConnectionAsync(Func<Task> disposeChannels)
    {
        await disposeChannels();

        if (Connection is not null)
        {
            await Connection.DisposeAsync();
            Connection = null;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}