using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Background service monitorujący heartbeaty pluginów.
/// Co <see cref="_checkIntervalMs"/> ms sprawdza wszystkie zarejestrowane pluginy
/// i emituje SSE event plugin_online / plugin_offline gdy zmienia się ich stan.
/// </summary>
internal sealed class HeartbeatMonitor : BackgroundService
{
    private readonly PluginRegistry _registry;
    private readonly SseHub _sseHub;
    private readonly IOptionsMonitor<AdapterOptions> _opts;
    private readonly ILogger<HeartbeatMonitor> _logger;

    private const int CheckIntervalMs = 10_000; // 10 s

    // pluginId → ostatni znany stan online
    private readonly Dictionary<string, bool> _lastStatus = new(StringComparer.Ordinal);

    public HeartbeatMonitor(
        PluginRegistry registry,
        SseHub sseHub,
        IOptionsMonitor<AdapterOptions> opts,
        ILogger<HeartbeatMonitor> logger)
    {
        _registry = registry;
        _sseHub = sseHub;
        _opts = opts;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CheckIntervalMs, stoppingToken).ConfigureAwait(false);
            await RunCheckAsync(stoppingToken);
        }
    }

    internal Task RunCheckAsync(CancellationToken ct)
    {
        var timeout = _opts.CurrentValue.HeartbeatTimeoutSeconds;
        if (timeout > 0)
        {
            foreach (var reg in _registry.All)
            {
            if (reg.IsDeregistered) continue;
                var isOnline = reg.IsOnline(timeout);
                if (!_lastStatus.TryGetValue(reg.PluginId, out var wasOnline))
                    wasOnline = true;

                if (isOnline == wasOnline) continue;

                _lastStatus[reg.PluginId] = isOnline;

                if (!isOnline)
                    _logger.LogWarning(
                        "HeartbeatMonitor: plugin '{PluginId}' nie odpowiada od {Timeout}s — offline.",
                        reg.PluginId, timeout);
                else
                    _logger.LogInformation(
                        "HeartbeatMonitor: plugin '{PluginId}' powrócił online.", reg.PluginId);

                _sseHub.EmitPluginStatus(reg.PluginId, isOnline);
            }

            var activeIds = _registry.All.Select(r => r.PluginId).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _lastStatus.Keys.Where(k => !activeIds.Contains(k)).ToList())
                _lastStatus.Remove(key);
        }

        return Task.CompletedTask;
    }
}
