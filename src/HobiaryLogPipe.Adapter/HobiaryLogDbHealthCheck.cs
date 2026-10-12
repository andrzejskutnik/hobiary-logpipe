using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Health check weryfikujący dostępność HobiaryLogDB dla wszystkich zarejestrowanych pluginów.
/// Healthy   — wszystkie pluginy mają sprawny klient (.hlog dostępny).
/// Degraded  — co najmniej jeden plugin nie ma aktywnego klienta (rejestracja w toku lub błąd otwarcia).
/// Unhealthy — brak zarejestrowanych pluginów lub wywołanie ActiveRecordCount rzuciło wyjątek.
/// </summary>
internal sealed class HobiaryLogDbHealthCheck : IHealthCheck
{
    private readonly PluginRegistry _registry;

    public HobiaryLogDbHealthCheck(PluginRegistry registry)
    {
        _registry = registry;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var all = _registry.All.ToList();
        var active = all.Where(r => !r.IsDeregistered).ToList();

        if (all.Count == 0)
            return Task.FromResult(HealthCheckResult.Degraded(
                "Brak zarejestrowanych pluginów."));

        if (active.Count == 0)
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Wszystkie pluginy ({all.Count}) wyrejestrowane."));

        var errors = new List<string>();
        foreach (var reg in active)
        {
            try
            {
                _ = reg.Client.ActiveRecordCount;
            }
            catch (Exception ex)
            {
                errors.Add($"{reg.PluginId}: {ex.Message}");
            }
        }

        if (errors.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy(
                $"HobiaryLogDB OK — {active.Count} aktywnych plugin(ów)."));

        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        for (var i = 0; i < errors.Count; i++)
            data[$"error_{i}"] = errors[i];

        return Task.FromResult(HealthCheckResult.Degraded(
            $"HobiaryLogDB: {errors.Count}/{active.Count} klient(ów) niedostępnych.",
            data: data));
    }
}
