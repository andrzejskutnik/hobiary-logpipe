using System.Text;
using System.Text.Json;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.HobiaryLog;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Orchestrates adapter lifecycle, plugin announcements, and shutdown flushing.
/// Message transport, data processing, plugin registration, and dead-letter policy are delegated to dedicated services.
/// </summary>
public sealed class AdapterService : IHostedService, IAsyncDisposable
{
    private readonly ILogger<AdapterService> _logger;
    private readonly IOptionsMonitor<AdapterOptions> _adapterOptions;
    private readonly IAdapterMessageTransport _messageTransport;
    private readonly LogMessageProcessor _logMessageProcessor;
    private readonly PluginRegistrationService _pluginRegistrationService;
    private readonly PluginRegistry _registry;
    private readonly DeadLetterHandler _deadLetterHandler;
    private IDisposable? _optionsChangeToken;

    /// <summary>Gets the connection state reported by the configured message transport.</summary>
    public ConnectionStatus ConnectionStatus => _messageTransport.ConnectionStatus;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal AdapterService(
        ILogger<AdapterService> logger,
        IOptionsMonitor<AdapterOptions> adapterOpts,
        IAdapterMessageTransport messageTransport,
        LogMessageProcessor logMessageProcessor,
        PluginRegistrationService pluginRegistrationService,
        DeadLetterHandler deadLetterHandler,
        PluginRegistry registry)
    {
        _logger = logger;
        _adapterOptions = adapterOpts;
        _messageTransport = messageTransport;
        _logMessageProcessor = logMessageProcessor;
        _pluginRegistrationService = pluginRegistrationService;
        _deadLetterHandler = deadLetterHandler;
        _registry = registry;
        _optionsChangeToken = adapterOpts.OnChange(OnAdapterOptionsChanged);
    }

    private void OnAdapterOptionsChanged(AdapterOptions newOpts, string? _)
    {
        _logMessageProcessor.UpdateOptions(newOpts);
        _logger.LogInformation(
            "AdapterOptions reloaded: AllowedPlugins=[{Plugins}] MaxDlqRetries={MaxRetries}",
            string.Join(", ", newOpts.AllowedPlugins),
            newOpts.MaxDlqRetries);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logMessageProcessor.ConfigurePayloadEncryption(_adapterOptions.CurrentValue.PayloadEncryption);
        await _messageTransport.StartAsync(
            HandleAnnounceAsync,
            _logMessageProcessor.ProcessAsync,
            HandleDlqAsync,
            RestoreRegisteredPluginQueuesAsync,
            cancellationToken);
        _logger.LogInformation("AdapterService started.");
    }

    private async Task RestoreRegisteredPluginQueuesAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in _registry.All)
            await _messageTransport.BindDataQueueAsync(registration.PluginId, _logMessageProcessor.ProcessAsync);
    }

    private async Task FlushAllBatchWritersAsync()
    {
        var tasks = _registry.All
            .Where(registration => registration.BatchWriter is not null)
            .Select(registration => registration.BatchWriter!.DisposeAsync().AsTask());
        await Task.WhenAll(tasks);
    }

    private Task<DeadLetterDecision> HandleDlqAsync(IncomingDeadLetterMessage message)
        => Task.FromResult(_deadLetterHandler.Handle(message));

    private async Task<MessageDisposition> HandleAnnounceAsync(IncomingAnnounceMessage incoming)
    {
        string? pluginId = null;
        try
        {
            var json = Encoding.UTF8.GetString(incoming.Body);
            var message = JsonSerializer.Deserialize<AnnounceMessage>(json, JsonOpts);
            if (message is null)
            {
                _logger.LogWarning("Announce: empty or unparseable message");
                return MessageDisposition.Reject;
            }

            pluginId = message.PluginId;
            var result = _pluginRegistrationService.Register(message);
            if (!result.IsRegistered)
                return MessageDisposition.Reject;

            await _messageTransport.BindDataQueueAsync(pluginId, _logMessageProcessor.ProcessAsync);
            _logger.LogInformation(
                "Plugin '{PluginId}' registered, logs → {LogDir}", pluginId, result.LogDirectory);
            return MessageDisposition.Acknowledge;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Announce: error processing plugin '{PluginId}'", pluginId ?? "?");
            return MessageDisposition.Reject;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AdapterService stopping — flushing batch writers...");
        await FlushAllBatchWritersAsync();
        _logger.LogInformation("AdapterService batch writers flushed. Stopping message transport.");
        await _messageTransport.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _optionsChangeToken?.Dispose();
        _logMessageProcessor.Dispose();
        _registry.Dispose();
        await _messageTransport.DisposeAsync();
    }
}
