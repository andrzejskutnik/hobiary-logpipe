using System.Diagnostics;
using System.Text.Json;
using HobiaryLogPipe.Contracts;
using Microsoft.Extensions.Logging;

namespace HobiaryLogPipe.InputPlugin;

internal sealed class DirectLogPipeTransport : ILogPipeTransport
{
    private readonly ILogger _logger;
    private readonly InputPluginOptions _options;
    private readonly Func<InputPluginOptions, IDirectLogClient> _directClientFactory;
    private readonly object _appendGate = new();
    private IDirectLogClient? _directClient;

    public DirectLogPipeTransport(
        ILogger logger,
        InputPluginOptions options,
        Func<InputPluginOptions, IDirectLogClient> directClientFactory)
    {
        _logger = logger;
        _options = options;
        _directClientFactory = directClientFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        _directClient = _directClientFactory(_options);
        _logger.LogInformation(
            "Direct transport started for plugin '{PluginId}' in '{Directory}'.",
            _options.PluginId,
            Path.Combine(_options.BaseLogDirectory, _options.PluginId));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync();
    }

    public Task WriteAsync(LogMessage message, CancellationToken cancellationToken)
    {
        if (_directClient is null)
            throw new InvalidOperationException("LogPipeWriter not started. Call StartAsync first or register via AddHobiaryLogPipe.");

        var payload = SerializeMessage(_options, message);
        var traceId = ResolveTraceId(message);
        var correlationId = ResolveCorrelationId(message);
        var record = new DirectHlogRecord(
            DirectHlogLevelMapper.Map(message.Level),
            message.Timestamp.ToUnixTimeMilliseconds() * 1000L,
            message.Source,
            message.Service,
            payload,
            DirectIdParser.ParseTraceId(traceId),
            message.SpanId,
            DirectIdParser.ParseCorrelationId(correlationId));

        lock (_appendGate)
        {
            _directClient.Append(record);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_directClient is not null)
        {
            await _directClient.DisposeAsync();
            _directClient = null;
        }
    }

    internal static byte[] SerializeMessage(InputPluginOptions options, LogMessage msg)
    {
        var fm = options.FieldMapping;
        var traceId = ResolveTraceId(msg);
        var corrId = ResolveCorrelationId(msg);
        var dict = new Dictionary<string, object?>
        {
            [fm.Level] = msg.Level.ToString().ToLowerInvariant(),
            [fm.Message] = msg.Message,
            [fm.Source] = msg.Source,
            [fm.Service] = msg.Service,
            ["timestamp"] = msg.Timestamp.ToString("O"),
        };

        if (traceId is not null)
            dict[fm.TraceId] = traceId;

        if (corrId is not null)
            dict[fm.CorrelationId] = corrId;

        return JsonSerializer.SerializeToUtf8Bytes(dict);
    }

    internal static string? ResolveTraceId(LogMessage msg)
        => msg.TraceId
           ?? (Activity.Current is { } act && act.TraceId != default
               ? act.TraceId.ToHexString()
               : null);

    internal static string? ResolveCorrelationId(LogMessage msg)
        => msg.CorrelationId
           ?? Activity.Current?.GetBaggageItem("correlationId");

    private void ValidateOptions()
    {
        if (!PluginIdValidator.IsValid(_options.PluginId))
            throw new InvalidOperationException(
                $"PluginId '{_options.PluginId}' jest nieprawidłowy dla trybu Direct. Dozwolone: [a-zA-Z0-9_.-]{{1,64}}.");

        if (_options.PayloadEncryption.Enabled)
            throw new InvalidOperationException("PayloadEncryption nie jest obsługiwane przez tryb Direct.");

        if (_options.GzipEnabled)
            throw new InvalidOperationException("GzipEnabled nie jest obsługiwane przez tryb Direct.");

        if (string.IsNullOrWhiteSpace(_options.BaseLogDirectory))
            throw new InvalidOperationException("BaseLogDirectory jest wymagane dla trybu Direct.");
    }
}