using HobiaryLogPipe.AspNetCore;
using HobiaryLogPipe.HobiaryLog;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using System.Text;

var pluginId = Environment.GetEnvironmentVariable("HOBIARY_PLUGIN_ID") ?? "brokerless-web-demo";
var baseLogDirectory = Environment.GetEnvironmentVariable("HOBIARY_DIRECT_LOG_DIR") ?? Path.Combine("samples", "BrokerlessWebDemo", "demo-logs");
var url = Environment.GetEnvironmentVariable("HOBIARY_WEB_URL") ?? "http://localhost:5088";
var logDirectory = Path.Combine(baseLogDirectory, pluginId);

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(url);
builder.Services.AddHobiaryLogPipe(options =>
{
    options.Transport = LogPipeTransport.Direct;
    options.PluginId = pluginId;
    options.BaseLogDirectory = baseLogDirectory;
});

var app = builder.Build();

app.UseHobiaryHttpLogging(new HobiaryHttpLoggingOptions
{
    Service = pluginId,
    Source = "BrokerlessWebDemo.Http",
    MinLevel = LogPipeLevel.Info,
});

app.MapGet("/", (ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("BrokerlessWebDemo.Root");
    logger.LogInformation("GET / invoked.");
    return Results.Ok(new
    {
        mode = "Direct",
        pluginId,
        logDirectory,
        endpoints = new[] { "/", "/orders/42", "/fail", "/logs/recent?limit=20&minutes=30", "/logs/view?limit=20&minutes=30" }
    });
});

app.MapGet("/orders/{id:int}", async (int id, ILogPipeWriter writer, CancellationToken cancellationToken) =>
{
    await writer.WriteAsync(new LogMessage
    {
        Level = LogPipeLevel.Info,
        Message = $"Loaded order {id} in brokerless web demo",
        Source = "OrdersEndpoint",
        Service = pluginId,
        Timestamp = DateTimeOffset.UtcNow,
    }, cancellationToken);

    return Results.Ok(new { id, status = "loaded", transport = "Direct" });
});

app.MapGet("/fail", () =>
{
    return Results.Problem("Synthetic failure for brokerless logging demo.", statusCode: 500);
});

app.MapGet("/logs/recent", (int? limit, int? minutes) =>
{
    var resolvedLimit = Math.Clamp(limit ?? 20, 1, 200);
    var resolvedMinutes = Math.Clamp(minutes ?? 30, 1, 24 * 60);
    if (!TryLoadRecords(logDirectory, resolvedLimit, resolvedMinutes, out var records, out var error))
    {
        return Results.Problem(
            detail: error,
            title: "Unable to open brokerless log directory.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Ok(new
    {
        pluginId,
        logDirectory,
        limit = resolvedLimit,
        minutes = resolvedMinutes,
        count = records.Length,
        items = records,
    });
});

app.MapGet("/logs/view", (int? limit, int? minutes) =>
{
    var resolvedLimit = Math.Clamp(limit ?? 20, 1, 200);
    var resolvedMinutes = Math.Clamp(minutes ?? 30, 1, 24 * 60);
    if (!TryLoadRecords(logDirectory, resolvedLimit, resolvedMinutes, out var records, out var error))
    {
        var htmlError = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Brokerless Logs</title>
  <style>
    body { font-family: Consolas, monospace; margin: 2rem; background: #f4f1e8; color: #1f2937; }
    .error { padding: 1rem; background: #fff1f2; border: 1px solid #fecdd3; }
  </style>
</head>
<body>
  <h1>Brokerless Logs</h1>
  <div class="error">{{System.Net.WebUtility.HtmlEncode(error ?? "Unknown error")}}</div>
</body>
</html>
""";
        return Results.Content(htmlError, "text/html; charset=utf-8", statusCode: StatusCodes.Status500InternalServerError);
    }

    var html = BuildLogsHtml(pluginId, logDirectory, resolvedLimit, resolvedMinutes, records);
    return Results.Content(html, "text/html; charset=utf-8");
});

Console.WriteLine($"BrokerlessWebDemo listening on {url}. Logs: '{logDirectory}'.");

await app.RunAsync();

static bool TryLoadRecords(string logDirectory, int limit, int minutes, out BrokerlessLogView[] records, out string? error)
{
    records = [];
    error = null;

    if (!HobiaryLogClient.TryCreate(logDirectory, out var client, out error) || client is null)
        return false;

    using (client)
    {
        var nowUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
        var windowUs = (long)TimeSpan.FromMinutes(minutes).TotalMilliseconds * 1000L;
        records = client.QueryRange(nowUs - windowUs, nowUs + 1_000_000L, limit: limit)
            .Select(record => new BrokerlessLogView(
                record.Level.ToString(),
                record.TimestampUs,
                record.Source,
                record.Service,
                record.TraceId,
                record.CorrelationId,
                record.Payload is { Length: > 0 } ? Encoding.UTF8.GetString(record.Payload) : null))
            .ToArray();
        return true;
    }
}

static string BuildLogsHtml(string pluginId, string logDirectory, int limit, int minutes, BrokerlessLogView[] records)
{
    var sb = new StringBuilder();
    sb.AppendLine("<!DOCTYPE html>");
    sb.AppendLine("<html lang=\"en\">");
    sb.AppendLine("<head>");
    sb.AppendLine("  <meta charset=\"utf-8\">");
    sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
    sb.AppendLine("  <title>Brokerless Logs</title>");
    sb.AppendLine("  <style>");
    sb.AppendLine("    :root { color-scheme: light; }");
    sb.AppendLine("    body { margin: 0; font-family: Georgia, serif; background: linear-gradient(135deg, #f4f1e8, #dbeafe); color: #111827; }");
    sb.AppendLine("    main { max-width: 980px; margin: 0 auto; padding: 32px 20px 48px; }");
    sb.AppendLine("    .hero { background: rgba(255,255,255,0.82); border: 1px solid rgba(17,24,39,0.08); border-radius: 20px; padding: 24px; box-shadow: 0 12px 30px rgba(17,24,39,0.08); } ");
    sb.AppendLine("    h1 { margin: 0 0 8px; font-size: 2.2rem; }");
    sb.AppendLine("    p { margin: 6px 0; line-height: 1.5; }");
    sb.AppendLine("    .meta { color: #374151; font-size: 0.95rem; }");
    sb.AppendLine("    .list { margin-top: 24px; display: grid; gap: 14px; }");
    sb.AppendLine("    .item { background: rgba(255,255,255,0.88); border-radius: 16px; padding: 16px; border-left: 8px solid #94a3b8; box-shadow: 0 8px 20px rgba(17,24,39,0.06); } ");
    sb.AppendLine("    .item.Error, .item.Fatal { border-left-color: #dc2626; }");
    sb.AppendLine("    .item.Warn { border-left-color: #d97706; }");
    sb.AppendLine("    .item.Info { border-left-color: #2563eb; }");
    sb.AppendLine("    .item.Debug, .item.Trace { border-left-color: #64748b; }");
    sb.AppendLine("    .head { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; font-family: Consolas, monospace; font-size: 0.92rem; } ");
    sb.AppendLine("    .payload { margin-top: 10px; padding: 12px; background: #0f172a; color: #e5e7eb; border-radius: 12px; overflow-x: auto; white-space: pre-wrap; font-family: Consolas, monospace; font-size: 0.9rem; }");
    sb.AppendLine("    .empty { margin-top: 24px; padding: 16px; background: rgba(255,255,255,0.8); border-radius: 16px; } ");
    sb.AppendLine("    a { color: #1d4ed8; }");
    sb.AppendLine("  </style>");
    sb.AppendLine("</head>");
    sb.AppendLine("<body>");
    sb.AppendLine("<main>");
    sb.AppendLine("  <section class=\"hero\">");
    sb.AppendLine($"    <h1>Brokerless Logs for {Html(pluginId)}</h1>");
    sb.AppendLine($"    <p class=\"meta\">Directory: {Html(logDirectory)}</p>");
    sb.AppendLine($"    <p class=\"meta\">Window: last {minutes} minute(s), limit {limit}. JSON: <a href=\"/logs/recent?limit={limit}&minutes={minutes}\">/logs/recent</a></p>");
    sb.AppendLine("  </section>");

    if (records.Length == 0)
    {
        sb.AppendLine("  <section class=\"empty\"><strong>No records found.</strong> Generate traffic with <code>/orders/42</code> or <code>/fail</code> and refresh.</section>");
    }
    else
    {
        sb.AppendLine("  <section class=\"list\">");
        foreach (var record in records)
        {
            var isoTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(record.TimestampUs / 1000L).ToString("O");
            sb.AppendLine($"    <article class=\"item {Html(record.Level)}\">");
            sb.AppendLine("      <div class=\"head\">");
            sb.AppendLine($"        <span><strong>{Html(record.Level)}</strong></span>");
            sb.AppendLine($"        <span>{Html(isoTimestamp)}</span>");
            sb.AppendLine($"        <span>source={Html(record.Source ?? "<null>")}</span>");
            sb.AppendLine($"        <span>service={Html(record.Service ?? "<null>")}</span>");
            sb.AppendLine("      </div>");
            if (!string.IsNullOrWhiteSpace(record.TraceId) || !string.IsNullOrWhiteSpace(record.CorrelationId))
            {
                sb.AppendLine("      <div class=\"head\">");
                sb.AppendLine($"        <span>traceId={Html(record.TraceId ?? "<null>")}</span>");
                sb.AppendLine($"        <span>correlationId={Html(record.CorrelationId ?? "<null>")}</span>");
                sb.AppendLine("      </div>");
            }
            sb.AppendLine($"      <div class=\"payload\">{Html(record.Payload ?? "<empty>")}</div>");
            sb.AppendLine("    </article>");
        }
        sb.AppendLine("  </section>");
    }

    sb.AppendLine("</main>");
    sb.AppendLine("</body>");
    sb.AppendLine("</html>");
    return sb.ToString();
}

static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

internal sealed record BrokerlessLogView(
    string Level,
    long TimestampUs,
    string? Source,
    string? Service,
    string? TraceId,
    string? CorrelationId,
    string? Payload);