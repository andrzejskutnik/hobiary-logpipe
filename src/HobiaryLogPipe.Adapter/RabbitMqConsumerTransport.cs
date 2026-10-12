using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HobiaryLogPipe.Adapter;

internal sealed class RabbitMqConsumerTransport
{
    private readonly ILogger<RabbitMqConsumerTransport> _logger;
    private readonly RabbitMqOptions _options;
    private IChannel? _announceChannel;
    private IChannel? _dataChannel;
    private IChannel? _dlqChannel;
    private readonly BatchAckTracker _batchAckTracker;
    private readonly BatchAckTimeoutScheduler _batchAckTimeoutScheduler;
    private readonly SemaphoreSlim _dataChannelGate = new(1, 1);

    public RabbitMqConsumerTransport(
        ILogger<RabbitMqConsumerTransport> logger,
        IOptions<RabbitMqOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _batchAckTracker = new BatchAckTracker(_options.BatchAckSize, _options.BatchAckTimeoutMs);
        _batchAckTimeoutScheduler = new BatchAckTimeoutScheduler(
            exception => _logger.LogError(exception, "Batch ACK timeout flush failed."));
    }

    internal async Task ConfigureAsync(
        IConnection connection,
        Func<IncomingAnnounceMessage, Task<MessageDisposition>> announceHandler,
        Func<IncomingLogMessage, Task<MessageDisposition>> dataHandler,
        Func<IncomingDeadLetterMessage, Task<DeadLetterDecision>> dlqHandler,
        CancellationToken cancellationToken)
    {
        _announceChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _announceChannel.ExchangeDeclareAsync(
            exchange: _options.AnnounceExchange,
            type: ExchangeType.Fanout,
            durable: true,
            cancellationToken: cancellationToken);
        await _announceChannel.QueueDeclareAsync(
            queue: _options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var announceQueue = await _announceChannel.QueueDeclareAsync(
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: cancellationToken);
        await _announceChannel.QueueBindAsync(
            queue: announceQueue.QueueName,
            exchange: _options.AnnounceExchange,
            routingKey: string.Empty,
            cancellationToken: cancellationToken);

        var announceConsumer = new AsyncEventingBasicConsumer(_announceChannel);
        announceConsumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var message = new IncomingAnnounceMessage(eventArgs.Body.ToArray());
            var disposition = await announceHandler(message);
            if (disposition == MessageDisposition.Acknowledge)
                await AcknowledgeAnnounceAsync(eventArgs.DeliveryTag);
            else
                await RejectAnnounceAsync(eventArgs.DeliveryTag);
        };
        await _announceChannel.BasicConsumeAsync(
            queue: announceQueue.QueueName,
            autoAck: false,
            consumer: announceConsumer,
            cancellationToken: cancellationToken);

        _dataChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _dataChannel.ExchangeDeclareAsync(
            exchange: _options.DataExchange,
            type: ExchangeType.Direct,
            durable: true,
            cancellationToken: cancellationToken);
        await _dataChannel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)_options.PrefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        _dlqChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _dlqChannel.QueueDeclareAsync(
            queue: _options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        var dlqConsumer = new AsyncEventingBasicConsumer(_dlqChannel);
        dlqConsumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var headers = CopyHeaders(eventArgs.BasicProperties?.Headers);
            var message = new IncomingDeadLetterMessage(
                eventArgs.RoutingKey,
                eventArgs.Body.ToArray(),
                headers,
                eventArgs.BasicProperties?.ContentType,
                eventArgs.BasicProperties?.ContentEncoding);
            var decision = await dlqHandler(message);

            if (decision.ShouldRetry && _dataChannel is not null)
            {
                var exchange = decision.Exchange == "unknown"
                    ? _options.DataExchange
                    : decision.Exchange!;
                await PublishRetryAsync(message, exchange, decision.RetryCount);
            }
            else if (decision.ShouldRetry)
                _logger.LogWarning("DLQ retry could not be published because the data channel is unavailable; parking message.");

            await AcknowledgeDlqAsync(eventArgs.DeliveryTag);
        };
        await _dlqChannel.BasicConsumeAsync(
            queue: _options.DeadLetterQueue,
            autoAck: false,
            consumer: dlqConsumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation("DLQ consumer started on '{Queue}'", _options.DeadLetterQueue);
    }

    internal async Task BindDataQueueAsync(
        string pluginId,
        Func<IncomingLogMessage, Task<MessageDisposition>> dataHandler)
    {
        if (_dataChannel is null)
            return;

        await _dataChannelGate.WaitAsync();
        try
        {
            if (_dataChannel is null)
                return;

            var queueName = $"hobiary.data.{pluginId}";
            await _dataChannel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false);
            await _dataChannel.QueueBindAsync(
                queue: queueName,
                exchange: _options.DataExchange,
                routingKey: pluginId);

            var consumer = new AsyncEventingBasicConsumer(_dataChannel);
            consumer.ReceivedAsync += async (_, eventArgs) =>
            {
                var message = new IncomingLogMessage(
                    eventArgs.RoutingKey,
                    eventArgs.Body.ToArray(),
                    CopyHeaders(eventArgs.BasicProperties?.Headers));
                var disposition = await dataHandler(message);
                if (disposition == MessageDisposition.Acknowledge)
                    await BatchAcknowledgeAsync(eventArgs.DeliveryTag);
                else
                    await RejectDataAsync(eventArgs.DeliveryTag);
            };
            await _dataChannel.BasicConsumeAsync(
                queue: queueName,
                autoAck: false,
                consumer: consumer);

            _logger.LogInformation("Data queue '{Queue}' bound for plugin '{PluginId}'", queueName, pluginId);
        }
        finally
        {
            _dataChannelGate.Release();
        }
    }

    private async Task RejectAnnounceAsync(ulong deliveryTag)
        => await DeadLetterAsync(_announceChannel!, deliveryTag);

    private async Task AcknowledgeAnnounceAsync(ulong deliveryTag)
        => await _announceChannel!.BasicAckAsync(deliveryTag, multiple: false);

    private async Task RejectDataAsync(ulong deliveryTag)
    {
        await _dataChannelGate.WaitAsync();
        try
        {
            if (_dataChannel is not null)
                await DeadLetterAsync(_dataChannel, deliveryTag);
        }
        finally
        {
            _dataChannelGate.Release();
        }
    }

    private async Task PublishRetryAsync(
        IncomingDeadLetterMessage message,
        string exchange,
        int retryCount)
    {
        var headers = message.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(message.Headers);
        headers["x-hobiary-retry-count"] = retryCount;
        var properties = new BasicProperties
        {
            ContentType = message.ContentType,
            ContentEncoding = message.ContentEncoding,
            Headers = headers,
        };

        await _dataChannelGate.WaitAsync();
        try
        {
            if (_dataChannel is not null)
            {
                await _dataChannel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: message.RoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: message.Body,
                    cancellationToken: CancellationToken.None);
            }
        }
        finally
        {
            _dataChannelGate.Release();
        }
    }

    private async Task AcknowledgeDlqAsync(ulong deliveryTag)
        => await _dlqChannel!.BasicAckAsync(deliveryTag, multiple: false);

    internal async Task DisposeChannelsAsync()
    {
        await _dataChannelGate.WaitAsync();
        try
        {
            _batchAckTimeoutScheduler.Cancel();
            _batchAckTracker.Reset();

            if (_announceChannel is not null)
            {
                await _announceChannel.DisposeAsync();
                _announceChannel = null;
            }
            if (_dataChannel is not null)
            {
                await _dataChannel.DisposeAsync();
                _dataChannel = null;
            }
            if (_dlqChannel is not null)
            {
                await _dlqChannel.DisposeAsync();
                _dlqChannel = null;
            }
        }
        finally
        {
            _dataChannelGate.Release();
        }
    }

    private async Task BatchAcknowledgeAsync(ulong deliveryTag)
    {
        await _dataChannelGate.WaitAsync();
        try
        {
            if (_dataChannel is null)
                return;

            var hadPending = _batchAckTracker.HasPending;
            var decision = _batchAckTracker.Record(deliveryTag, Environment.TickCount64);
            if (decision is not null)
            {
                _batchAckTimeoutScheduler.Cancel();
                await _dataChannel.BasicAckAsync(decision.DeliveryTag, decision.Multiple);
            }
            else if (!hadPending && _options.BatchAckSize > 0)
            {
                _batchAckTimeoutScheduler.Schedule(
                    TimeSpan.FromMilliseconds(_options.BatchAckTimeoutMs),
                    FlushBatchAckOnTimeoutAsync);
            }
        }
        finally
        {
            _dataChannelGate.Release();
        }
    }

    private async Task FlushBatchAckOnTimeoutAsync(CancellationToken cancellationToken)
    {
        await _dataChannelGate.WaitAsync(cancellationToken);
        try
        {
            if (cancellationToken.IsCancellationRequested || _dataChannel is null)
                return;

            var decision = _batchAckTracker.FlushPending();
            if (decision is not null)
                await _dataChannel.BasicAckAsync(decision.DeliveryTag, decision.Multiple);
        }
        finally
        {
            _dataChannelGate.Release();
        }
    }

    private static IDictionary<string, object?>? CopyHeaders(IDictionary<string, object?>? headers)
        => headers is null ? null : new Dictionary<string, object?>(headers);

    private static async Task DeadLetterAsync(IChannel channel, ulong deliveryTag)
        => await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
}
