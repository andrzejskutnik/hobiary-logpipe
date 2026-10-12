namespace HobiaryLogPipe.Contracts;

/// <summary>
/// Opcje TLS dla połączenia AMQP (RabbitMQ).
/// Odpowiada sekcji konfiguracji "Tls" wewnątrz "RabbitMQ" lub "HobiaryInputPlugin".
/// </summary>
public sealed class AmqpTlsOptions
{
    /// <summary>Czy szyfrowanie TLS jest włączone. Domyślnie false.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Nazwa serwera do walidacji certyfikatu (SNI).
    /// Jeśli null — używana jest wartość Host z opcji połączenia.
    /// </summary>
    public string? ServerName { get; init; }

    /// <summary>
    /// Ścieżka do pliku certyfikatu klienta (.pfx / .p12).
    /// Opcjonalne — wymagane tylko przy mTLS.
    /// </summary>
    public string? CertPath { get; init; }

    /// <summary>Hasło do certyfikatu klienta. Opcjonalne.</summary>
    public string? CertPassphrase { get; init; }

    /// <summary>
    /// Czy akceptować nieprawidłowe certyfikaty serwera.
    /// UWAGA: używać wyłącznie w środowiskach deweloperskich!
    /// </summary>
    public bool AcceptInvalidCertificates { get; init; } = false;
}
