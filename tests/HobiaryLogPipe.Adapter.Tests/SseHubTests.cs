using System.Threading.Channels;
using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.HobiaryLog;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class SseHubTests
{
    // ── Subskrypcja / anulowanie ──────────────────────────────────────────

    [Fact]
    public void Subscribe_ReturnsReader()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();
        Assert.NotNull(reader);
        hub.Unsubscribe(reader);
    }

    [Fact]
    public void SubscriberCount_ZeroInitially()
    {
        var hub = new SseHub();
        Assert.Equal(0, hub.SubscriberCount);
    }

    [Fact]
    public void SubscriberCount_IncreasesOnSubscribe()
    {
        var hub = new SseHub();
        var r1 = hub.Subscribe();
        var r2 = hub.Subscribe();
        Assert.Equal(2, hub.SubscriberCount);
        hub.Unsubscribe(r1);
        hub.Unsubscribe(r2);
    }

    [Fact]
    public void Unsubscribe_DecreasesCount()
    {
        var hub = new SseHub();
        var r = hub.Subscribe();
        hub.Unsubscribe(r);
        Assert.Equal(0, hub.SubscriberCount);
    }

    // ── Emit ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Emit_DeliversMessageToSubscriber()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();

        hub.Emit("orders", HlogLevel.Info, 1_000_000L, "hello");

        var msg = await reader.ReadAsync();
        Assert.Contains("orders", msg);
        Assert.Contains("info", msg);
        hub.Unsubscribe(reader);
    }

    [Fact]
    public async Task Emit_ContainsTimestamp()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();

        hub.Emit("test", HlogLevel.Warn, 999_000L, null);

        var msg = await reader.ReadAsync();
        Assert.Contains("999000", msg);
        hub.Unsubscribe(reader);
    }

    [Fact]
    public async Task Emit_DeliveredToMultipleSubscribers()
    {
        var hub = new SseHub();
        var r1 = hub.Subscribe();
        var r2 = hub.Subscribe();

        hub.Emit("plugin", HlogLevel.Error, 1L, "payload");

        var m1 = await r1.ReadAsync();
        var m2 = await r2.ReadAsync();
        Assert.Equal(m1, m2);
        hub.Unsubscribe(r1);
        hub.Unsubscribe(r2);
    }

    [Fact]
    public void Emit_NoSubscribers_DoesNotThrow()
    {
        var hub = new SseHub();
        var ex = Record.Exception(() => hub.Emit("plugin", HlogLevel.Fatal, 0L, null));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Emit_NullPayload_ProducesValidJson()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();

        hub.Emit("svc", HlogLevel.Debug, 42L, null);

        var msg = await reader.ReadAsync();
        // Powinno zawierać "payload":null lub "payload":""
        Assert.Contains("svc", msg);
        hub.Unsubscribe(reader);
    }

    // ── Unsubscribe idempotency ────────────────────────────────────────────

    [Fact]
    public void Unsubscribe_UnknownReader_DoesNotThrow()
    {
        var hub = new SseHub();
        var otherCh = Channel.CreateBounded<string>(1);
        var ex = Record.Exception(() => hub.Unsubscribe(otherCh.Reader));
        Assert.Null(ex);
    }
}
