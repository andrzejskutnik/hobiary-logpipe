using HobiaryLogPipe.HobiaryLog;
using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class PluginRegistryTests : IDisposable
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

    // ── Register / TryGet ─────────────────────────────────────────────────

    [Fact]
    public void Register_AddsPlugin()
    {
        _registry.Register(MakeReg("plugin-a"));

        Assert.True(_registry.Contains("plugin-a"));
    }

    [Fact]
    public void TryGet_ExistingPlugin_ReturnsTrueWithRegistration()
    {
        _registry.Register(MakeReg("plugin-b"));

        var found = _registry.TryGet("plugin-b", out var reg);

        Assert.True(found);
        Assert.NotNull(reg);
        Assert.Equal("plugin-b", reg!.PluginId);
    }

    [Fact]
    public void TryGet_UnknownPlugin_ReturnsFalse()
    {
        var found = _registry.TryGet("nonexistent", out var reg);

        Assert.False(found);
        Assert.Null(reg);
    }

    [Fact]
    public void Contains_ExistingPlugin_ReturnsTrue()
    {
        _registry.Register(MakeReg("plugin-c"));

        Assert.True(_registry.Contains("plugin-c"));
    }

    [Fact]
    public void Contains_UnknownPlugin_ReturnsFalse()
    {
        Assert.False(_registry.Contains("no-such-plugin"));
    }

    // ── Overwrite ─────────────────────────────────────────────────────────

    [Fact]
    public void Register_SamePluginId_OverwritesPrevious()
    {
        var first = MakeReg("plugin-d");
        var second = new PluginRegistration
        {
            PluginId = "plugin-d",
            FieldMapping = new FieldMappingConfig { Level = "severity" },
            MessageFormat = "json",
            Client = null!,
            RegisteredAt = DateTimeOffset.UtcNow,
        };

        _registry.Register(first);
        _registry.Register(second);

        _registry.TryGet("plugin-d", out var reg);
        Assert.Equal("severity", reg!.FieldMapping.Level);
    }

    [Fact]
    public void Register_SamePluginId_DisposesPreviousRegistration()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hobiary-logpipe-plugin-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var firstClient = new HobiaryLogClient(Path.Combine(tempRoot, "first"));
            var secondClient = new HobiaryLogClient(Path.Combine(tempRoot, "second"));

            var first = new PluginRegistration
            {
                PluginId = "plugin-dispose",
                FieldMapping = new FieldMappingConfig(),
                MessageFormat = "json",
                Client = firstClient,
                RegisteredAt = DateTimeOffset.UtcNow,
            };

            var second = new PluginRegistration
            {
                PluginId = "plugin-dispose",
                FieldMapping = new FieldMappingConfig(),
                MessageFormat = "json",
                Client = secondClient,
                RegisteredAt = DateTimeOffset.UtcNow,
            };

            _registry.Register(first);
            _registry.Register(second);

            Assert.Throws<ObjectDisposedException>(() => firstClient.Append(new HlogRecord(HlogLevel.Info, 1L)));
        }
        finally
        {
            _registry.Dispose();

            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── All ───────────────────────────────────────────────────────────────

    [Fact]
    public void All_ReturnsAllRegistrations()
    {
        _registry.Register(MakeReg("p1"));
        _registry.Register(MakeReg("p2"));
        _registry.Register(MakeReg("p3"));

        var all = _registry.All.Select(r => r.PluginId).OrderBy(x => x).ToList();

        Assert.Equal(new[] { "p1", "p2", "p3" }, all);
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_ClearsRegistry()
    {
        _registry.Register(MakeReg("p-dispose"));
        _registry.Dispose();

        Assert.False(_registry.Contains("p-dispose"));
        Assert.Empty(_registry.All);
    }
}
