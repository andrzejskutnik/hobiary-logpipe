using System.Text;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter;

internal sealed class DeadLetterHandler
{
    private readonly ILogger<DeadLetterHandler> _logger;
    private readonly IOptionsMonitor<AdapterOptions> _options;
    private readonly PipeMetrics _metrics;

    public DeadLetterHandler(
        ILogger<DeadLetterHandler> logger,
        IOptionsMonitor<AdapterOptions> options,
        PipeMetrics metrics)
    {
        _logger = logger;
        _options = options;
        _metrics = metrics;
    }

    public DeadLetterDecision Handle(IncomingDeadLetterMessage message)
    {
        var reason = DeadLetterParser.ParseReason(message.Headers);
        var sourceExchange = DeadLetterParser.ParseSourceExchange(message.Headers);
        var retryCount = DeadLetterParser.ParseRetryCount(message.Headers);
        var bodyPreview = Encoding.UTF8.GetString(
            message.Body.AsSpan(0, Math.Min(message.Body.Length, 256)));

        _metrics.IncrementDlq();

        var maxRetries = _options.CurrentValue.MaxDlqRetries;
        if (maxRetries > 0 && retryCount < maxRetries)
        {
            _logger.LogWarning(
                "DLQ retry {Attempt}/{Max}: routing_key='{RoutingKey}' exchange='{Exchange}' reason='{Reason}'",
                retryCount + 1, maxRetries, message.RoutingKey, sourceExchange, reason);
            return DeadLetterDecision.Retry(sourceExchange, retryCount + 1);
        }

        _logger.LogWarning(
            "DLQ park (no more retries): routing_key='{RoutingKey}' exchange='{Exchange}' reason='{Reason}' body={Body}",
            message.RoutingKey, sourceExchange, reason, bodyPreview);
        return DeadLetterDecision.Parked();
    }
}