using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using HobiaryLogPipe.Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace HobiaryLogPipe.Serilog.Tests;

/// <summary>
/// Stub ILogPipeWriter przechwytujący wywołania WriteAsync.
/// </summary>
internal sealed class CapturingWriter : ILogPipeWriter
{
    private readonly List<LogMessage> _captured = new();

    public IReadOnlyList<LogMessage> Captured => _captured;

    public Task WriteAsync(LogMessage message, CancellationToken cancellationToken = default)
    {
        _captured.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class HobiaryLogPipeSinkTests
{
    private static LogEvent MakeEvent(
        LogEventLevel level,
        string messageTemplate,
        IEnumerable<LogEventProperty>? properties = null)
    {
        var template = new MessageTemplateParser().Parse(messageTemplate);
        var props = properties?.ToArray() ?? Array.Empty<LogEventProperty>();
        return new LogEvent(DateTimeOffset.UtcNow, level, exception: null, template, props);
    }

    // ── Mapowanie poziomów Serilog → LogPipeLevel ─────────────────────────

    [Theory]
    [InlineData(LogEventLevel.Verbose, LogPipeLevel.Trace)]
    [InlineData(LogEventLevel.Debug, LogPipeLevel.Debug)]
    [InlineData(LogEventLevel.Information, LogPipeLevel.Info)]
    [InlineData(LogEventLevel.Warning, LogPipeLevel.Warn)]
    [InlineData(LogEventLevel.Error, LogPipeLevel.Error)]
    [InlineData(LogEventLevel.Fatal, LogPipeLevel.Fatal)]
    public void Emit_MapsLevel_Correctly(LogEventLevel serilogLevel, LogPipeLevel expectedPipe)
    {
        var writer = new CapturingWriter();
        var sink = new HobiaryLogPipeSink(writer);

        sink.Emit(MakeEvent(serilogLevel, "Test message"));

        Assert.Single(writer.Captured);
        Assert.Equal(expectedPipe, writer.Captured[0].Level);
    }

    // ── Treść wiadomości ───────────────────────────────────────────────────

    [Fact]
    public void Emit_RendersMessage_WithoutTemplate()
    {
        var writer = new CapturingWriter();
        var sink = new HobiaryLogPipeSink(writer);

        sink.Emit(MakeEvent(LogEventLevel.Information, "Hello world"));

        Assert.Equal("Hello world", writer.Captured[0].Message);
    }

    [Fact]
    public void Emit_RendersMessage_WithProperties()
    {
        var writer = new CapturingWriter();
        var sink = new HobiaryLogPipeSink(writer);

        var prop = new LogEventProperty("Name", new ScalarValue("Alice"));
        sink.Emit(MakeEvent(LogEventLevel.Information, "Hello {Name}", new[] { prop }));

        Assert.Equal("Hello \"Alice\"", writer.Captured[0].Message);
    }

    // ── SourceContext ──────────────────────────────────────────────────────

    [Fact]
    public void Emit_ExtractsSourceContext_WhenPresent()
    {
        var writer = new CapturingWriter();
        var sink = new HobiaryLogPipeSink(writer);

        var prop = new LogEventProperty("SourceContext", new ScalarValue("MyNamespace.MyClass"));
        sink.Emit(MakeEvent(LogEventLevel.Error, "Boom", new[] { prop }));

        Assert.Equal("MyNamespace.MyClass", writer.Captured[0].Source);
    }

    [Fact]
    public void Emit_SourceContext_IsNull_WhenAbsent()
    {
        var writer = new CapturingWriter();
        var sink = new HobiaryLogPipeSink(writer);

        sink.Emit(MakeEvent(LogEventLevel.Error, "Boom"));

        Assert.Null(writer.Captured[0].Source);
    }

    [Fact]
    public void Emit_SourceContext_IsNull_WhenNonString()
    {
        var writer = new CapturingWriter();
        var sink = new HobiaryLogPipeSink(writer);

        var prop = new LogEventProperty("SourceContext", new ScalarValue(42));
        sink.Emit(MakeEvent(LogEventLevel.Error, "Boom", new[] { prop }));

        Assert.Null(writer.Captured[0].Source);
    }

    // ── Timestamp ─────────────────────────────────────────────────────────

    [Fact]
    public void Emit_Timestamp_IsPassedThrough()
    {
        var writer = new CapturingWriter();
        var sink = new HobiaryLogPipeSink(writer);

        var ts = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var template = new MessageTemplateParser().Parse("msg");
        var ev = new LogEvent(ts, LogEventLevel.Information, null, template, Array.Empty<LogEventProperty>());

        sink.Emit(ev);

        Assert.Equal(ts, writer.Captured[0].Timestamp);
    }

    // ── Wywołanie WriteAsync ───────────────────────────────────────────────

    [Fact]
    public void Emit_CallsWriteAsync_ExactlyOnce()
    {
        var writer = new CapturingWriter();
        var sink = new HobiaryLogPipeSink(writer);

        sink.Emit(MakeEvent(LogEventLevel.Warning, "one"));
        sink.Emit(MakeEvent(LogEventLevel.Warning, "two"));
        sink.Emit(MakeEvent(LogEventLevel.Warning, "three"));

        Assert.Equal(3, writer.Captured.Count);
    }
}
