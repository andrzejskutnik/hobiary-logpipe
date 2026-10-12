using System.Threading.Channels;
using HobiaryLogPipe.HobiaryLog;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Buforuje HlogRecord i zapisuje je do HobiaryLogClient w partiach co <see cref="FlushInterval"/>.
/// Zmniejsza liczbę syscalli P/Invoke przy dużym natężeniu logów.
/// </summary>
internal sealed class BatchWriter : IAsyncDisposable
{
    internal static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromMilliseconds(100);

    private readonly HobiaryLogClient _client;
    private readonly Channel<HlogRecord> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _flushTask;
    private int _disposed;

    internal TimeSpan FlushInterval { get; }

    internal BatchWriter(HobiaryLogClient client, TimeSpan? flushInterval = null)
    {
        _client = client;
        FlushInterval = flushInterval ?? DefaultFlushInterval;
        _channel = Channel.CreateUnbounded<HlogRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
        _flushTask = FlushLoopAsync(_cts.Token);
    }

    /// <summary>Dodaje rekord do bufora (non-blocking).</summary>
    internal ValueTask EnqueueAsync(HlogRecord record, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(record, ct);

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, ct);
                DrainChannel();
            }
            catch (OperationCanceledException) { break; }
        }

        // Zapisz rekordy pozostałe w buforze po zamknięciu.
        DrainChannel();
    }

    private void DrainChannel()
    {
        while (_channel.Reader.TryRead(out var record))
            _client.Append(record);
    }

    /// <summary>
    /// Zamyka BatchWriter, zapisując wszystkie oczekujące rekordy i zatrzymując pętlę flushującą.
    /// </summary>
    /// <returns>Task reprezentujący asynchroniczną operację zamykania.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _channel.Writer.TryComplete();
        await _cts.CancelAsync();
        await _flushTask;
        _cts.Dispose();
    }
}
