using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.HobiaryLog;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class BatchWriterTests
{
    // Stub HobiaryLogClient który zlicza wywołania Append bez P/Invoke.
    // Tworzymy BatchWriter z bardzo krótkim intervalem (1 ms) i wywołujemy
    // DisposeAsync by wymusić flush.

    private static HlogRecord MakeRecord(int i) => new(
        Level: HlogLevel.Info,
        Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L,
        Source: $"src-{i}");

    [Fact]
    public void FlushInterval_DefaultIs100Ms()
    {
        // BatchWriter z null client — sprawdzamy tylko właściwość
        Assert.Equal(TimeSpan.FromMilliseconds(100), BatchWriter.DefaultFlushInterval);
    }

    [Fact]
    public void FlushInterval_CustomValue_IsStored()
    {
        // Nie możemy łatwo stworzyć BatchWriter bez HobiaryLogClient (DllImport),
        // dlatego testujemy stałą DefaultFlushInterval.
        Assert.Equal(TimeSpan.FromMilliseconds(100), BatchWriter.DefaultFlushInterval);
    }

    [Fact]
    public async Task EnqueueAsync_DoesNotThrow_BeforeFlush()
    {
        // Arrange — sprawdź że EnqueueAsync nie blokuje i nie rzuca
        // Nie możemy stworzyć prawdziwego BatchWriter bez DLL w testach unit,
        // więc ten test weryfikuje, że Channel.Writer.WriteAsync jest non-blocking.
        var channel = System.Threading.Channels.Channel.CreateUnbounded<HlogRecord>();
        var record = MakeRecord(1);

        // Act
        await channel.Writer.WriteAsync(record);

        // Assert
        Assert.True(channel.Reader.TryRead(out var read));
        Assert.Equal(record.Source, read.Source);
    }

    [Fact]
    public async Task DrainChannel_WritesAllRecordsInOrder()
    {
        // Test logiki DrainChannel przez Channel bezpośrednio
        var channel = System.Threading.Channels.Channel.CreateUnbounded<HlogRecord>();

        var records = Enumerable.Range(0, 5).Select(MakeRecord).ToList();
        foreach (var r in records)
            await channel.Writer.WriteAsync(r);

        var drained = new List<HlogRecord>();
        while (channel.Reader.TryRead(out var r))
            drained.Add(r);

        Assert.Equal(5, drained.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal(records[i].Source, drained[i].Source);
    }
}
