using System.Text.Json.Serialization;

namespace HobiaryLogPipe.Contracts;

/// <summary>
/// Komunikat rejestracyjny wysyłany przez plugin przy starcie
/// na exchange hobiary.announce (fanout).
/// </summary>
public sealed class AnnounceMessage
{
    [JsonPropertyName("pluginId")]
    public string PluginId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    /// <summary>Format wiadomości danych. Obecnie obsługiwany: "json".</summary>
    [JsonPropertyName("messageFormat")]
    public string MessageFormat { get; init; } = "json";

    [JsonPropertyName("fieldMapping")]
    public FieldMappingConfig FieldMapping { get; init; } = new();
}
