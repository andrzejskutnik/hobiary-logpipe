using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class DeadLetterParserTests
{
    // ── ParseReason ──────────────────────────────────────────────────────

    [Fact]
    public void ParseReason_NullHeaders_ReturnsUnknown()
    {
        var result = DeadLetterParser.ParseReason(null);
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ParseReason_EmptyHeaders_ReturnsUnknown()
    {
        var result = DeadLetterParser.ParseReason(new Dictionary<string, object?>());
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ParseReason_XDeathAbsent_ReturnsUnknown()
    {
        var headers = new Dictionary<string, object?> { ["x-other"] = "value" };
        var result = DeadLetterParser.ParseReason(headers);
        Assert.Equal("unknown", result);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("expired")]
    [InlineData("maxlen")]
    [InlineData("delivery-limit")]
    public void ParseReason_WithKnownReason_ReturnsReason(string expectedReason)
    {
        var xDeath = new List<object>
        {
            new Dictionary<string, object?> { ["reason"] = expectedReason, ["exchange"] = "hobiary.data" }
        };
        var headers = new Dictionary<string, object?> { ["x-death"] = xDeath };

        var result = DeadLetterParser.ParseReason(headers);

        Assert.Equal(expectedReason, result);
    }

    [Fact]
    public void ParseReason_XDeathEmptyList_ReturnsUnknown()
    {
        var headers = new Dictionary<string, object?> { ["x-death"] = new List<object>() };
        var result = DeadLetterParser.ParseReason(headers);
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ParseReason_XDeathEntryWithoutReasonKey_ReturnsUnknown()
    {
        var xDeath = new List<object>
        {
            new Dictionary<string, object?> { ["exchange"] = "hobiary.data" }
        };
        var headers = new Dictionary<string, object?> { ["x-death"] = xDeath };
        var result = DeadLetterParser.ParseReason(headers);
        Assert.Equal("unknown", result);
    }

    // ── ParseSourceExchange ───────────────────────────────────────────────

    [Fact]
    public void ParseSourceExchange_NullHeaders_ReturnsUnknown()
    {
        var result = DeadLetterParser.ParseSourceExchange(null);
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ParseSourceExchange_WithExchange_ReturnsExchange()
    {
        var xDeath = new List<object>
        {
            new Dictionary<string, object?> { ["reason"] = "rejected", ["exchange"] = "hobiary.announce" }
        };
        var headers = new Dictionary<string, object?> { ["x-death"] = xDeath };

        var result = DeadLetterParser.ParseSourceExchange(headers);

        Assert.Equal("hobiary.announce", result);
    }

    [Fact]
    public void ParseSourceExchange_EntryWithoutExchangeKey_ReturnsUnknown()
    {
        var xDeath = new List<object>
        {
            new Dictionary<string, object?> { ["reason"] = "rejected" }
        };
        var headers = new Dictionary<string, object?> { ["x-death"] = xDeath };
        var result = DeadLetterParser.ParseSourceExchange(headers);
        Assert.Equal("unknown", result);
    }
}
