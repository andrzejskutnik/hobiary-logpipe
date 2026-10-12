using HobiaryLogPipe.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class HobiaryLogDbHealthCheckTests : IDisposable
{
    private readonly PluginRegistry _registry = new();
    private readonly HobiaryLogDbHealthCheck _check;

    public HobiaryLogDbHealthCheckTests()
    {
        _check = new HobiaryLogDbHealthCheck(_registry);
    }

    public void Dispose() => _registry.Dispose();

    private static PluginRegistration MakeReg(string pluginId) => new()
    {
        PluginId = pluginId,
        FieldMapping = new FieldMappingConfig(),
        MessageFormat = "json",
        Client = null!,
        RegisteredAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task EmptyRegistry_ReturnsDegraded()
    {
        var result = await _check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("Brak", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegistryWithNullClient_ReturnsDegraded_WithErrorData()
    {
        // Client = null! spowoduje NullReferenceException przy próbie odczytu ActiveRecordCount
        _registry.Register(MakeReg("broken-plugin"));

        var result = await _check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("1/1", result.Description);
        Assert.NotEmpty(result.Data);
        Assert.Contains(result.Data.Keys, k => k.StartsWith("error_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiplePlugins_OneWithNullClient_ReturnsDegraded()
    {
        _registry.Register(MakeReg("plugin-a"));
        _registry.Register(MakeReg("plugin-b"));

        var result = await _check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("2/2", result.Description);
    }

    [Fact]
    public async Task DegradedResult_DescriptionContainsPluginCount()
    {
        _registry.Register(MakeReg("svc-1"));

        var result = await _check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("1/1", result.Description);
    }

    [Fact]
    public async Task DegradedResult_Data_ContainsPluginIdInErrorMessage()
    {
        _registry.Register(MakeReg("orders-service"));

        var result = await _check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        var errorValues = result.Data.Values.OfType<string>().ToList();
        Assert.Contains(errorValues, v => v.Contains("orders-service", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationToken_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Nie rzuca — health check jest synchroniczny wewnętrznie
        var result = await _check.CheckHealthAsync(null!, cts.Token);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }
}
