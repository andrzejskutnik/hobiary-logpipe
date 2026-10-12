using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using HobiaryLogPipe.OpenTelemetry;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using System.Diagnostics;

namespace HobiaryLogPipe.OpenTelemetry.Tests;

public sealed class HobiaryLogPipeExporterTests
{
    // ── MapLevel ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LogLevel.Trace, LogPipeLevel.Trace)]
    [InlineData(LogLevel.Debug, LogPipeLevel.Debug)]
    [InlineData(LogLevel.Information, LogPipeLevel.Info)]
    [InlineData(LogLevel.Warning, LogPipeLevel.Warn)]
    [InlineData(LogLevel.Error, LogPipeLevel.Error)]
    [InlineData(LogLevel.Critical, LogPipeLevel.Fatal)]
    public void MapLevel_MapsCorrectly(LogLevel input, LogPipeLevel expected)
    {
        Assert.Equal(expected, HobiaryLogPipeExporter.MapLevel(input));
    }

    [Fact]
    public void MapLevel_None_ReturnsInfo()
    {
        Assert.Equal(LogPipeLevel.Info, HobiaryLogPipeExporter.MapLevel(LogLevel.None));
    }

    // ── Constructor ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var writer = new StubWriter();
        var exporter = new HobiaryLogPipeExporter(writer);
        exporter.Dispose();
    }

    // ── Trace context propagation ─────────────────────────────────────────

    [Fact]
    public void Export_WithActiveActivity_PropagatesTraceId()
    {
        using var activitySource = new ActivitySource("hobiary.otel.test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "hobiary.otel.test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var captured = new List<LogMessage>();
        var writer = new CapturingWriter(captured);

        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddOpenTelemetry(o =>
                o.AddProcessor(new SimpleLogRecordExportProcessor(
                    new HobiaryLogPipeExporter(writer)))));

        var logger = loggerFactory.CreateLogger("test");

        using var activity = activitySource.StartActivity("op");
        Assert.NotNull(activity);

        logger.LogInformation("trace test");

        Assert.Single(captured);
        Assert.NotNull(captured[0].TraceId);
        Assert.Equal(32, captured[0].TraceId!.Length);
        Assert.NotEqual("00000000000000000000000000000000", captured[0].TraceId);
    }

    [Fact]
    public void Export_WithActiveActivity_PropagatesSpanId()
    {
        using var activitySource = new ActivitySource("hobiary.otel.span.test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "hobiary.otel.span.test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var captured = new List<LogMessage>();
        var writer = new CapturingWriter(captured);

        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddOpenTelemetry(o =>
                o.AddProcessor(new SimpleLogRecordExportProcessor(
                    new HobiaryLogPipeExporter(writer)))));

        var logger = loggerFactory.CreateLogger("test");

        using var activity = activitySource.StartActivity("span-op");
        Assert.NotNull(activity);

        logger.LogWarning("span test");

        Assert.Single(captured);
        Assert.NotEqual(0UL, captured[0].SpanId);
    }

    [Fact]
    public void Export_WithoutActivity_TraceIdIsNull()
    {
        var captured = new List<LogMessage>();
        var writer = new CapturingWriter(captured);

        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddOpenTelemetry(o =>
                o.AddProcessor(new SimpleLogRecordExportProcessor(
                    new HobiaryLogPipeExporter(writer)))));

        var logger = loggerFactory.CreateLogger("test");

        // Brak aktywnej Activity — brak trace context
        logger.LogError("no trace");

        Assert.Single(captured);
        Assert.Null(captured[0].TraceId);
        Assert.Equal(0UL, captured[0].SpanId);
    }
}

// ── Stubs ─────────────────────────────────────────────────────────────────

internal sealed class StubWriter : ILogPipeWriter
{
    public Task WriteAsync(LogMessage message, CancellationToken ct = default) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class CapturingWriter(List<LogMessage> captured) : ILogPipeWriter
{
    public Task WriteAsync(LogMessage message, CancellationToken ct = default)
    {
        captured.Add(message);
        return Task.CompletedTask;
    }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
