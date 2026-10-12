using HobiaryLogPipe.HobiaryLog;
using System.Text;
using System.Text.Json;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class NdjsonExportTests
{
    private static readonly JsonSerializerOptions CamelCase =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // --- LogEntry serialization ---

    [Fact]
    public void LogEntry_SerializesWithCamelCaseKeys()
    {
        var entry = new LogEntry("warn", 1_000_000L, "svc", "api", "msg");
        var json  = JsonSerializer.Serialize(entry, CamelCase);
        var doc   = JsonDocument.Parse(json);
        var root  = doc.RootElement;

        Assert.True(root.TryGetProperty("level",       out _), "brak 'level'");
        Assert.True(root.TryGetProperty("timestampUs", out _), "brak 'timestampUs'");
        Assert.True(root.TryGetProperty("source",      out _), "brak 'source'");
        Assert.True(root.TryGetProperty("service",     out _), "brak 'service'");
        Assert.True(root.TryGetProperty("payload",     out _), "brak 'payload'");
    }

    [Fact]
    public void LogEntry_NullFields_SerializeAsNull()
    {
        var entry = new LogEntry("info", 0L, null, null, null);
        var json  = JsonSerializer.Serialize(entry, CamelCase);
        var doc   = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("source").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("service").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("payload").ValueKind);
    }

    [Fact]
    public void LogEntry_RoundTrips_Values()
    {
        var entry = new LogEntry("error", 987_654_321L, "orders", "api-gw", "timeout");
        var json  = JsonSerializer.Serialize(entry, CamelCase);
        var doc   = JsonDocument.Parse(json);
        var root  = doc.RootElement;

        Assert.Equal("error",         root.GetProperty("level").GetString());
        Assert.Equal(987_654_321L,    root.GetProperty("timestampUs").GetInt64());
        Assert.Equal("orders",        root.GetProperty("source").GetString());
        Assert.Equal("api-gw",        root.GetProperty("service").GetString());
        Assert.Equal("timeout",       root.GetProperty("payload").GetString());
    }

    // --- NDJSON multi-line format ---

    [Fact]
    public void Ndjson_MultipleRecords_EachLineIsValidJson()
    {
        var records = new[]
        {
            new LogEntry("info",  1000L, "a", "svc", "msg1"),
            new LogEntry("error", 2000L, "b", "svc", "msg2"),
            new LogEntry("warn",  3000L, null, null, null),
        };

        var sb = new StringBuilder();
        foreach (var r in records)
            sb.AppendLine(JsonSerializer.Serialize(r, CamelCase));

        var lines = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);

        foreach (var line in lines)
        {
            // każda linia musi być valid JSON
            var ex = Record.Exception(() => JsonDocument.Parse(line));
            Assert.Null(ex);
        }
    }

    [Fact]
    public void Ndjson_LinesAreIndependent_NewlineTerminated()
    {
        var entry = new LogEntry("debug", 42L, null, null, null);
        var line  = JsonSerializer.Serialize(entry, CamelCase) + "\n";

        Assert.EndsWith("\n", line);
        Assert.DoesNotContain("\n", line.TrimEnd('\n')); // brak newline wewnątrz
    }

    // --- QueryRange bez limitu (warunkowe na DLL) ---

    [Fact]
    public void QueryRange_IntMaxLimit_ReturnsAllRecords()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_export_{Guid.NewGuid()}");
        var ok  = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            for (int i = 0; i < 5; i++)
                client.Append(new HlogRecord(HlogLevel.Info, ts + i * 1000L, null, null, null));

            var all = client.QueryRange(ts - 1L, ts + 10_000L, limit: int.MaxValue);
            Assert.Equal(5, all.Count);
        }
    }

    [Fact]
    public void QueryRange_IntMaxLimit_WithLevelFilter_FiltersCorrectly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_export_{Guid.NewGuid()}");
        var ok  = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            client.Append(new HlogRecord(HlogLevel.Debug, ts,           null, null, null));
            client.Append(new HlogRecord(HlogLevel.Error, ts + 1000L,  null, null, null));
            client.Append(new HlogRecord(HlogLevel.Fatal, ts + 2000L,  null, null, null));

            var errors = client.QueryRange(ts - 1L, ts + 5000L, HlogLevel.Error, int.MaxValue);
            Assert.Equal(2, errors.Count);
            Assert.All(errors, r => Assert.True(r.Level >= HlogLevel.Error));
        }
    }
}
