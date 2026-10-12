using System.Text.Json.Serialization;

namespace HobiaryLogPipe.Contracts;

/// <summary>
/// Kanoniczny rekord loga — znormalizowana forma po zastosowaniu fieldMapping.
/// InputPlugin serializuje go zgodnie z FieldMappingConfig przed wysyłką.
/// Adapter deserializuje raw JSON do tej struktury po stronie konsumenta.
/// </summary>
public sealed class LogMessage
{
    [JsonPropertyName("level")]
    public LogPipeLevel Level { get; init; } = LogPipeLevel.Info;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("service")]
    public string? Service { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Trace ID jako hex string 32 znaków (16 bajtów). Null = brak kontekstu tracing.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    /// <summary>Correlation ID jako hex string 32 znaków (16 bajtów) lub GUID. Null = brak.</summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    /// <summary>64-bit span ID. 0 = brak kontekstu tracing.</summary>
    [JsonIgnore]
    public ulong SpanId { get; init; }
}
