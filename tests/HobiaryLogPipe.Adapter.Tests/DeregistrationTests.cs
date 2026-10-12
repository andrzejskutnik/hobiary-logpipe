using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class DeregistrationTests : IDisposable
{
    private readonly PluginRegistry _registry = new();

    public void Dispose() => _registry.Dispose();

    private static PluginRegistration MakeReg(string pluginId) => new()
    {
        PluginId = pluginId,
        FieldMapping = new FieldMappingConfig(),
        MessageFormat = "json",
        Client = null!,
        RegisteredAt = DateTimeOffset.UtcNow,
    };

    // ── PluginRegistration ────────────────────────────────────────────────

    [Fact]
    public void IsDeregistered_InitiallyFalse()
    {
        var reg = MakeReg("plugin-a");
        Assert.False(reg.IsDeregistered);
    }

    [Fact]
    public void Deregister_SetsIsDeregisteredTrue()
    {
        var reg = MakeReg("plugin-a");
        reg.Deregister();
        Assert.True(reg.IsDeregistered);
    }

    [Fact]
    public void Deregister_IsIdempotent()
    {
        var reg = MakeReg("plugin-a");
        reg.Deregister();
        reg.Deregister();
        Assert.True(reg.IsDeregistered);
    }

    // ── SseHub ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmitPluginDeregistered_DeliversEventToSubscriber()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();

        hub.EmitPluginDeregistered("orders");

        var msg = await reader.ReadAsync();
        Assert.Contains("orders", msg);
        Assert.Contains("plugin_deregistered", msg);
        hub.Unsubscribe(reader);
    }

    [Fact]
    public void EmitPluginDeregistered_NoSubscribers_DoesNotThrow()
    {
        var hub = new SseHub();
        var ex = Record.Exception(() => hub.EmitPluginDeregistered("orders"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task EmitPluginDeregistered_ContainsTimestampUs()
    {
        var hub = new SseHub();
        var reader = hub.Subscribe();

        hub.EmitPluginDeregistered("svc");

        var msg = await reader.ReadAsync();
        Assert.Contains("timestampUs", msg);
        hub.Unsubscribe(reader);
    }

    // ── HealthCheck: deregistered plugins ────────────────────────────────

    [Fact]
    public async Task HealthCheck_DeregisteredPlugin_NotCountedAsActive()
    {
        var check = new HobiaryLogDbHealthCheck(_registry);
        var reg = MakeReg("gone-plugin");
        reg.Deregister();
        _registry.Register(reg);

        // Wszystkie pluginy wyrejestrowane → Degraded z innym komunikatem niż "Brak"
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("wyrejestrowane", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthCheck_OneActiveOneDeregistered_OnlyActiveChecked()
    {
        // Tworzymy health check przez osobny registry, żeby nie zakłócać Dispose
        using var registry = new PluginRegistry();
        var check = new HobiaryLogDbHealthCheck(registry);

        var deregistered = MakeReg("gone");
        deregistered.Deregister();
        registry.Register(deregistered);

        // Zarejestrowany aktywny plugin z null klientem → powinien zwrócić Degraded
        // (bo null client wyrzuci wyjątek przy ActiveRecordCount)
        registry.Register(MakeReg("active-broken"));

        var result = await check.CheckHealthAsync(null!);

        // Powinien sprawdzić tylko "active-broken" → 1/1 niedostępnych
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("1/1", result.Description);
    }

    // ── HeartbeatMonitor ─────────────────────────────────────────────────

    [Fact]
    public async Task HeartbeatMonitor_SkipsDeregisteredPlugins()
    {
        // Deregistered plugin nie powinien wygenerować zdarzenia SSE offline
        var hub = new SseHub();
        var reader = hub.Subscribe();

        var reg = MakeReg("dead-plugin");
        reg.TouchHeartbeat();
        reg.Deregister();
        _registry.Register(reg);

        var optsMonitor = new StubOptionsMonitor(new AdapterOptions { HeartbeatTimeoutSeconds = 1 });
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HeartbeatMonitor>.Instance;
        var monitor = new HeartbeatMonitor(_registry, hub, optsMonitor, logger);

        // Poczekaj, żeby timeout (1s) minął
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        await monitor.RunCheckAsync(CancellationToken.None);

        // Brak wiadomości SSE — kanał powinien być pusty
        Assert.False(reader.TryRead(out _));
        hub.Unsubscribe(reader);
    }

    private sealed class StubOptionsMonitor(AdapterOptions value)
        : Microsoft.Extensions.Options.IOptionsMonitor<AdapterOptions>
    {
        public AdapterOptions CurrentValue => value;
        public AdapterOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AdapterOptions, string?> listener) => null;
    }
}
