using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.HobiaryLog;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter;

internal sealed class PluginRegistrationService
{
    private readonly ILogger<PluginRegistrationService> _logger;
    private readonly IOptionsMonitor<AdapterOptions> _options;
    private readonly PluginRegistry _registry;

    public PluginRegistrationService(
        ILogger<PluginRegistrationService> logger,
        IOptionsMonitor<AdapterOptions> options,
        PluginRegistry registry)
    {
        _logger = logger;
        _options = options;
        _registry = registry;
    }

    public PluginRegistrationResult Register(AnnounceMessage message)
    {
        var pluginId = message.PluginId;
        if (!PluginIdValidator.IsValid(pluginId))
        {
            _logger.LogWarning("Announce: invalid pluginId '{PluginId}'", pluginId);
            return PluginRegistrationResult.Rejected(pluginId);
        }

        var options = _options.CurrentValue;
        if (!options.AllowedPlugins.Contains(pluginId, StringComparer.Ordinal))
        {
            _logger.LogWarning("Announce: pluginId '{PluginId}' not in allowlist", pluginId);
            return PluginRegistrationResult.Rejected(pluginId);
        }

        var fieldMapping = message.FieldMapping;
        if (!PluginIdValidator.IsMappingKeyValid(fieldMapping.Level) ||
            !PluginIdValidator.IsMappingKeyValid(fieldMapping.Message) ||
            !PluginIdValidator.IsMappingKeyValid(fieldMapping.Source) ||
            !PluginIdValidator.IsMappingKeyValid(fieldMapping.Service))
        {
            _logger.LogWarning("Announce: invalid fieldMapping for '{PluginId}'", pluginId);
            return PluginRegistrationResult.Rejected(pluginId);
        }

        var logDirectory = Path.Combine(options.BaseLogDirectory, pluginId);
        if (!HobiaryLogClient.TryCreate(logDirectory, out var client, out var dllError))
        {
            _logger.LogError(
                "Nie można zarejestrować pluginu '{PluginId}': {Error}", pluginId, dllError);
            return PluginRegistrationResult.Rejected(pluginId);
        }

        var registration = new PluginRegistration
        {
            PluginId = pluginId,
            FieldMapping = fieldMapping,
            MessageFormat = message.MessageFormat,
            Client = client!,
            BatchWriter = new BatchWriter(client!),
            RegisteredAt = DateTimeOffset.UtcNow,
        };

        _registry.Register(registration);
        return PluginRegistrationResult.RegisteredPlugin(pluginId, logDirectory);
    }
}

internal sealed record PluginRegistrationResult(
    bool IsRegistered,
    string PluginId,
    string? LogDirectory)
{
    public static PluginRegistrationResult Rejected(string pluginId)
        => new(false, pluginId, null);

    public static PluginRegistrationResult RegisteredPlugin(string pluginId, string logDirectory)
        => new(true, pluginId, logDirectory);
}