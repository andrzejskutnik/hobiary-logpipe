using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class PipeMetricsTests
{
    [Fact]
    public void GetSnapshot_InitialState_AllZero()
    {
        var metrics = new PipeMetrics();
        var s = metrics.GetSnapshot();

        Assert.Equal(0, s.MessagesReceived);
        Assert.Equal(0, s.MessagesWritten);
        Assert.Equal(0, s.MessagesRejected);
        Assert.Equal(0, s.Reconnects);
        Assert.Equal(0, s.DlqMessages);
    }

    [Fact]
    public void IncrementReceived_IncreasesReceived()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementReceived();
        metrics.IncrementReceived();

        Assert.Equal(2, metrics.GetSnapshot().MessagesReceived);
    }

    [Fact]
    public void IncrementWritten_IncreasesWritten()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementWritten();

        Assert.Equal(1, metrics.GetSnapshot().MessagesWritten);
    }

    [Fact]
    public void IncrementRejected_IncreasesRejected()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementRejected();
        metrics.IncrementRejected();
        metrics.IncrementRejected();

        Assert.Equal(3, metrics.GetSnapshot().MessagesRejected);
    }

    [Fact]
    public void IncrementReconnects_IncreasesReconnects()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementReconnects();

        Assert.Equal(1, metrics.GetSnapshot().Reconnects);
    }

    [Fact]
    public void IncrementDlq_IncreasesDlqMessages()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementDlq();
        metrics.IncrementDlq();

        Assert.Equal(2, metrics.GetSnapshot().DlqMessages);
    }

    [Fact]
    public void GetSnapshot_IsImmutable()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementReceived();

        var s1 = metrics.GetSnapshot();
        metrics.IncrementReceived();
        var s2 = metrics.GetSnapshot();

        Assert.Equal(1, s1.MessagesReceived);
        Assert.Equal(2, s2.MessagesReceived);
    }

    [Fact]
    public void AllCounters_IndependentOfEachOther()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementReceived();
        metrics.IncrementWritten();
        metrics.IncrementRejected();
        metrics.IncrementReconnects();
        metrics.IncrementDlq();
        metrics.IncrementHeartbeats();

        var s = metrics.GetSnapshot();

        Assert.Equal(1, s.MessagesReceived);
        Assert.Equal(1, s.MessagesWritten);
        Assert.Equal(1, s.MessagesRejected);
        Assert.Equal(1, s.Reconnects);
        Assert.Equal(1, s.DlqMessages);
        Assert.Equal(1, s.HeartbeatsReceived);
    }

    [Fact]
    public void IncrementHeartbeats_IncreasesHeartbeatsReceived()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementHeartbeats();
        metrics.IncrementHeartbeats();
        metrics.IncrementHeartbeats();

        Assert.Equal(3, metrics.GetSnapshot().HeartbeatsReceived);
    }

    [Fact]
    public void HeartbeatsReceived_InitiallyZero()
    {
        var metrics = new PipeMetrics();

        Assert.Equal(0, metrics.GetSnapshot().HeartbeatsReceived);
    }
}
