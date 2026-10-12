using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Opcje konfiguracyjne dla adaptera HobiaryLogPipe.
/// </summary>
public sealed class AdapterOptions
{
    /// <summary>
    /// Katalog bazowy dla logów.
    /// </summary>
    public string BaseLogDirectory { get; init; } = "logs";
    /// <summary>
    /// Lista dozwolonych pluginów.
    /// </summary>
    public string[] AllowedPlugins { get; init; } = [];
    /// <summary>
    /// Akcja do wykonania dla nieznanych pluginów (np. "deadletter").
    /// </summary>
    public string UnknownPluginAction { get; init; } = "deadletter";
    /// <summary>
    /// Klucz API dla HTTP.
    /// </summary>
    public string HttpApiKey { get; init; } = "";

    /// <summary>Opcjonalne deszyfrowanie payload wiadomości (AES-256-GCM, E2E).</summary>
    public PayloadEncryptionOptions PayloadEncryption { get; init; } = new();

    /// <summary>Maksymalna liczba prób retry dla wiadomości z DLQ. 0 = brak retry (park od razu).</summary>
    public int MaxDlqRetries { get; init; } = 3;

    /// <summary>
    /// Minimalny poziom logów per plugin. Klucz = pluginId, wartość = poziom (Trace/Debug/Info/Warn/Error/Fatal).
    /// Logi poniżej podanego poziomu są ACKowane i odrzucane bez zapisu.
    /// </summary>
    public Dictionary<string, string> PluginMinLevels { get; init; } = [];

    /// <summary>
    /// Maksymalna liczba wiadomości na sekundę per plugin (token bucket).
    /// Klucz = pluginId, wartość = maks. wiad./s (>0). Brak wpisu = brak limitu.
    /// </summary>
    public Dictionary<string, int> PluginRateLimits { get; init; } = [];

    /// <summary>
    /// Wymagane pola JSON per plugin. Wiadomości bez któregokolwiek z pól są odrzucane do DLQ.
    /// Klucz = pluginId, wartość = tablica nazw pól (po fieldMapping — oryginalne nazwy z JSON).
    /// Przykład: { "orders-service": ["level", "orderId"] }
    /// </summary>
    public Dictionary<string, string[]> PluginRequiredFields { get; init; } = [];

    /// <summary>
    /// Czas (w sekundach) po którym plugin uznawany jest za offline gdy nie wyśle heartbeat.
    /// 0 = wyłączone (heartbeaty ignorowane). Domyślnie 90 s.
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; init; } = 90;
}
