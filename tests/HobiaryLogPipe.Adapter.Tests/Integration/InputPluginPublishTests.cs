using System.Text;
using System.Text.Json;
using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HobiaryLogPipe.Adapter.Tests.Integration;

[Collection("RabbitMq")]
public sealed class InputPluginPublishTests : IClassFixture<RabbitMqContainerFixture>
{
    private readonly RabbitMqContainerFixture _rmq;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public InputPluginPublishTests(RabbitMqContainerFixture rmq) => _rmq = rmq;

    private ConnectionFactory MakeFactory() => new()
    {
        HostName = _rmq.Hostname,
        Port = _rmq.Port,
        UserName = _rmq.Username,
        Password = _rmq.Password,
    };

    private LogPipeWriter MakeWriter(string pluginId) =>
        new(NullLogger<LogPipeWriter>.Instance, Options.Create(new InputPluginOptions
        {
            PluginId = pluginId,
            Host = _rmq.Hostname,
            Port = _rmq.Port,
            Username = _rmq.Username,
            Password = _rmq.Password,
        }));

    // ── AnnounceMessage ───────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_PublishesAnnounceMessage_WithCorrectPluginId()
    {
        // Arrange — consumer PRZED startem writera, żeby nie przegapić wiadomości
        await using var conn = await MakeFactory().CreateConnectionAsync();
        await using var ch = await conn.CreateChannelAsync();

        await ch.ExchangeDeclareAsync("hobiary.announce", ExchangeType.Fanout, durable: true);
        var q = await ch.QueueDeclareAsync(durable: false, exclusive: true, autoDelete: true);
        await ch.QueueBindAsync(q.QueueName, "hobiary.announce", routingKey: string.Empty);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.ReceivedAsync += (_, ea) =>
        {
            tcs.TrySetResult(Encoding.UTF8.GetString(ea.Body.Span));
            return Task.CompletedTask;
        };
        await ch.BasicConsumeAsync(q.QueueName, autoAck: true, consumer: consumer);

        // Act
        await using var writer = MakeWriter("svc-announce-test");
        await writer.StartAsync(CancellationToken.None);

        // Assert
        var json = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var msg = JsonSerializer.Deserialize<AnnounceMessage>(json, JsonOpts);

        Assert.NotNull(msg);
        Assert.Equal("svc-announce-test", msg!.PluginId);
        Assert.Equal("json", msg.MessageFormat);
    }

    [Fact]
    public async Task StartAsync_PublishesAnnounceMessage_WithDefaultFieldMapping()
    {
        await using var conn = await MakeFactory().CreateConnectionAsync();
        await using var ch = await conn.CreateChannelAsync();

        await ch.ExchangeDeclareAsync("hobiary.announce", ExchangeType.Fanout, durable: true);
        var q = await ch.QueueDeclareAsync(durable: false, exclusive: true, autoDelete: true);
        await ch.QueueBindAsync(q.QueueName, "hobiary.announce", routingKey: string.Empty);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.ReceivedAsync += (_, ea) =>
        {
            tcs.TrySetResult(Encoding.UTF8.GetString(ea.Body.Span));
            return Task.CompletedTask;
        };
        await ch.BasicConsumeAsync(q.QueueName, autoAck: true, consumer: consumer);

        await using var writer = MakeWriter("svc-mapping-test");
        await writer.StartAsync(CancellationToken.None);

        var json = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var msg = JsonSerializer.Deserialize<AnnounceMessage>(json, JsonOpts);

        Assert.NotNull(msg);
        Assert.Equal("level", msg!.FieldMapping.Level);
        Assert.Equal("message", msg.FieldMapping.Message);
        Assert.Equal("source", msg.FieldMapping.Source);
        Assert.Equal("service", msg.FieldMapping.Service);
    }

    // ── LogMessage ────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_PublishesLogMessage_WithCorrectRoutingKey()
    {
        const string pluginId = "svc-data-test";

        await using var conn = await MakeFactory().CreateConnectionAsync();
        await using var ch = await conn.CreateChannelAsync();

        // Ustaw exchange + queue PRZED startem writera
        await ch.ExchangeDeclareAsync("hobiary.data", ExchangeType.Direct, durable: true);
        var queueName = $"hobiary.data.{pluginId}";
        await ch.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false);
        await ch.QueueBindAsync(queueName, "hobiary.data", routingKey: pluginId);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.ReceivedAsync += (_, ea) =>
        {
            tcs.TrySetResult(Encoding.UTF8.GetString(ea.Body.Span));
            return Task.CompletedTask;
        };
        await ch.BasicConsumeAsync(queueName, autoAck: true, consumer: consumer);

        await using var writer = MakeWriter(pluginId);
        await writer.StartAsync(CancellationToken.None);

        // Act
        await writer.WriteAsync(new LogMessage
        {
            Level = LogPipeLevel.Error,
            Message = "Integration test error",
            Source = "TestSource",
            Service = "test-svc",
        });

        // Assert
        var json = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("error", root.GetProperty("level").GetString());
        Assert.Equal("Integration test error", root.GetProperty("message").GetString());
        Assert.Equal("TestSource", root.GetProperty("source").GetString());
        Assert.Equal("test-svc", root.GetProperty("service").GetString());
    }

    [Theory]
    [InlineData(LogPipeLevel.Trace, "trace")]
    [InlineData(LogPipeLevel.Debug, "debug")]
    [InlineData(LogPipeLevel.Info, "info")]
    [InlineData(LogPipeLevel.Warn, "warn")]
    [InlineData(LogPipeLevel.Fatal, "fatal")]
    public async Task WriteAsync_MapsLogLevel_ToLowerCaseString(LogPipeLevel level, string expectedStr)
    {
        var pluginId = $"svc-level-{level.ToString().ToLower()}";

        await using var conn = await MakeFactory().CreateConnectionAsync();
        await using var ch = await conn.CreateChannelAsync();

        await ch.ExchangeDeclareAsync("hobiary.data", ExchangeType.Direct, durable: true);
        var queueName = $"hobiary.data.{pluginId}";
        await ch.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false);
        await ch.QueueBindAsync(queueName, "hobiary.data", routingKey: pluginId);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.ReceivedAsync += (_, ea) =>
        {
            tcs.TrySetResult(Encoding.UTF8.GetString(ea.Body.Span));
            return Task.CompletedTask;
        };
        await ch.BasicConsumeAsync(queueName, autoAck: true, consumer: consumer);

        await using var writer = MakeWriter(pluginId);
        await writer.StartAsync(CancellationToken.None);

        await writer.WriteAsync(new LogMessage { Level = level, Message = "level test" });

        var json = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expectedStr, doc.RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public async Task RealAnnounce_DoesNotEmitOffline_ImmediatelyAfterRegistration_WithoutHeartbeat()
    {
        var pluginId = $"svc-heartbeat-{Guid.NewGuid():N}";
        var tempRoot = Path.Combine(Path.GetTempPath(), "hobiary-logpipe-heartbeat-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var registry = new PluginRegistry();
        var sseHub = new SseHub();
        var metrics = new PipeMetrics();
        var adapterOptions = new OptionsMonitorStub<AdapterOptions>(new AdapterOptions
        {
            BaseLogDirectory = tempRoot,
            AllowedPlugins = [pluginId],
            HeartbeatTimeoutSeconds = 90,
        });
        var rabbitOptions = Options.Create(new RabbitMqOptions
        {
            Host = _rmq.Hostname,
            Port = _rmq.Port,
            Username = _rmq.Username,
            Password = _rmq.Password,
        });

        var adapter = new AdapterService(
            NullLogger<AdapterService>.Instance,
            adapterOptions,
            new RabbitMqAdapterMessageTransport(
                new RabbitMqConnectionManager(
                    NullLogger<RabbitMqConnectionManager>.Instance,
                    rabbitOptions,
                    metrics,
                    new RabbitMqConnectionFactory(rabbitOptions.Value)),
                new RabbitMqConsumerTransport(
                    NullLogger<RabbitMqConsumerTransport>.Instance,
                    rabbitOptions)),
            new LogMessageProcessor(
                NullLogger<LogMessageProcessor>.Instance,
                registry,
                metrics,
                sseHub,
                adapterOptions.CurrentValue),
            new PluginRegistrationService(
                NullLogger<PluginRegistrationService>.Instance,
                adapterOptions,
                registry),
            new DeadLetterHandler(
                NullLogger<DeadLetterHandler>.Instance,
                adapterOptions,
                metrics),
            registry);

        var writer = MakeWriter(pluginId);
        var reader = sseHub.Subscribe();
        var monitor = new HeartbeatMonitor(
            registry,
            sseHub,
            adapterOptions,
            NullLogger<HeartbeatMonitor>.Instance);

        try
        {
            await adapter.StartAsync(CancellationToken.None);
            await writer.StartAsync(CancellationToken.None);

            await WaitUntilAsync(
                condition: () => registry.Contains(pluginId),
                timeout: TimeSpan.FromSeconds(10),
                message: $"Plugin '{pluginId}' was not registered by AdapterService after announce.");

            await monitor.RunCheckAsync(CancellationToken.None);

            Assert.False(reader.TryRead(out _));
        }
        finally
        {
            sseHub.Unsubscribe(reader);
            await writer.StopAsync(CancellationToken.None);
            await adapter.StopAsync(CancellationToken.None);
            await writer.DisposeAsync();
            await adapter.DisposeAsync();

            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string message)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException(message);
    }

    private sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
