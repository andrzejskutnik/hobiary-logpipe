using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Health check sprawdzający stan połączenia AMQP.
/// Raportuje Healthy gdy adapter jest podłączony, Degraded przy reconnect, Unhealthy gdy brak połączenia.
/// </summary>
internal sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IAdapterMessageTransport _messageTransport;

    public RabbitMqHealthCheck(IAdapterMessageTransport messageTransport)
    {
        _messageTransport = messageTransport;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = _messageTransport.ConnectionStatus;
        return Task.FromResult(status switch
        {
            ConnectionStatus.Connected    => HealthCheckResult.Healthy("RabbitMQ połączony."),
            ConnectionStatus.Reconnecting => HealthCheckResult.Degraded("RabbitMQ reconnect w toku."),
            _                             => HealthCheckResult.Unhealthy("RabbitMQ rozłączony."),
        });
    }
}

public enum ConnectionStatus { Connected, Reconnecting, Disconnected }
