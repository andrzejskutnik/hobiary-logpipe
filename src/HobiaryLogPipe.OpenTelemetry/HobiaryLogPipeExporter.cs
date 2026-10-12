using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using System.Diagnostics;

namespace HobiaryLogPipe.OpenTelemetry;

/// <summary>
/// OpenTelemetry BaseExporter wysyłający LogRecord do HobiaryLogPipe przez ILogPipeWriter.
/// </summary>
internal sealed class HobiaryLogPipeExporter : BaseExporter<LogRecord>
{
    private readonly ILogPipeWriter _writer;

    internal HobiaryLogPipeExporter(ILogPipeWriter writer) => _writer = writer;

    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var record in batch)
        {
            string? traceId = null;
            ulong spanId = 0;
            if (record.TraceId != default)
            {
                var tid = record.TraceId;
                var bytes = new byte[16];
                tid.CopyTo(bytes);
                traceId = Convert.ToHexString(bytes).ToLowerInvariant();
            }
            if (record.SpanId != default)
            {
                var sid = record.SpanId;
                var bytes = new byte[8];
                sid.CopyTo(bytes);
                spanId = BitConverter.ToUInt64(bytes);
            }

            var msg = new LogMessage
            {
                Level = MapLevel(record.LogLevel),
                Message = record.FormattedMessage ?? record.Body ?? string.Empty,
                Source = record.CategoryName,
                Timestamp = new DateTimeOffset(record.Timestamp, TimeSpan.Zero),
                TraceId = traceId,
                SpanId = spanId,
            };
            _ = _writer.WriteAsync(msg, CancellationToken.None);
        }
        return ExportResult.Success;
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
