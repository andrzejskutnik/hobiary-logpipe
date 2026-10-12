using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using HobiaryLogPipe.MicrosoftLogging;
using Microsoft.Extensions.Logging;

namespace HobiaryLogPipe.MicrosoftLogging.Tests;

public sealed class HobiaryLogPipeLoggerTests
{
    // ── MapLevel ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LogLevel.Trace, LogPipeLevel.Trace)]
    [InlineData(LogLevel.Debug, LogPipeLevel.Debug)]
    [InlineData(LogLevel.Information, LogPipeLevel.Info)]
    [InlineData(LogLevel.Warning, LogPipeLevel.Warn)]
    [InlineData(LogLevel.Error, LogPipeLevel.Error)]
    [InlineData(LogLevel.Critical, LogPipeLevel.Fatal)]
    public void MapLevel_MapsCorrectly(LogLevel input, LogPipeLevel expected)
    {
        Assert.Equal(expected, HobiaryLogPipeLogger.MapLevel(input));
    }

    // ── Log() → WriteAsync ────────────────────────────────────────────────

    [Fact]
    public async Task Log_CallsWriteAsync_WithCorrectMessage()
    {
        var capture = new CaptureWriter();
        var logger = new HobiaryLogPipeLogger("MyCategory", capture);

        logger.Log(LogLevel.Information, new EventId(0), "hello world",
            null, (s, _) => s);

        await Task.Delay(5); // WriteAsync jest fire-and-forget

        Assert.Single(capture.Written);
        Assert.Equal("hello world", capture.Written[0].Message);
    }

    [Fact]
    public async Task Log_SetsSourceToCategoryName()
    {
        var capture = new CaptureWriter();
        var logger = new HobiaryLogPipeLogger("MyApp.Services.OrderService", capture);

        logger.Log(LogLevel.Warning, default, "msg", null, (s, _) => s);

        await Task.Delay(5);

        Assert.Equal("MyApp.Services.OrderService", capture.Written[0].Source);
    }

    [Fact]
    public async Task Log_WithException_AppendsExceptionToMessage()
    {
        var capture = new CaptureWriter();
        var logger = new HobiaryLogPipeLogger("Cat", capture);
        var ex = new InvalidOperationException("boom");

        logger.Log(LogLevel.Error, default, "error occurred", ex, (s, e) => s);

        await Task.Delay(5);

        Assert.Contains("boom", capture.Written[0].Message);
    }

    [Fact]
    public void IsEnabled_None_ReturnsFalse()
    {
        var logger = new HobiaryLogPipeLogger("Cat", new CaptureWriter());
        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Critical)]
    public void IsEnabled_OtherLevels_ReturnTrue(LogLevel level)
    {
        var logger = new HobiaryLogPipeLogger("Cat", new CaptureWriter());
        Assert.True(logger.IsEnabled(level));
    }

    [Fact]
    public async Task Log_LevelMappedCorrectly_ToLogPipeLevel()
    {
        var capture = new CaptureWriter();
        var logger = new HobiaryLogPipeLogger("Cat", capture);

        logger.Log(LogLevel.Critical, default, "fatal msg", null, (s, _) => s);

        await Task.Delay(5);

        Assert.Equal(LogPipeLevel.Fatal, capture.Written[0].Level);
    }

    // ── HobiaryLogPipeLoggerProvider ──────────────────────────────────────

    [Fact]
    public void CreateLogger_ReturnsDifferentInstancesPerCategory()
    {
        var provider = new HobiaryLogPipeLoggerProvider(new CaptureWriter());

        var l1 = provider.CreateLogger("Cat1");
        var l2 = provider.CreateLogger("Cat2");

        Assert.NotSame(l1, l2);
    }
}

// ── Test helper ───────────────────────────────────────────────────────────

internal sealed class CaptureWriter : ILogPipeWriter
{
    public List<LogMessage> Written { get; } = [];

    public Task WriteAsync(LogMessage message, CancellationToken ct = default)
    {
        Written.Add(message);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
