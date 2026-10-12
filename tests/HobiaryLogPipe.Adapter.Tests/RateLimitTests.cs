using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class RateLimitTests
{
    // ── AdapterOptions ────────────────────────────────────────────────────

    [Fact]
    public void PluginRateLimits_Default_IsEmpty()
    {
        var opts = new AdapterOptions();
        Assert.Empty(opts.PluginRateLimits);
    }

    [Fact]
    public void PluginRateLimits_CustomEntry()
    {
        var opts = new AdapterOptions
        {
            PluginRateLimits = new Dictionary<string, int>
            {
                ["orders-service"] = 100,
                ["auth-service"]   = 500,
            }
        };
        Assert.Equal(100, opts.PluginRateLimits["orders-service"]);
        Assert.Equal(500, opts.PluginRateLimits["auth-service"]);
    }

    // ── PluginRateLimiterRegistry ─────────────────────────────────────────

    [Fact]
    public void Registry_NoLimiter_AlwaysAllows()
    {
        using var registry = new PluginRateLimiterRegistry();
        // Brak konfiguracji → TryAcquire zawsze true
        Assert.True(registry.TryAcquire("any-plugin"));
        Assert.True(registry.TryAcquire("any-plugin"));
    }

    [Fact]
    public void Registry_WithLimit_AllowsUpToLimit()
    {
        using var registry = new PluginRateLimiterRegistry();
        registry.Rebuild(new Dictionary<string, int> { ["orders"] = 5 });

        int allowed = 0;
        for (int i = 0; i < 10; i++)
            if (registry.TryAcquire("orders")) allowed++;

        // Token bucket z limitem 5 — pierwsze 5 prób powinno przejść
        Assert.Equal(5, allowed);
    }

    [Fact]
    public void Registry_ZeroRate_Ignored_AlwaysAllows()
    {
        using var registry = new PluginRateLimiterRegistry();
        // Wartość 0 jest ignorowana — brak limitera = zawsze przepuszcza
        registry.Rebuild(new Dictionary<string, int> { ["bad-plugin"] = 0 });
        Assert.True(registry.TryAcquire("bad-plugin"));
    }

    [Fact]
    public void Registry_Rebuild_RemovesOldLimiters()
    {
        using var registry = new PluginRateLimiterRegistry();
        registry.Rebuild(new Dictionary<string, int> { ["plugin-a"] = 1 });

        // Wyczerpaj limit
        registry.TryAcquire("plugin-a");
        Assert.False(registry.TryAcquire("plugin-a"));

        // Usuń z konfiguracji — po Rebuild brak limitera = zawsze true
        registry.Rebuild(new Dictionary<string, int>());
        Assert.True(registry.TryAcquire("plugin-a"));
    }

    [Fact]
    public void Registry_CaseInsensitive_PluginId()
    {
        using var registry = new PluginRateLimiterRegistry();
        registry.Rebuild(new Dictionary<string, int> { ["Orders-Service"] = 3 });

        int allowed = 0;
        for (int i = 0; i < 6; i++)
            if (registry.TryAcquire("orders-service")) allowed++;

        Assert.Equal(3, allowed);
    }

    // ── PipeMetrics ───────────────────────────────────────────────────────

    [Fact]
    public void PipeMetrics_RateLimited_InitiallyZero()
    {
        var m = new PipeMetrics();
        Assert.Equal(0, m.GetSnapshot().MessagesRateLimited);
    }

    [Fact]
    public void PipeMetrics_IncrementRateLimited()
    {
        var m = new PipeMetrics();
        m.IncrementRateLimited();
        m.IncrementRateLimited();
        Assert.Equal(2, m.GetSnapshot().MessagesRateLimited);
    }
}
