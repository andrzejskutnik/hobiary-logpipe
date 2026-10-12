using System.Threading.RateLimiting;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Rejestr rate limiterów per plugin. Tworzy TokenBucketRateLimiter na podstawie
/// konfiguracji PluginRateLimits. Bezpieczny wątkowo — ConcurrentDictionary.
/// </summary>
internal sealed class PluginRateLimiterRegistry : IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TokenBucketRateLimiter>
        _limiters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Przebudowuje registry na podstawie nowej konfiguracji.
    /// Usuwa limitery dla pluginów nieobecnych w nowych ustawieniach,
    /// tworzy lub aktualizuje dla obecnych.
    /// </summary>
    internal void Rebuild(Dictionary<string, int> pluginRateLimits)
    {
        // Usuń limitery dla pluginów, które zniknęły z konfiguracji
        foreach (var existing in _limiters.Keys.ToList())
        {
            if (!pluginRateLimits.ContainsKey(existing) &&
                _limiters.TryRemove(existing, out var removed))
                removed.Dispose();
        }

        // Dodaj / zaktualizuj
        foreach (var (pluginId, ratePerSec) in pluginRateLimits)
        {
            if (ratePerSec <= 0) continue;
            _limiters.AddOrUpdate(
                pluginId,
                _ => CreateLimiter(ratePerSec),
                (_, existing) =>
                {
                    // Jeśli zmienił się limit — stwórz nowy, stary usuń
                    existing.Dispose();
                    return CreateLimiter(ratePerSec);
                });
        }
    }

    /// <summary>
    /// Próbuje pobrać token dla danego pluginu.
    /// Zwraca <c>true</c> jeśli wiadomość może być przetworzona,
    /// <c>false</c> jeśli limit został przekroczony.
    /// Brak limitra dla pluginu = zawsze <c>true</c>.
    /// </summary>
    internal bool TryAcquire(string pluginId)
    {
        if (!_limiters.TryGetValue(pluginId, out var limiter))
            return true;

        using var lease = limiter.AttemptAcquire(permitCount: 1);
        return lease.IsAcquired;
    }

    private static TokenBucketRateLimiter CreateLimiter(int ratePerSec) =>
        new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = ratePerSec,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = ratePerSec,
            QueueProcessingOrder = QueueProcessingOrder.NewestFirst,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
            limiter.Dispose();
        _limiters.Clear();
    }
}
