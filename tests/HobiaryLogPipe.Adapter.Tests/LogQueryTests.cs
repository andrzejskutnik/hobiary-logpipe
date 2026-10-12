using HobiaryLogPipe.HobiaryLog;
using System.Text;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class LogQueryRecordTests
{
    [Fact]
    public void LogQueryRecord_StoresAllFields()
    {
        var payload = Encoding.UTF8.GetBytes("hello");
        var rec = new LogQueryRecord(HlogLevel.Error, 999_000L, "orders", "api-gw", payload);

        Assert.Equal(HlogLevel.Error, rec.Level);
        Assert.Equal(999_000L, rec.TimestampUs);
        Assert.Equal("orders", rec.Source);
        Assert.Equal("api-gw", rec.Service);
        Assert.Equal(payload, rec.Payload);
    }

    [Fact]
    public void LogQueryRecord_NullableFieldsAllowNull()
    {
        var rec = new LogQueryRecord(HlogLevel.Info, 0L, null, null, null);
        Assert.Null(rec.Source);
        Assert.Null(rec.Service);
        Assert.Null(rec.Payload);
    }
}

public sealed class LogQueryClientTests
{
    // QueryRange wymaga hobiarylogdb.dll — kondycjonalne jak HobiaryLogClientTests

    [Fact]
    public void QueryRange_WithDll_ReturnsEmptyForEmptyLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_qr_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return; // DLL niedostępna

        using (client)
        {
            var results = client.QueryRange(0L, long.MaxValue, limit: 100);
            Assert.NotNull(results);
            Assert.Empty(results);
        }
    }

    [Fact]
    public void QueryRange_WithDll_ReturnsWrittenRecord()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_qr_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            var payload = Encoding.UTF8.GetBytes("test-payload");
            client.Append(new HlogRecord(HlogLevel.Warn, ts, "src1", "svc1", payload));

            var results = client.QueryRange(ts - 1_000_000L, ts + 1_000_000L, limit: 10);
            Assert.NotEmpty(results);
            Assert.Equal(HlogLevel.Warn, results[0].Level);
        }
    }

    [Fact]
    public void QueryRange_WithDll_LimitIsRespected()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_qr_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            for (int i = 0; i < 10; i++)
                client.Append(new HlogRecord(HlogLevel.Info, ts + i * 1000L, null, null, null));

            var results = client.QueryRange(ts - 1L, ts + 100_000L, limit: 3);
            Assert.True(results.Count <= 3);
        }
    }

    [Fact]
    public void QueryRange_WithDll_MinLevelFilters()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_qr_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            client.Append(new HlogRecord(HlogLevel.Debug, ts,           null, null, null));
            client.Append(new HlogRecord(HlogLevel.Error, ts + 1000L,   null, null, null));

            var results = client.QueryRange(ts - 1L, ts + 2_000L, minLevel: HlogLevel.Error, limit: 10);
            Assert.All(results, r => Assert.True(r.Level >= HlogLevel.Error));
        }
    }
}

public sealed class LogLevelParsingTests
{
    [Theory]
    [InlineData("trace",  HlogLevel.Trace)]
    [InlineData("Debug",  HlogLevel.Debug)]
    [InlineData("INFO",   HlogLevel.Info)]
    [InlineData("Warn",   HlogLevel.Warn)]
    [InlineData("error",  HlogLevel.Error)]
    [InlineData("FATAL",  HlogLevel.Fatal)]
    public void ParseLevel_CaseInsensitive(string input, HlogLevel expected)
    {
        var ok = Enum.TryParse<HlogLevel>(input, ignoreCase: true, out var result);
        Assert.True(ok);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("verbose")]
    public void ParseLevel_InvalidReturns_False(string input)
    {
        var ok = Enum.TryParse<HlogLevel>(input, ignoreCase: true, out _);
        Assert.False(ok);
    }
}

public sealed class LogQueryLimitClampTests
{
    [Theory]
    [InlineData(null,  200)]
    [InlineData(50,     50)]
    [InlineData(0,       1)]
    [InlineData(-5,      1)]
    [InlineData(2000, 1000)]
    [InlineData(1000, 1000)]
    public void Limit_ClampsCorrectly(int? input, int expected)
    {
        var result = Math.Clamp(input ?? 200, 1, 1000);
        Assert.Equal(expected, result);
    }
}

public sealed class CountByLevelTests
{
    [Fact]
    public void CountByLevel_WithDll_ReturnsZeroForEmptyLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_cbl_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var counts = client.CountByLevel(0L, long.MaxValue);
            Assert.Equal(6, counts.Length);
            Assert.All(counts, c => Assert.Equal(0L, c));
        }
    }

    [Fact]
    public void CountByLevel_WithDll_CountsCorrectly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_cbl_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            client.Append(new HlogRecord(HlogLevel.Info,  ts,          null, null, null));
            client.Append(new HlogRecord(HlogLevel.Info,  ts + 1000L,  null, null, null));
            client.Append(new HlogRecord(HlogLevel.Error, ts + 2000L,  null, null, null));
            client.Append(new HlogRecord(HlogLevel.Fatal, ts + 3000L,  null, null, null));

            var counts = client.CountByLevel(ts - 1L, ts + 10_000L);

            Assert.Equal(2L, counts[(int)HlogLevel.Info]);
            Assert.Equal(1L, counts[(int)HlogLevel.Error]);
            Assert.Equal(1L, counts[(int)HlogLevel.Fatal]);
            Assert.Equal(0L, counts[(int)HlogLevel.Warn]);
        }
    }

    [Fact]
    public void CountByLevel_WithDll_RangeFiltersCorrectly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_cbl_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using (client)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            client.Append(new HlogRecord(HlogLevel.Debug, ts,           null, null, null));
            client.Append(new HlogRecord(HlogLevel.Warn,  ts + 10000L,  null, null, null));

            // zakres obejmuje tylko pierwszy rekord
            var counts = client.CountByLevel(ts - 1L, ts + 5000L);
            Assert.Equal(1L, counts[(int)HlogLevel.Debug]);
            Assert.Equal(0L, counts[(int)HlogLevel.Warn]);
        }
    }
}

public sealed class LogStatsRecordTests
{
    [Fact]
    public void LevelCounts_StoresAllLevels()
    {
        var lc = new LevelCounts(1, 2, 3, 4, 5, 6);
        Assert.Equal(1, lc.Trace);
        Assert.Equal(2, lc.Debug);
        Assert.Equal(3, lc.Info);
        Assert.Equal(4, lc.Warn);
        Assert.Equal(5, lc.Error);
        Assert.Equal(6, lc.Fatal);
    }

    [Fact]
    public void LogStats_TotalSumsAllLevels()
    {
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to   = DateTimeOffset.UtcNow;
        var lc   = new LevelCounts(1, 2, 3, 4, 5, 6);
        var stats = new LogStats("orders", from, to, 21, lc);

        Assert.Equal("orders", stats.PluginId);
        Assert.Equal(21L, stats.Total);
        Assert.Same(lc, stats.ByLevel);
    }
}
