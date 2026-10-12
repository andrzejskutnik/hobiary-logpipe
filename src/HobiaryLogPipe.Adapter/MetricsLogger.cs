namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Loguje snapshot metryk co <see cref="LogIntervalSeconds"/> sekund.
/// </summary>
public sealed class MetricsLogger : BackgroundService
{
    private readonly ILogger<MetricsLogger> _logger;
    private readonly PipeMetrics _metrics;

    public int LogIntervalSeconds { get; init; } = 60;

    public MetricsLogger(ILogger<MetricsLogger> logger, PipeMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(LogIntervalSeconds), stoppingToken);
                var s = _metrics.GetSnapshot();
                _logger.LogInformation(
                    "Metrics: received={Received} written={Written} rejected={Rejected} " +
                    "dlq={Dlq} reconnects={Reconnects}",
                    s.MessagesReceived, s.MessagesWritten, s.MessagesRejected,
                    s.DlqMessages, s.Reconnects);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
