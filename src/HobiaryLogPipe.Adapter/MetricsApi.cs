using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text;

namespace HobiaryLogPipe.Adapter;

internal static class MetricsApi
{
    internal static IEndpointRouteBuilder MapMetricsApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/metrics", (PipeMetrics metrics, PluginRegistry registry,
            IOptionsMonitor<AdapterOptions> opts) =>
        {
            var snap = metrics.GetSnapshot();
            var sb = new StringBuilder(512);

            AppendCounter(sb, "hobiary_messages_received_total",
                "Total messages received from RabbitMQ.", snap.MessagesReceived);
            AppendCounter(sb, "hobiary_messages_written_total",
                "Total messages written to HobiaryLogDB.", snap.MessagesWritten);
            AppendCounter(sb, "hobiary_messages_rejected_total",
                "Total messages rejected (unknown plugin, parse error, encryption mismatch).", snap.MessagesRejected);
            AppendCounter(sb, "hobiary_messages_filtered_total",
                "Total messages filtered by per-plugin level filter.", snap.MessagesFiltered);
            AppendCounter(sb, "hobiary_messages_rate_limited_total",
                "Total messages dropped by per-plugin rate limiter.", snap.MessagesRateLimited);
            AppendCounter(sb, "hobiary_messages_schema_invalid_total",
                "Total messages rejected due to missing required fields (schema validation).", snap.MessagesSchemaInvalid);
            AppendCounter(sb, "hobiary_dlq_messages_total",
                "Total messages received from Dead-Letter Queue.", snap.DlqMessages);
            AppendCounter(sb, "hobiary_reconnects_total",
                "Total RabbitMQ reconnect attempts.", snap.Reconnects);
            AppendCounter(sb, "hobiary_heartbeats_received_total",
                "Total heartbeat messages received from plugins.", snap.HeartbeatsReceived);

            var timeout = opts.CurrentValue.HeartbeatTimeoutSeconds;
            var pluginsOnline = timeout > 0
                ? registry.All.Count(r => r.LastHeartbeatAt is not null && r.IsOnline(timeout))
                : registry.All.Count();
            AppendGauge(sb, "hobiary_plugins_online",
                "Number of plugins currently online.", pluginsOnline);

            return Results.Content(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
        });

        return routes;
    }

    private static void AppendCounter(StringBuilder sb, string name, string help, long value)
    {
        sb.Append("# HELP ").AppendLine(name + ' ' + help);
        sb.Append("# TYPE ").AppendLine(name + " counter");
        sb.Append(name).Append(' ').AppendLine(value.ToString());
    }

    private static void AppendGauge(StringBuilder sb, string name, string help, long value)
    {
        sb.Append("# HELP ").AppendLine(name + ' ' + help);
        sb.Append("# TYPE ").AppendLine(name + " gauge");
        sb.Append(name).Append(' ').AppendLine(value.ToString());
    }
}
