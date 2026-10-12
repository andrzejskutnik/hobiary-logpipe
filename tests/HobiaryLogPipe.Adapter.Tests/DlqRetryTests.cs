using HobiaryLogPipe.Adapter;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class DeadLetterParserRetryCountTests
{
    [Fact]
    public void ParseRetryCount_Returns_Zero_When_Headers_Null()
    {
        Assert.Equal(0, DeadLetterParser.ParseRetryCount(null));
    }

    [Fact]
    public void ParseRetryCount_Returns_Zero_When_Header_Missing()
    {
        var headers = new Dictionary<string, object?> { ["other"] = 1 };
        Assert.Equal(0, DeadLetterParser.ParseRetryCount(headers));
    }

    [Fact]
    public void ParseRetryCount_Returns_Value_When_Int()
    {
        var headers = new Dictionary<string, object?> { ["x-hobiary-retry-count"] = 2 };
        Assert.Equal(2, DeadLetterParser.ParseRetryCount(headers));
    }

    [Fact]
    public void ParseRetryCount_Returns_Value_When_Long()
    {
        var headers = new Dictionary<string, object?> { ["x-hobiary-retry-count"] = 3L };
        Assert.Equal(3, DeadLetterParser.ParseRetryCount(headers));
    }

    [Fact]
    public void ParseRetryCount_Returns_Value_When_String()
    {
        var headers = new Dictionary<string, object?> { ["x-hobiary-retry-count"] = "5" };
        Assert.Equal(5, DeadLetterParser.ParseRetryCount(headers));
    }
}

public sealed class AdapterOptionsMaxDlqRetriesTests
{
    [Fact]
    public void MaxDlqRetries_Default_Is_3()
    {
        var opts = new AdapterOptions();
        Assert.Equal(3, opts.MaxDlqRetries);
    }

    [Fact]
    public void MaxDlqRetries_Custom()
    {
        var opts = new AdapterOptions { MaxDlqRetries = 5 };
        Assert.Equal(5, opts.MaxDlqRetries);
    }

    [Fact]
    public void MaxDlqRetries_Zero_Disables_Retry()
    {
        var opts = new AdapterOptions { MaxDlqRetries = 0 };
        Assert.Equal(0, opts.MaxDlqRetries);
    }
}

public sealed class DeadLetterHandlerTests
{
    [Fact]
    public void Handle_RetriesWhenRetryCountIsBelowLimit()
    {
        var metrics = new PipeMetrics();
        var handler = CreateHandler(maxRetries: 3, metrics);

        var decision = handler.Handle(CreateMessage(retryCount: 1, exchange: "hobiary.data"));

        Assert.True(decision.ShouldRetry);
        Assert.Equal("hobiary.data", decision.Exchange);
        Assert.Equal(2, decision.RetryCount);
        Assert.Equal(1, metrics.GetSnapshot().DlqMessages);
    }

    [Fact]
    public void Handle_ParksWhenRetryLimitIsReached()
    {
        var metrics = new PipeMetrics();
        var handler = CreateHandler(maxRetries: 2, metrics);

        var decision = handler.Handle(CreateMessage(retryCount: 2));

        Assert.False(decision.ShouldRetry);
        Assert.Null(decision.Exchange);
        Assert.Equal(1, metrics.GetSnapshot().DlqMessages);
    }

    [Fact]
    public void Handle_ParksImmediatelyWhenRetriesAreDisabled()
    {
        var handler = CreateHandler(maxRetries: 0, new PipeMetrics());

        var decision = handler.Handle(CreateMessage(retryCount: 0));

        Assert.False(decision.ShouldRetry);
        Assert.Null(decision.Exchange);
    }

    private static DeadLetterHandler CreateHandler(int maxRetries, PipeMetrics metrics)
        => new(
            NullLogger<DeadLetterHandler>.Instance,
            new OptionsMonitorStub(new AdapterOptions { MaxDlqRetries = maxRetries }),
            metrics);

    private static IncomingDeadLetterMessage CreateMessage(int retryCount, string exchange = "unknown")
    {
        var deathEntry = new Dictionary<string, object?>
        {
            ["reason"] = "rejected",
            ["exchange"] = exchange,
        };
        var headers = new Dictionary<string, object?>
        {
            ["x-death"] = new object[] { deathEntry },
            ["x-hobiary-retry-count"] = retryCount,
        };

        return new IncomingDeadLetterMessage("orders", [], headers, "application/json", null);
    }

    private sealed class OptionsMonitorStub(AdapterOptions value) : IOptionsMonitor<AdapterOptions>
    {
        public AdapterOptions CurrentValue => value;

        public AdapterOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AdapterOptions, string?> listener) => null;
    }
}
