using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.InputPlugin;

public sealed class InputPluginOptions
{
    public LogPipeTransport Transport { get; set; } = LogPipeTransport.RabbitMq;
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Bazowy katalog logów dla trybu Direct. Finalna ścieżka to {BaseLogDirectory}/{PluginId}.
    /// </summary>
    public string BaseLogDirectory { get; set; } = "logs";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    public string AnnounceExchange { get; set; } = "hobiary.announce";
    public string DataExchange { get; set; } = "hobiary.data";
    public int ReconnectDelaySeconds { get; set; } = 5;

    public FieldMappingConfig FieldMapping { get; set; } = new();

    /// <summary>Opcjonalne szyfrowanie transportu AMQP przez TLS.</summary>
    public AmqpTlsOptions Tls { get; set; } = new();

    /// <summary>Opcjonalne szyfrowanie payload wiadomości (AES-256-GCM, E2E).</summary>
    public PayloadEncryptionOptions PayloadEncryption { get; set; } = new();

    /// <summary>
    /// Kompresja payload wiadomości danych (gzip). Domyślnie wyłączona.
    /// Gdy włączona, adapter musi ją obsługiwać po stronie konsumenta.
    /// </summary>
    public bool GzipEnabled { get; set; } = false;

    /// <summary>
    /// Interwał wysyłania heartbeat do adaptera (w sekundach). 0 = wyłączone.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;
}
