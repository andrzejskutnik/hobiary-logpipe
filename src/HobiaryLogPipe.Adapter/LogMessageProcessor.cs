using System.Text;
using System.Text.Json;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.HobiaryLog;

namespace HobiaryLogPipe.Adapter;

internal sealed class LogMessageProcessor : IDisposable
{
    private readonly ILogger<LogMessageProcessor> _logger;
    private readonly PluginRegistry _registry;
    private readonly PipeMetrics _metrics;
    private readonly SseHub _sseHub;
    private readonly PluginRateLimiterRegistry _rateLimiters = new();
    private volatile Dictionary<string, HlogLevel> _pluginMinLevels;
    private volatile Dictionary<string, string[]> _requiredFields;
    private Dictionary<string, byte[]>? _decryptionKeyMap;

    public LogMessageProcessor(
        ILogger<LogMessageProcessor> logger,
        PluginRegistry registry,
        PipeMetrics metrics,
        SseHub sseHub,
        AdapterOptions options)
    {
        _logger = logger;
        _registry = registry;
        _metrics = metrics;
        _sseHub = sseHub;
        _pluginMinLevels = BuildPluginMinLevels(options.PluginMinLevels);
        _requiredFields = options.PluginRequiredFields;
        _rateLimiters.Rebuild(options.PluginRateLimits);
    }

    public void UpdateOptions(AdapterOptions options)
    {
        _pluginMinLevels = BuildPluginMinLevels(options.PluginMinLevels);
        _rateLimiters.Rebuild(options.PluginRateLimits);
        _requiredFields = options.PluginRequiredFields;
    }

    public void ConfigurePayloadEncryption(PayloadEncryptionOptions options)
    {
        _decryptionKeyMap = options.Enabled
            ? PayloadEncryptor.BuildKeyMap(options)
            : null;
    }

    public async Task<MessageDisposition> ProcessAsync(IncomingLogMessage message)
    {
        var pluginId = message.RoutingKey;
        _metrics.IncrementReceived();

        if (!_registry.TryGet(pluginId, out var registration) || registration is null)
        {
            _logger.LogWarning("Data: unknown plugin '{RoutingKey}', dead-lettering", pluginId);
            _metrics.IncrementRejected();
            return MessageDisposition.Reject;
        }

        try
        {
            var rawBody = message.Body;
            var isEncrypted = message.Headers?.ContainsKey(PayloadEncryptor.HeaderName) == true;
            if (isEncrypted && _decryptionKeyMap is null)
            {
                _logger.LogError(
                    "Data: zaszyfrowana wiadomość od '{PluginId}', ale PayloadEncryption jest wyłączone w adapterze.",
                    pluginId);
                _metrics.IncrementRejected();
                return MessageDisposition.Reject;
            }
            if (!isEncrypted && _decryptionKeyMap is not null)
            {
                _logger.LogError(
                    "Data: niezaszyfrowana wiadomość od '{PluginId}', ale PayloadEncryption jest włączone w adapterze.",
                    pluginId);
                _metrics.IncrementRejected();
                return MessageDisposition.Reject;
            }
            if (isEncrypted)
            {
                var keyId = PayloadEncryptor.ReadKeyId(message.Headers);
                if (!_decryptionKeyMap!.TryGetValue(keyId, out var decryptKey))
                {
                    _logger.LogError(
                        "Data: nieznany KeyId '{KeyId}' w wiadomości od '{PluginId}'. Dodaj klucz do PayloadEncryption.Keys.",
                        keyId, pluginId);
                    _metrics.IncrementRejected();
                    return MessageDisposition.Reject;
                }
                rawBody = PayloadEncryptor.Decrypt(rawBody, decryptKey);
            }

            if (message.Headers?.TryGetValue("x-hobiary-encoding", out var encodingHeader) == true
                && encodingHeader?.ToString() == "gzip")
            {
                using var input = new MemoryStream(rawBody);
                using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var output = new MemoryStream();
                gzip.CopyTo(output);
                rawBody = output.ToArray();
            }

            var payload = rawBody.Length <= HobiaryLogClient.MaxPayloadBytes
                ? rawBody
                : rawBody[..(int)HobiaryLogClient.MaxPayloadBytes];

            if (rawBody.Length < 64)
            {
                try
                {
                    using var heartbeatDocument = JsonDocument.Parse(rawBody);
                    var heartbeatRoot = heartbeatDocument.RootElement;
                    if (heartbeatRoot.TryGetProperty("type", out var typeProperty))
                    {
                        var messageType = typeProperty.GetString();
                        if (messageType == "heartbeat")
                        {
                            registration.TouchHeartbeat();
                            _metrics.IncrementHeartbeats();
                            return MessageDisposition.Acknowledge;
                        }
                        if (messageType == "deregister")
                        {
                            registration.Deregister();
                            _logger.LogInformation("Plugin '{PluginId}' wyrejestrował się.", pluginId);
                            _sseHub.EmitPluginDeregistered(pluginId);
                            return MessageDisposition.Acknowledge;
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }

            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var record = FieldMapper.Map(root, registration.FieldMapping, payload);

            var missingField = SchemaValidator.FindMissingField(root, pluginId, _requiredFields);
            if (missingField is not null)
            {
                _logger.LogWarning(
                    "Schema: wiadomość od '{PluginId}' nie zawiera wymaganego pola '{Field}'",
                    pluginId, missingField);
                _metrics.IncrementSchemaInvalid();
                _metrics.IncrementRejected();
                return MessageDisposition.Reject;
            }

            if (_pluginMinLevels.TryGetValue(pluginId, out var minLevel) && record.Level < minLevel)
            {
                _metrics.IncrementFiltered();
                return MessageDisposition.Acknowledge;
            }

            if (!_rateLimiters.TryAcquire(pluginId))
            {
                _metrics.IncrementRateLimited();
                return MessageDisposition.Acknowledge;
            }

            await registration.BatchWriter!.EnqueueAsync(record);
            _metrics.IncrementWritten();
            _sseHub.Emit(pluginId, record.Level, record.Timestamp,
                record.Payload is { Length: > 0 } ? Encoding.UTF8.GetString(record.Payload) : null);
            return MessageDisposition.Acknowledge;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Data: error processing message for '{PluginId}'", pluginId);
            _metrics.IncrementRejected();
            return MessageDisposition.Reject;
        }
    }

    private static Dictionary<string, HlogLevel> BuildPluginMinLevels(Dictionary<string, string> raw)
    {
        var result = new Dictionary<string, HlogLevel>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, levelString) in raw)
        {
            if (Enum.TryParse<HlogLevel>(levelString, ignoreCase: true, out var level))
                result[pluginId] = level;
        }
        return result;
    }

    public void Dispose() => _rateLimiters.Dispose();
}