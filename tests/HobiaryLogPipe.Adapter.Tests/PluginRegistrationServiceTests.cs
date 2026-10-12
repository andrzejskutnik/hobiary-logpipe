using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class PluginRegistrationServiceTests
{
    [Fact]
    public void Register_RejectsInvalidPluginId()
    {
        using var registry = new PluginRegistry();
        var service = CreateService(registry, allowedPlugins: ["orders"]);

        var result = service.Register(new AnnounceMessage { PluginId = ".." });

        Assert.False(result.IsRegistered);
        Assert.Empty(registry.All);
    }

    [Fact]
    public void Register_RejectsPluginOutsideAllowlist()
    {
        using var registry = new PluginRegistry();
        var service = CreateService(registry, allowedPlugins: ["orders"]);

        var result = service.Register(new AnnounceMessage { PluginId = "billing" });

        Assert.False(result.IsRegistered);
        Assert.Empty(registry.All);
    }

    [Fact]
    public void Register_RejectsInvalidFieldMapping()
    {
        using var registry = new PluginRegistry();
        var service = CreateService(registry, allowedPlugins: ["orders"]);

        var result = service.Register(new AnnounceMessage
        {
            PluginId = "orders",
            FieldMapping = new FieldMappingConfig { Level = "bad field" },
        });

        Assert.False(result.IsRegistered);
        Assert.Empty(registry.All);
    }

    private static PluginRegistrationService CreateService(
        PluginRegistry registry,
        string[] allowedPlugins)
    {
        var options = new AdapterOptions { AllowedPlugins = allowedPlugins };
        return new PluginRegistrationService(
            NullLogger<PluginRegistrationService>.Instance,
            new OptionsMonitorStub(options),
            registry);
    }

    private sealed class OptionsMonitorStub(AdapterOptions value) : IOptionsMonitor<AdapterOptions>
    {
        public AdapterOptions CurrentValue => value;

        public AdapterOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AdapterOptions, string?> listener) => null;
    }
}