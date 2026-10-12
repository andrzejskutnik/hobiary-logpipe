using System.Text.Json;
using HobiaryLogPipe.HobiaryLog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Minimal API endpoints do odpytywania stanu adaptera i zarejestrowanych pluginów.
/// GET /api/plugins                             — lista zarejestrowanych pluginów
/// GET /api/plugins/{pluginId}                  — szczegóły pluginu (statystyki)
/// GET /api/plugins/{pluginId}/health           — strukturalny status zdrowia pluginu
/// GET /api/plugins/{pluginId}/stats            — statystyki logów per poziom
/// GET /api/plugins/{pluginId}/logs/export      — eksport logów NDJSON (pełny lub chunkowany)
/// GET /api/metrics                             — snapshot metryk
/// GET /api/logs                                — odpytywanie historycznych logów
/// </summary>
internal static class LogQueryApi
{
    private static readonly JsonSerializerOptions NdjsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IEndpointRouteBuilder MapLogQueryApi(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api");

        group.MapGet("/plugins", (PluginRegistry registry) =>
        {
            var plugins = registry.All.Select(r => new PluginSummary(
                r.PluginId,
                r.MessageFormat,
                r.FieldMapping.Level,
                r.FieldMapping.Message,
                r.RegisteredAt,
                r.IsDeregistered));
            return Results.Ok(plugins);
        });

        group.MapGet("/plugins/{pluginId}", (string pluginId, PluginRegistry registry,
            IOptionsMonitor<AdapterOptions> opts) =>
        {
            if (!registry.TryGet(pluginId, out var reg) || reg is null)
                return ApiResponses.NotFound(
                    "plugin_not_found",
                    $"Plugin '{pluginId}' not found.",
                    new { pluginId });

            var timeout = opts.CurrentValue.HeartbeatTimeoutSeconds;
            return Results.Ok(new PluginDetail(
                reg.PluginId,
                reg.MessageFormat,
                reg.FieldMapping.Level,
                reg.FieldMapping.Message,
                reg.FieldMapping.Source,
                reg.FieldMapping.Service,
                reg.RegisteredAt,
                reg.Client.ActiveRecordCount,
                reg.LastHeartbeatAt,
                timeout > 0 ? reg.IsOnline(timeout) : null,
                reg.IsDeregistered));
        });

        group.MapGet("/metrics", (PipeMetrics metrics) =>
        {
            var s = metrics.GetSnapshot();
            return Results.Ok(s);
        });

        // GET /api/plugins/{pluginId}/stats?from=...&to=...
        group.MapGet("/plugins/{pluginId}/stats", (
            string pluginId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            PluginRegistry registry) =>
        {
            if (!PluginIdValidator.IsValid(pluginId))
                return ApiResponses.BadRequest(
                    "invalid_plugin_id",
                    "Invalid pluginId.",
                    new { pluginId });

            if (!registry.TryGet(pluginId, out var reg) || reg is null)
                return ApiResponses.NotFound(
                    "plugin_not_found",
                    $"Plugin '{pluginId}' not found or not yet registered.",
                    new { pluginId });

            var fromDto = from ?? DateTimeOffset.UtcNow.AddHours(-1);
            var toDto = to ?? DateTimeOffset.UtcNow;
            var fromUs = fromDto.ToUnixTimeMilliseconds() * 1000L;
            var toUs = toDto.ToUnixTimeMilliseconds() * 1000L;

            if (fromUs > toUs)
                return ApiResponses.BadRequest(
                    "invalid_time_range",
                    "'from' must be before 'to'.",
                    new { from, to });

            var counts = reg.Client.CountByLevel(fromUs, toUs);
            var total = counts.Sum();

            return Results.Ok(new LogStats(
                pluginId,
                fromDto,
                toDto,
                total,
                new LevelCounts(
                    counts[(int)HlogLevel.Trace],
                    counts[(int)HlogLevel.Debug],
                    counts[(int)HlogLevel.Info],
                    counts[(int)HlogLevel.Warn],
                    counts[(int)HlogLevel.Error],
                    counts[(int)HlogLevel.Fatal])));
        });

        // GET /api/logs?pluginId=orders&from=...&to=...&level=Warn&source=OrdersService&service=api-gw&traceId=...&limit=100
        group.MapGet("/logs", (
            string pluginId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? level,
            string? source,
            string? service,
            string? traceId,
            int? limit,
            long? cursor,
            int? offset,
            bool? paged,
            PluginRegistry registry) =>
        {
            if (!PluginIdValidator.IsValid(pluginId))
                return ApiResponses.BadRequest(
                    "invalid_plugin_id",
                    "Invalid pluginId.",
                    new { pluginId });

            if (!registry.TryGet(pluginId, out var reg) || reg is null)
                return ApiResponses.NotFound(
                    "plugin_not_found",
                    $"Plugin '{pluginId}' not found or not yet registered.",
                    new { pluginId });

            var fromUs = (from ?? DateTimeOffset.UtcNow.AddHours(-1)).ToUnixTimeMilliseconds() * 1000L;
            var toUs = (to ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() * 1000L;

            if (fromUs > toUs)
                return ApiResponses.BadRequest(
                    "invalid_time_range",
                    "'from' must be before 'to'.",
                    new { from, to });

            HlogLevel? minLevel = null;
            if (level is not null)
            {
                if (!Enum.TryParse<HlogLevel>(level, ignoreCase: true, out var parsedLevel))
                    return ApiResponses.BadRequest(
                        "invalid_level",
                        $"Unknown level '{level}'. Valid: Trace, Debug, Info, Warn, Error, Fatal.",
                        new { level });
                minLevel = parsedLevel;
            }

            int effectiveLimit = Math.Clamp(limit ?? 200, 1, 1000);
            int effectiveOffset = Math.Clamp(offset ?? 0, 0, 50_000);
            bool wantsPagedResponse = paged == true || offset.HasValue || cursor.HasValue;

            if (!wantsPagedResponse)
            {
                var records = reg.Client.QueryRange(fromUs, toUs, minLevel, effectiveLimit);
                var filtered = ApplyFilters(records, source, service, traceId);

                var entries = filtered.Select(r => new LogEntry(
                    r.Level.ToString().ToLowerInvariant(),
                    r.TimestampUs,
                    r.Source,
                    r.Service,
                    r.Payload is { Length: > 0 }
                        ? System.Text.Encoding.UTF8.GetString(r.Payload)
                        : null,
                    TraceId: r.TraceId,
                    CorrelationId: r.CorrelationId)).ToArray();

                return Results.Ok(entries);
            }

            if (offset.HasValue && !cursor.HasValue)
            {
                var rawLimit = checked(effectiveOffset + effectiveLimit + 1);
                var records = reg.Client.QueryRange(fromUs, toUs, minLevel, rawLimit);
                var filtered = ApplyFilters(records, source, service, traceId);
                var entries = filtered.Select(r => new LogEntry(
                    r.Level.ToString().ToLowerInvariant(),
                    r.TimestampUs,
                    r.Source,
                    r.Service,
                    r.Payload is { Length: > 0 }
                        ? System.Text.Encoding.UTF8.GetString(r.Payload)
                        : null,
                    TraceId: r.TraceId,
                    CorrelationId: r.CorrelationId)).ToArray();

                return Results.Ok(LogQueryPaging.PageByOffset(entries, effectiveOffset, effectiveLimit));
            }

            var pageFromUs = cursor.HasValue
                ? Math.Max(fromUs, checked(cursor.Value + 1))
                : fromUs;

            var pageRecords = reg.Client.QueryRange(pageFromUs, toUs, minLevel, effectiveLimit + 1);
            var pageFiltered = ApplyFilters(pageRecords, source, service, traceId);
            var pageEntries = pageFiltered.Select(r => new LogEntry(
                r.Level.ToString().ToLowerInvariant(),
                r.TimestampUs,
                r.Source,
                r.Service,
                r.Payload is { Length: > 0 }
                    ? System.Text.Encoding.UTF8.GetString(r.Payload)
                    : null,
                TraceId: r.TraceId,
                CorrelationId: r.CorrelationId)).ToArray();

            return Results.Ok(LogQueryPaging.PageByCursor(pageEntries, cursor, effectiveLimit));
        });

        // GET /api/plugins/{pluginId}/logs/export?from=...&to=...&level=Warn&source=...&service=...
        group.MapGet("/plugins/{pluginId}/logs/export", (
            string pluginId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? level,
            string? source,
            string? service,
            int? limit,
            long? cursor,
            int? offset,
            HttpContext http,
            PluginRegistry registry) =>
        {
            if (!PluginIdValidator.IsValid(pluginId))
                return ApiResponses.BadRequest(
                    "invalid_plugin_id",
                    "Invalid pluginId.",
                    new { pluginId });

            if (!registry.TryGet(pluginId, out var reg) || reg is null)
                return ApiResponses.NotFound(
                    "plugin_not_found",
                    $"Plugin '{pluginId}' not found or not yet registered.",
                    new { pluginId });

            var fromDto = from ?? DateTimeOffset.UtcNow.AddHours(-1);
            var toDto = to ?? DateTimeOffset.UtcNow;
            var fromUs = fromDto.ToUnixTimeMilliseconds() * 1000L;
            var toUs = toDto.ToUnixTimeMilliseconds() * 1000L;

            if (fromUs > toUs)
                return ApiResponses.BadRequest(
                    "invalid_time_range",
                    "'from' must be before 'to'.",
                    new { from, to });

            HlogLevel? minLevel = null;
            if (level is not null)
            {
                if (!Enum.TryParse<HlogLevel>(level, ignoreCase: true, out var parsedLevel))
                    return ApiResponses.BadRequest(
                        "invalid_level",
                        $"Unknown level '{level}'. Valid: Trace, Debug, Info, Warn, Error, Fatal.",
                        new { level });
                minLevel = parsedLevel;
            }

            int effectiveLimit = Math.Clamp(limit ?? 1000, 1, 1000);
            int effectiveOffset = Math.Clamp(offset ?? 0, 0, 50_000);
            bool wantsPagedExport = limit.HasValue || offset.HasValue || cursor.HasValue;
            LogQueryRecord[] selected;
            long? nextCursor = null;

            if (offset.HasValue && !cursor.HasValue)
            {
                var records = reg.Client.QueryRange(
                    fromUs,
                    toUs,
                    minLevel,
                    checked(effectiveOffset + effectiveLimit + 1));
                selected = ApplyFilters(records, source, service, traceId: null)
                    .Skip(effectiveOffset).Take(effectiveLimit).ToArray();
                http.Response.Headers["X-Offset-Applied"] = effectiveOffset.ToString();
            }
            else
            {
                var exportFromUs = cursor.HasValue
                    ? Math.Max(fromUs, checked(cursor.Value + 1))
                    : fromUs;
                var records = reg.Client.QueryRange(
                    exportFromUs,
                    toUs,
                    minLevel,
                    wantsPagedExport ? effectiveLimit + 1 : int.MaxValue);

                var filteredExport = ApplyFilters(records, source, service, traceId: null).ToList();
                selected = wantsPagedExport
                    ? filteredExport.Take(effectiveLimit).ToArray()
                    : [.. filteredExport];

                nextCursor = wantsPagedExport && filteredExport.Count > effectiveLimit
                    ? selected[^1].TimestampUs
                    : null;
            }

            http.Response.Headers["X-Export-Limit"] = effectiveLimit.ToString();
            http.Response.Headers["X-Export-Count"] = selected.Length.ToString();
            http.Response.Headers["X-Has-More"] = (nextCursor is not null).ToString().ToLowerInvariant();
            if (cursor.HasValue)
                http.Response.Headers["X-Cursor-Applied"] = cursor.Value.ToString();
            if (nextCursor.HasValue)
                http.Response.Headers["X-Next-Cursor"] = nextCursor.Value.ToString();

            return Results.Stream(async stream =>
            {
                var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                await using (writer)
                {
                    foreach (var r in selected)
                    {
                        var entry = new LogEntry(
                            r.Level.ToString().ToLowerInvariant(),
                            r.TimestampUs,
                            r.Source,
                            r.Service,
                            r.Payload is { Length: > 0 }
                                ? System.Text.Encoding.UTF8.GetString(r.Payload)
                                : null,
                            TraceId: r.TraceId,
                            CorrelationId: r.CorrelationId);
                        await writer.WriteLineAsync(JsonSerializer.Serialize(entry, NdjsonOpts));
                    }
                }
            }, contentType: "application/x-ndjson");
        });

        // GET /api/plugins/{pluginId}/health
        group.MapGet("/plugins/{pluginId}/health", (
            string pluginId,
            PluginRegistry registry,
            IOptionsMonitor<AdapterOptions> opts) =>
        {
            if (!registry.TryGet(pluginId, out var reg) || reg is null)
                return ApiResponses.NotFound(
                    "plugin_not_found",
                    $"Plugin '{pluginId}' not found.",
                    new { pluginId });

            var timeout = opts.CurrentValue.HeartbeatTimeoutSeconds;
            var now = DateTimeOffset.UtcNow;
            var lastBeat = reg.LastHeartbeatAt;
            var ageSec = lastBeat.HasValue
                ? (now - lastBeat.Value).TotalSeconds
                : (double?)null;
            var isOnline = timeout > 0 ? reg.IsOnline(timeout) : (bool?)null;

            return Results.Ok(new PluginHealth(
                reg.PluginId,
                isOnline,
                reg.IsDeregistered,
                lastBeat,
                ageSec.HasValue ? Math.Round(ageSec.Value, 1) : null,
                timeout > 0 ? timeout : null,
                reg.Client?.ActiveRecordCount ?? 0,
                now));
        });

        return routes;
    }

    private static IEnumerable<LogQueryRecord> ApplyFilters(
        IEnumerable<LogQueryRecord> records,
        string? source,
        string? service,
        string? traceId)
    {
        if (!string.IsNullOrEmpty(source))
            records = records.Where(r => string.Equals(r.Source, source, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(service))
            records = records.Where(r => string.Equals(r.Service, service, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(traceId))
            records = records.Where(r => string.Equals(r.TraceId, traceId, StringComparison.OrdinalIgnoreCase));
        return records;
    }
}

internal sealed record PluginHealth(
    string PluginId,
    bool? IsOnline,
    bool IsDeregistered,
    DateTimeOffset? LastHeartbeatAt,
    double? HeartbeatAgeSec,
    int? HeartbeatTimeoutSec,
    ulong RecordCount,
    DateTimeOffset CheckedAt);

internal sealed record PluginSummary(
    string PluginId,
    string MessageFormat,
    string LevelField,
    string MessageField,
    DateTimeOffset RegisteredAt,
    bool IsDeregistered);

internal sealed record PluginDetail(
    string PluginId,
    string MessageFormat,
    string LevelField,
    string MessageField,
    string SourceField,
    string ServiceField,
    DateTimeOffset RegisteredAt,
    ulong RecordCount,
    DateTimeOffset? LastHeartbeatAt,
    bool? IsOnline,
    bool IsDeregistered);

internal sealed record LogEntry(
    string Level,
    long TimestampUs,
    string? Source,
    string? Service,
    string? Payload,
    string? TraceId = null,
    string? CorrelationId = null);

internal sealed record LevelCounts(
    long Trace,
    long Debug,
    long Info,
    long Warn,
    long Error,
    long Fatal);

internal sealed record LogStats(
    string PluginId,
    DateTimeOffset From,
    DateTimeOffset To,
    long Total,
    LevelCounts ByLevel);

internal sealed record PagedLogsResponse(
    IReadOnlyList<LogEntry> Items,
    long? Cursor,
    int Limit,
    long? NextCursor,
    bool HasMore);

internal static class LogQueryPaging
{
    internal static PagedLogsResponse PageByOffset(IReadOnlyList<LogEntry> entries, int offset, int limit)
    {
        var safeOffset = Math.Max(0, offset);
        var safeLimit = Math.Clamp(limit, 1, 1000);

        if (safeOffset >= entries.Count)
            return new PagedLogsResponse([], null, safeLimit, null, false);

        var slice = entries.Skip(safeOffset).Take(safeLimit).ToArray();
        long? nextCursor = entries.Count > safeOffset + safeLimit
            ? slice[^1].TimestampUs
            : null;
        return new PagedLogsResponse(slice, null, safeLimit, nextCursor, nextCursor is not null);
    }

    internal static PagedLogsResponse PageByCursor(IReadOnlyList<LogEntry> entries, long? cursor, int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, 1000);
        if (entries.Count == 0)
            return new PagedLogsResponse([], cursor, safeLimit, null, false);

        var hasMore = entries.Count > safeLimit;
        var slice = hasMore ? entries.Take(safeLimit).ToArray() : entries.ToArray();
        long? nextCursor = hasMore ? slice[^1].TimestampUs : null;
        return new PagedLogsResponse(slice, cursor, safeLimit, nextCursor, hasMore);
    }
}
