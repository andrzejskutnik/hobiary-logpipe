using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.AspNetCore;

/// <summary>
/// Opcje middleware logowania HTTP.
/// </summary>
public sealed class HobiaryHttpLoggingOptions
{
    /// <summary>Minimalne level logowane. Domyślnie Info (wszystko).</summary>
    public LogPipeLevel MinLevel { get; init; } = LogPipeLevel.Info;

    /// <summary>Wartość pola Source w LogMessage. Domyślnie "http".</summary>
    public string Source { get; init; } = "http";

    /// <summary>Wartość pola Service w LogMessage. Domyślnie pusty string.</summary>
    public string Service { get; init; } = string.Empty;
}
