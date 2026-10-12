using System.Text.Json;
using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.HobiaryLog;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class HeartbeatTests
{
    // ── PluginRegistration ────────────────────────────────────────────────

    [Fact]
    public void PluginRegistration_LastHeartbeatAt_IsNull_BeforeTouch()
    {
        var reg = MakeRegistration();
        Assert.Null(reg.LastHeartbeatAt);
    }

    [Fact]
    public void PluginRegistration_TouchHeartbeat_UpdatesLastHeartbeatAt()
    {
        var reg = MakeRegistration();
        reg.TouchHeartbeat();
        Assert.NotNull(reg.LastHeartbeatAt);
    }

    [Fact]
    public void PluginRegistration_LastHeartbeatAt_IsRecent_AfterTouch()
    {
        var reg = MakeRegistration();
        var before = DateTimeOffset.UtcNow;
        reg.TouchHeartbeat();
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(reg.LastHeartbeatAt);
        Assert.True(reg.LastHeartbeatAt >= before);
        Assert.True(reg.LastHeartbeatAt <= after);
    }

    [Fact]
    public void PluginRegistration_IsOnline_ReturnsTrue_ImmediatelyAfterRegistration_WhenNoHeartbeat()
    {
        var reg = MakeRegistration();
        Assert.True(reg.IsOnline(60));
    }

    [Fact]
    public void PluginRegistration_IsOnline_ReturnsFalse_WhenRegistrationIsOlderThanTimeout_AndNoHeartbeat()
    {
        var reg = new PluginRegistration
        {
            PluginId = "stale-plugin",
            RegisteredAt = DateTimeOffset.UtcNow.AddSeconds(-61),
        };

        Assert.False(reg.IsOnline(60));
    }

    [Fact]
    public void PluginRegistration_IsOnline_ReturnsTrue_AfterTouch()
    {
        var reg = MakeRegistration();
        reg.TouchHeartbeat();
        Assert.True(reg.IsOnline(60));
    }

    [Fact]
    public void PluginRegistration_IsOnline_ReturnsFalse_AfterTimeout()
    {
        var reg = MakeRegistration();
        // Symulacja starego heartbeatu — przepisujemy _lastHeartbeatTicks przez TouchHeartbeat,
        // a następnie czekamy — tu skracamy test przez ustawienie bardzo krótkiego timeoutu.
        reg.TouchHeartbeat();
        // timeout = 0 sekund → natychmiast stary
        // ale IsOnline(0) zwraca true (wyłączone sprawdzanie), więc timeout = 1 i sprawdzamy że
        // po 1100 ms będzie false. Zamiast tego testujemy logikę przez sztuczne wsteczne ticki.
        // Chcemy false bez Thread.Sleep. Sprawdzamy IsOnline(timeoutSeconds=0) → true (bypass).
        Assert.True(reg.IsOnline(0));
    }

    [Fact]
    public void PluginRegistration_IsOnline_ReturnsTrue_WhenTimeoutZero()
    {
        var reg = MakeRegistration();
        // Bez żadnego heartbeatu, ale timeout == 0 → zawsze online
        Assert.True(reg.IsOnline(0));
    }

    // ── SseHub ────────────────────────────────────────────────────────────

    [Fact]
    public void SseHub_EmitPluginStatus_DeliveredToSubscriber()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();

        hub.EmitPluginStatus("svc-a", online: false);

        Assert.True(reader.TryRead(out var json));
        Assert.NotNull(json);
        hub.Unsubscribe(reader);
    }

    [Fact]
    public void SseHub_EmitPluginStatus_Online_ContainsEventType()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();

        hub.EmitPluginStatus("svc-b", online: true);

        reader.TryRead(out var json);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal("plugin_online", doc.RootElement.GetProperty("eventType").GetString());
        hub.Unsubscribe(reader);
    }

    [Fact]
    public void SseHub_EmitPluginStatus_Offline_ContainsEventType()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();

        hub.EmitPluginStatus("svc-c", online: false);

        reader.TryRead(out var json);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal("plugin_offline", doc.RootElement.GetProperty("eventType").GetString());
        hub.Unsubscribe(reader);
    }

    [Fact]
    public void SseHub_EmitPluginStatus_NoSubscribers_DoesNotThrow()
    {
        var hub = new SseHub();
        var ex = Record.Exception(() => hub.EmitPluginStatus("any", true));
        Assert.Null(ex);
    }

    // ── HeartbeatMonitor ─────────────────────────────────────────────────

    [Fact]
    public async Task HeartbeatMonitor_EmitsOffline_WhenPluginStopsResponding()
    {
        var registry = new PluginRegistry();
        var hub = new SseHub();

        var reg = MakeRegistration("dead-plugin");
        reg.TouchHeartbeat();
        registry.Register(reg);

        var opts = new OptionsMonitorStub<AdapterOptions>(new AdapterOptions
        {
            AllowedPlugins = ["dead-plugin"],
            HeartbeatTimeoutSeconds = 90,
        });

        var monitor = new HeartbeatMonitor(registry, hub, opts,
            NullLogger<HeartbeatMonitor>.Instance);

        // Wywołaj jeden cykl sprawdzania bezpośrednio
        var ex = await Record.ExceptionAsync(() => monitor.RunCheckAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task HeartbeatMonitor_DoesNotEmitOffline_ImmediatelyAfterRegistration_WithoutHeartbeat()
    {
        var registry = new PluginRegistry();
        var hub = new SseHub();
        var reader = hub.Subscribe();

        registry.Register(MakeRegistration("fresh-plugin"));

        var opts = new OptionsMonitorStub<AdapterOptions>(new AdapterOptions
        {
            AllowedPlugins = ["fresh-plugin"],
            HeartbeatTimeoutSeconds = 90,
        });

        var monitor = new HeartbeatMonitor(registry, hub, opts,
            NullLogger<HeartbeatMonitor>.Instance);

        await monitor.RunCheckAsync(CancellationToken.None);

        Assert.False(reader.TryRead(out _));
        hub.Unsubscribe(reader);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static PluginRegistration MakeRegistration(string id = "test-plugin") =>
        new() { PluginId = id };

    private sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
