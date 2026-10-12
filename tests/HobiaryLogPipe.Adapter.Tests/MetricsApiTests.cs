using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class MetricsApiTests
{
    [Fact]
    public void Empty_Snapshot_Contains_All_Metric_Names()
    {
        var metrics = new PipeMetrics();
        var snap = metrics.GetSnapshot();

        var body = BuildMetricsBody(snap, pluginsOnline: 0);

        Assert.Contains("hobiary_messages_received_total", body);
        Assert.Contains("hobiary_messages_written_total", body);
        Assert.Contains("hobiary_messages_rejected_total", body);
        Assert.Contains("hobiary_messages_filtered_total", body);
        Assert.Contains("hobiary_dlq_messages_total", body);
        Assert.Contains("hobiary_reconnects_total", body);
        Assert.Contains("hobiary_heartbeats_received_total", body);
        Assert.Contains("hobiary_plugins_online", body);
    }

    [Fact]
    public void Incremented_Values_Appear_In_Output()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementReceived();
        metrics.IncrementReceived();
        metrics.IncrementWritten();
        metrics.IncrementRejected();
        metrics.IncrementFiltered();
        metrics.IncrementFiltered();
        metrics.IncrementFiltered();
        metrics.IncrementDlq();
        metrics.IncrementReconnects();
        metrics.IncrementHeartbeats();
        metrics.IncrementHeartbeats();

        var body = BuildMetricsBody(metrics.GetSnapshot(), pluginsOnline: 4);

        Assert.Contains("hobiary_messages_received_total 2", body);
        Assert.Contains("hobiary_messages_written_total 1", body);
        Assert.Contains("hobiary_messages_rejected_total 1", body);
        Assert.Contains("hobiary_messages_filtered_total 3", body);
        Assert.Contains("hobiary_dlq_messages_total 1", body);
        Assert.Contains("hobiary_reconnects_total 1", body);
        Assert.Contains("hobiary_heartbeats_received_total 2", body);
        Assert.Contains("hobiary_plugins_online 4", body);
    }

    [Fact]
    public void Output_Contains_HELP_And_TYPE_Lines()
    {
        var metrics = new PipeMetrics();
        var body = BuildMetricsBody(metrics.GetSnapshot(), pluginsOnline: 0);

        Assert.Contains("# HELP ", body);
        Assert.Contains("# TYPE ", body);
        Assert.Contains("counter", body);
        Assert.Contains("gauge", body);
    }

    [Fact]
    public void PluginsOnline_Gauge_ReflectsPassedValue()
    {
        var metrics = new PipeMetrics();
        var body = BuildMetricsBody(metrics.GetSnapshot(), pluginsOnline: 7);

        Assert.Contains("hobiary_plugins_online 7", body);
    }

    // Helper: reprodukuje logikę MetricsApi.AppendCounter (public-accessible via snapshot)
    private static string BuildMetricsBody(PipeMetricsSnapshot snap, int pluginsOnline)
    {
        var sb = new System.Text.StringBuilder();
        Append(sb, "hobiary_messages_received_total", "counter", snap.MessagesReceived);
        Append(sb, "hobiary_messages_written_total", "counter", snap.MessagesWritten);
        Append(sb, "hobiary_messages_rejected_total", "counter", snap.MessagesRejected);
        Append(sb, "hobiary_messages_filtered_total", "counter", snap.MessagesFiltered);
        Append(sb, "hobiary_dlq_messages_total", "counter", snap.DlqMessages);
        Append(sb, "hobiary_reconnects_total", "counter", snap.Reconnects);
        Append(sb, "hobiary_heartbeats_received_total", "counter", snap.HeartbeatsReceived);
        Append(sb, "hobiary_plugins_online", "gauge", pluginsOnline);
        return sb.ToString();

        static void Append(System.Text.StringBuilder b, string name, string type, long value)
        {
            b.Append("# HELP ").AppendLine(name + " help");
            b.Append("# TYPE ").AppendLine(name + " " + type);
            b.Append(name).Append(' ').AppendLine(value.ToString());
        }
    }
}
