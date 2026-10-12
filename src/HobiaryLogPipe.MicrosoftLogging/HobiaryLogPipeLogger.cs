using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging;

namespace HobiaryLogPipe.MicrosoftLogging;

/// <summary>
/// ILogger implementacja zapisująca do ILogPipeWriter.
/// </summary>
internal sealed class HobiaryLogPipeLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ILogPipeWriter _writer;

    internal HobiaryLogPipeLogger(string categoryName, ILogPipeWriter writer)
    {
        _categoryName = categoryName;
        _writer = writer;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception is not null)
            message = $"{message}{Environment.NewLine}{exception}";

        var pipeLevel = MapLevel(logLevel);

        var record = new LogMessage
        {
            Level = pipeLevel,
            Message = message,
            Source = _categoryName,
            Timestamp = DateTimeOffset.UtcNow,
        };

        _ = _writer.WriteAsync(record, CancellationToken.None);
    }

    internal static LogPipeLevel MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogPipeLevel.Trace,
        LogLevel.Debug => LogPipeLevel.Debug,
        LogLevel.Information => LogPipeLevel.Info,
        LogLevel.Warning => LogPipeLevel.Warn,
        LogLevel.Error => LogPipeLevel.Error,
        LogLevel.Critical => LogPipeLevel.Fatal,
        _ => LogPipeLevel.Info,
    };
}
