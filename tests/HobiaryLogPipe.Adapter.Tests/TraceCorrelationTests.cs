using HobiaryLogPipe.HobiaryLog;
using System.Text;
using System.Text.Json;
using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class TraceCorrelationHlogRecordTests
{
    // HlogRecord: CorrelationId is optional with default null

    [Fact]
    public void HlogRecord_CorrelationId_DefaultsToNull()
    {
        var rec = new HlogRecord(HlogLevel.Info, 1000L);
        Assert.Null(rec.CorrelationId);
    }

    [Fact]
    public void HlogRecord_CorrelationId_CanBeSet()
    {
        var corrId = new byte[16];
        corrId[0] = 0xAB;
        var rec = new HlogRecord(HlogLevel.Info, 1000L, CorrelationId: corrId);
        Assert.Equal(corrId, rec.CorrelationId);
    }
}

public sealed class TraceCorrelationLogQueryRecordTests
{
    [Fact]
    public void LogQueryRecord_TraceAndCorrelation_DefaultToNull()
    {
        var rec = new LogQueryRecord(HlogLevel.Info, 1000L, null, null, null);
        Assert.Null(rec.TraceId);
        Assert.Null(rec.CorrelationId);
    }

    [Fact]
    public void LogQueryRecord_TraceAndCorrelation_CanBeSet()
    {
        var rec = new LogQueryRecord(HlogLevel.Info, 1000L, null, null, null,
            TraceId: "4bf92f3577b34da6a3ce929d0e0e4736",
            CorrelationId: "aabbccddeeff00112233445566778899");

        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", rec.TraceId);
        Assert.Equal("aabbccddeeff00112233445566778899", rec.CorrelationId);
    }
}

public sealed class TraceCorrelationLogEntryTests
{
    private static readonly JsonSerializerOptions CamelCase =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void LogEntry_TraceAndCorrelation_DefaultToNull()
    {
        var e = new LogEntry("info", 1000L, null, null, null);
        Assert.Null(e.TraceId);
        Assert.Null(e.CorrelationId);
    }

    [Fact]
    public void LogEntry_WithTrace_SerializesCorrectly()
    {
        var e = new LogEntry("info", 1000L, null, null, null,
            TraceId: "4bf92f3577b34da6a3ce929d0e0e4736",
            CorrelationId: "aabbccddeeff00112233445566778899");

        var json = JsonSerializer.Serialize(e, CamelCase);
        var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", root.GetProperty("traceId").GetString());
        Assert.Equal("aabbccddeeff00112233445566778899", root.GetProperty("correlationId").GetString());
    }

    [Fact]
    public void LogEntry_WithoutTrace_SerializesNulls()
    {
        var e = new LogEntry("info", 1000L, null, null, null);
        var json = JsonSerializer.Serialize(e, CamelCase);
        var doc  = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("traceId").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("correlationId").ValueKind);
    }
}

public sealed class TraceCorrelationQueryRangeTests
{
    [Fact]
    public void QueryRange_WithDll_TraceIdRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_trace_{Guid.NewGuid()}");
        var ok  = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts      = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            var traceId = Convert.FromHexString("4bf92f3577b34da6a3ce929d0e0e4736");
            client.Append(new HlogRecord(HlogLevel.Info, ts, TraceId: traceId));

            var results = client.QueryRange(ts - 1L, ts + 1_000_000L, limit: 10);
            Assert.NotEmpty(results);
            Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", results[0].TraceId);
        }
    }

    [Fact]
    public void QueryRange_WithDll_CorrelationIdRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_corr_{Guid.NewGuid()}");
        var ok  = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            var corrId = Convert.FromHexString("aabbccddeeff00112233445566778899");
            client.Append(new HlogRecord(HlogLevel.Warn, ts, CorrelationId: corrId));

            var results = client.QueryRange(ts - 1L, ts + 1_000_000L, limit: 10);
            Assert.NotEmpty(results);
            Assert.Equal("aabbccddeeff00112233445566778899", results[0].CorrelationId);
        }
    }

    [Fact]
    public void QueryRange_WithDll_NoTraceId_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_notrace_{Guid.NewGuid()}");
        var ok  = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            client.Append(new HlogRecord(HlogLevel.Info, ts));

            var results = client.QueryRange(ts - 1L, ts + 1_000_000L, limit: 10);
            Assert.NotEmpty(results);
            Assert.Null(results[0].TraceId);
            Assert.Null(results[0].CorrelationId);
        }
    }
}

public sealed class TraceCorrelationLogMessageTests
{
    [Fact]
    public void LogMessage_TraceId_IsString()
    {
        var msg = new LogMessage { TraceId = "4bf92f3577b34da6a3ce929d0e0e4736" };
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", msg.TraceId);
    }

    [Fact]
    public void LogMessage_CorrelationId_IsString()
    {
        var msg = new LogMessage { CorrelationId = "aabbccddeeff00112233445566778899" };
        Assert.Equal("aabbccddeeff00112233445566778899", msg.CorrelationId);
    }

    [Fact]
    public void LogMessage_SerializesTraceAndCorrelationId()
    {
        var msg = new LogMessage
        {
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            CorrelationId = "aabbccddeeff00112233445566778899"
        };
        var json = System.Text.Json.JsonSerializer.Serialize(msg);
        Assert.Contains("\"traceId\"", json);
        Assert.Contains("\"correlationId\"", json);
        Assert.Contains("4bf92f3577b34da6a3ce929d0e0e4736", json);
    }
}

public sealed class FieldMappingConfigCorrelationTests
{
    [Fact]
    public void FieldMappingConfig_DefaultCorrelationId_IsCorrelationId()
    {
        var config = new FieldMappingConfig();
        Assert.Equal("correlationId", config.CorrelationId);
    }

    [Fact]
    public void FieldMappingConfig_CustomCorrelationId_CanBeSet()
    {
        var config = new FieldMappingConfig { CorrelationId = "cid" };
        Assert.Equal("cid", config.CorrelationId);
    }
}
