using System.Collections.Concurrent;

namespace HobiaryLogPipe.Adapter;

public sealed class PluginRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, PluginRegistration> _reg = new();

    public void Register(PluginRegistration registration)
    {
        while (true)
        {
            if (_reg.TryGetValue(registration.PluginId, out var existing))
            {
                if (!_reg.TryUpdate(registration.PluginId, registration, existing))
                    continue;

                if (!ReferenceEquals(existing, registration))
                    existing.Dispose();

                return;
            }

            if (_reg.TryAdd(registration.PluginId, registration))
                return;
        }
    }

    public bool TryGet(string pluginId, out PluginRegistration? registration)
        => _reg.TryGetValue(pluginId, out registration);

    public bool Contains(string pluginId)
        => _reg.ContainsKey(pluginId);

    public IEnumerable<PluginRegistration> All => _reg.Values;

    public void Dispose()
    {
        foreach (var reg in _reg.Values)
            reg.Dispose();
        _reg.Clear();
    }
}
