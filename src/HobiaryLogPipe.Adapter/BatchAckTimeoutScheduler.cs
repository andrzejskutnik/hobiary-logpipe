namespace HobiaryLogPipe.Adapter;

internal sealed class BatchAckTimeoutScheduler : IDisposable
{
    private CancellationTokenSource? _scheduledFlush;
    private readonly Action<Exception>? _onError;

    public BatchAckTimeoutScheduler(Action<Exception>? onError = null)
    {
        _onError = onError;
    }

    public void Schedule(TimeSpan delay, Func<CancellationToken, Task> flush)
    {
        Cancel();
        var cancellation = new CancellationTokenSource();
        _scheduledFlush = cancellation;
        _ = RunAsync(cancellation, delay, flush);
    }

    public void Cancel()
    {
        var cancellation = Interlocked.Exchange(ref _scheduledFlush, null);
        cancellation?.Cancel();
    }

    private async Task RunAsync(
        CancellationTokenSource cancellation,
        TimeSpan delay,
        Func<CancellationToken, Task> flush)
    {
        try
        {
            await Task.Delay(delay, cancellation.Token);
            if (ReferenceEquals(Volatile.Read(ref _scheduledFlush), cancellation))
                await flush(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _onError?.Invoke(exception);
        }
        finally
        {
            Interlocked.CompareExchange(ref _scheduledFlush, null, cancellation);
            cancellation.Dispose();
        }
    }

    public void Dispose() => Cancel();
}