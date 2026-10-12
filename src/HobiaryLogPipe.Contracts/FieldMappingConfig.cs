namespace HobiaryLogPipe.Contracts;

public sealed class FieldMappingConfig
{
    /// <summary>Nazwa pola "level" w JSONie pluginu. Domyślnie: "level".</summary>
    public string Level { get; init; } = "level";

    /// <summary>Nazwa pola "message" w JSONie pluginu. Domyślnie: "message".</summary>
    public string Message { get; init; } = "message";

    /// <summary>Nazwa pola "source" w JSONie pluginu. Domyślnie: "source".</summary>
    public string Source { get; init; } = "source";

    /// <summary>Nazwa pola "service" w JSONie pluginu. Domyślnie: "service".</summary>
    public string Service { get; init; } = "service";

    /// <summary>Nazwa pola trace ID (hex string 32 znaków) w JSONie pluginu. Domyślnie: "traceId".</summary>
    public string TraceId { get; init; } = "traceId";

    /// <summary>Nazwa pola correlation ID (hex string 32 znaków lub GUID) w JSONie pluginu. Domyślnie: "correlationId".</summary>
    public string CorrelationId { get; init; } = "correlationId";

    /// <summary>Nazwa pola span ID (hex string 16 znaków lub uint64) w JSONie pluginu. Domyślnie: "spanId".</summary>
    public string SpanId { get; init; } = "spanId";
}
