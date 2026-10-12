using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class BatchAckTrackerTests
{
    [Fact]
    public void Record_ReturnsNoDecision_BeforeBatchThreshold()
    {
        var tracker = new BatchAckTracker(batchSize: 3, timeoutMs: 200);

        Assert.Null(tracker.Record(10, timestamp: 1_000));
        Assert.Null(tracker.Record(11, timestamp: 1_050));
    }

    [Fact]
    public void Record_ReturnsMultiAck_AtBatchThreshold()
    {
        var tracker = new BatchAckTracker(batchSize: 2, timeoutMs: 200);

        Assert.Null(tracker.Record(10, timestamp: 1_000));
        var decision = tracker.Record(11, timestamp: 1_010);

        Assert.Equal(new BatchAckDecision(11, Multiple: true), decision);
    }

    [Fact]
    public void Record_ReturnsMultiAck_WhenNextDeliveryObservesElapsedTimeout()
    {
        var tracker = new BatchAckTracker(batchSize: 10, timeoutMs: 200);

        Assert.Null(tracker.Record(10, timestamp: 1_000));
        var decision = tracker.Record(11, timestamp: 1_200);

        Assert.Equal(new BatchAckDecision(11, Multiple: true), decision);
    }

    [Fact]
    public void FlushPending_ReturnsMultiAck_WithoutAnotherDelivery()
    {
        var tracker = new BatchAckTracker(batchSize: 10, timeoutMs: 200);
        Assert.Null(tracker.Record(10, timestamp: 1_000));

        var decision = tracker.FlushPending();

        Assert.Equal(new BatchAckDecision(10, Multiple: true), decision);
        Assert.False(tracker.HasPending);
    }

    [Fact]
    public void Record_ReturnsSingleAck_WhenBatchingIsDisabled()
    {
        var tracker = new BatchAckTracker(batchSize: 0, timeoutMs: 200);

        Assert.Equal(new BatchAckDecision(10, Multiple: false), tracker.Record(10, timestamp: 1_000));
        Assert.Equal(new BatchAckDecision(11, Multiple: false), tracker.Record(11, timestamp: 1_010));
    }

    [Fact]
    public void Reset_ClearsPendingBatchState()
    {
        var tracker = new BatchAckTracker(batchSize: 2, timeoutMs: 200);
        Assert.Null(tracker.Record(10, timestamp: 1_000));

        tracker.Reset();

        Assert.Null(tracker.Record(11, timestamp: 1_010));
    }
}

public sealed class BatchAckTimeoutSchedulerTests
{
    [Fact]
    public async Task Schedule_InvokesFlushAfterDelayWithoutFurtherInput()
    {
        using var scheduler = new BatchAckTimeoutScheduler();
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        scheduler.Schedule(TimeSpan.FromMilliseconds(30), _ =>
        {
            flushed.TrySetResult();
            return Task.CompletedTask;
        });

        await flushed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Cancel_PreventsScheduledFlush()
    {
        using var scheduler = new BatchAckTimeoutScheduler();
        var flushCount = 0;
        scheduler.Schedule(TimeSpan.FromMilliseconds(80), _ =>
        {
            Interlocked.Increment(ref flushCount);
            return Task.CompletedTask;
        });

        scheduler.Cancel();
        await Task.Delay(150);

        Assert.Equal(0, Volatile.Read(ref flushCount));
    }
}