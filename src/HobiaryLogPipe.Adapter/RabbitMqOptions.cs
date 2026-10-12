using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter;

public sealed class RabbitMqOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "/";
    public string Username { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string AnnounceExchange { get; init; } = "hobiary.announce";
    public string DataExchange { get; init; } = "hobiary.data";
    public string DeadLetterQueue { get; init; } = "hobiary.unknown";
    public int PrefetchCount { get; init; } = 50;

    /// <summary>Bazowe opóźnienie pierwszego reconnect attempt (sekundy).</summary>
    public int ReconnectDelaySeconds { get; init; } = 2;

    /// <summary>Maksymalne opóźnienie reconnect (sekundy). Backoff nie przekroczy tej wartości.</summary>
    public int ReconnectMaxDelaySeconds { get; init; } = 60;

    /// <summary>Mnożnik exponential backoff (domyślnie 2.0).</summary>
    public double ReconnectBackoffMultiplier { get; init; } = 2.0;

    /// <summary>Jitter jako ułamek aktualnego opóźnienia (0–1). Losowy offset zapobiegający thundering herd.</summary>
    public double ReconnectJitterFactor { get; init; } = 0.2;

    /// <summary>Liczba wiadomości zbieranych przed MultiAck. 0 = brak batch ack (ACK per message).</summary>
    public int BatchAckSize { get; init; } = 50;

    /// <summary>Maksymalny czas (ms) oczekiwania na wypełnienie batcha ACK przed wysłaniem MultiAck.</summary>
    public int BatchAckTimeoutMs { get; init; } = 200;

    /// <summary>Opcjonalne szyfrowanie transportu AMQP przez TLS.</summary>
    public AmqpTlsOptions Tls { get; init; } = new();
}
