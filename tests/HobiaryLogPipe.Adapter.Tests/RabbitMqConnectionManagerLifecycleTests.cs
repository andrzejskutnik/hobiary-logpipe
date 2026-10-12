using System.Reflection;
using HobiaryLogPipe.Adapter;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class RabbitMqConnectionManagerLifecycleTests
{
    [Fact]
    public async Task ConnectionShutdown_ReconnectsAndReconfigures_OnlyOnceForConcurrentSignals()
    {
        var options = Options.Create(new RabbitMqOptions
        {
            ReconnectDelaySeconds = 1,
            ReconnectMaxDelaySeconds = 1,
            ReconnectJitterFactor = 0,
        });
        var metrics = new PipeMetrics();
        var factory = new ControlledConnectionFactory();
        await using var manager = new RabbitMqConnectionManager(
            NullLogger<RabbitMqConnectionManager>.Instance,
            options,
            metrics,
            factory);
        var configurationCount = 0;
        var reconfigured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = CreateCallbacks(() =>
        {
            if (Interlocked.Increment(ref configurationCount) == 2)
                reconfigured.TrySetResult();
        });

        await manager.StartAsync(callbacks, CancellationToken.None);
        await factory.Connections[0].RaiseShutdownAsync();
        await factory.Connections[0].RaiseShutdownAsync();

        await reconfigured.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, factory.Connections.Count);
        Assert.Equal(1, metrics.GetSnapshot().Reconnects);
        Assert.Equal(ConnectionStatus.Connected, manager.ConnectionStatus);
        Assert.Equal(2, configurationCount);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingReconnect()
    {
        var options = Options.Create(new RabbitMqOptions
        {
            ReconnectDelaySeconds = 30,
            ReconnectMaxDelaySeconds = 30,
            ReconnectJitterFactor = 0,
        });
        var factory = new ControlledConnectionFactory();
        await using var manager = new RabbitMqConnectionManager(
            NullLogger<RabbitMqConnectionManager>.Instance,
            options,
            new PipeMetrics(),
            factory);

        await manager.StartAsync(CreateCallbacks(() => { }), CancellationToken.None);
        await factory.Connections[0].RaiseShutdownAsync();
        await manager.StopAsync();

        Assert.Single(factory.Connections);
        Assert.Equal(ConnectionStatus.Disconnected, manager.ConnectionStatus);
    }

    private static RabbitMqConnectionCallbacks CreateCallbacks(Action configured)
        => new(
            _ =>
            {
                configured();
                return Task.CompletedTask;
            },
            () => Task.CompletedTask);

    private sealed class ControlledConnectionFactory : IRabbitMqConnectionFactory
    {
        public List<FakeRabbitMqConnection> Connections { get; } = [];

        public Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
        {
            var connection = DispatchProxy.Create<IConnection, FakeRabbitMqConnection>();
            Connections.Add((FakeRabbitMqConnection)(object)connection);
            return Task.FromResult(connection);
        }
    }

    public class FakeRabbitMqConnection : DispatchProxy
    {
        private AsyncEventHandler<ShutdownEventArgs>? _shutdownHandlers;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "add_ConnectionShutdownAsync":
                    _shutdownHandlers += (AsyncEventHandler<ShutdownEventArgs>)args![0]!;
                    return null;
                case "remove_ConnectionShutdownAsync":
                    _shutdownHandlers -= (AsyncEventHandler<ShutdownEventArgs>)args![0]!;
                    return null;
                case "DisposeAsync":
                    return ValueTask.CompletedTask;
                default:
                    throw new NotSupportedException($"Unexpected IConnection call: {targetMethod?.Name}");
            }
        }

        public async Task RaiseShutdownAsync()
        {
            if (_shutdownHandlers is not null)
            {
                var eventArgs = new ShutdownEventArgs(
                    ShutdownInitiator.Application,
                    replyCode: 200,
                    replyText: "simulated shutdown");
                await _shutdownHandlers(this, eventArgs);
            }
        }
    }
}