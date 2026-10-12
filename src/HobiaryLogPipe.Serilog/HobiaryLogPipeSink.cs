using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Serilog.Core;
using Serilog.Events;

namespace HobiaryLogPipe.Serilog;

public sealed class HobiaryLogPipeSink : ILogEventSink, IAsyncDisposable
{
    private readonly ILogPipeWriter _writer;

    public HobiaryLogPipeSink(ILogPipeWriter writer)
    {
        _writer = writer;
    }

    public void Emit(LogEvent logEvent)
    {
        var message = new LogMessage
        {
            Level = MapLevel(logEvent.Level),
            Message = logEvent.RenderMessage(),
            Source = GetSourceContext(logEvent),
            Timestamp = logEvent.Timestamp,
        };

        // Fire-and-forget — Serilog Emit jest synchroniczny
        _ = _writer.WriteAsync(message);
    }

    private static LogPipeLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogPipeLevel.Trace,
        LogEventLevel.Debug => LogPipeLevel.Debug,
        LogEventLevel.Information => LogPipeLevel.Info,
        LogEventLevel.Warning => LogPipeLevel.Warn,
        LogEventLevel.Error => LogPipeLevel.Error,
        LogEventLevel.Fatal => LogPipeLevel.Fatal,
        _ => LogPipeLevel.Info,
    };

    private static string? GetSourceContext(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out var val) &&
            val is ScalarValue { Value: string str })
            return str;
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is IAsyncDisposable d)
            await d.DisposeAsync();
    }
}
