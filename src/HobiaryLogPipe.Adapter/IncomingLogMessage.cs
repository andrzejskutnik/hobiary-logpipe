namespace HobiaryLogPipe.Adapter;

internal sealed record IncomingLogMessage(
    string RoutingKey,
    byte[] Body,
    IDictionary<string, object?>? Headers);

internal sealed record IncomingAnnounceMessage(byte[] Body);

internal sealed record IncomingDeadLetterMessage(
    string RoutingKey,
    byte[] Body,
    IDictionary<string, object?>? Headers,
    string? ContentType,
    string? ContentEncoding);

internal enum MessageDisposition
{
    Acknowledge,
    Reject,
}

internal sealed record DeadLetterDecision(bool ShouldRetry, string? Exchange, int RetryCount)
{
    public static DeadLetterDecision Parked() => new(false, null, 0);

    public static DeadLetterDecision Retry(string exchange, int retryCount)
        => new(true, exchange, retryCount);
}