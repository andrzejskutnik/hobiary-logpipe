using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Minimal API endpoint do strumieniowania logów przez SSE.
/// GET /api/logs/stream           — wszystkie pluginy
/// GET /api/logs/stream/{pluginId} — tylko wybrany plugin
/// </summary>
internal static class SseApi
{
    public static IEndpointRouteBuilder MapSseApi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/logs/stream", StreamAll);
        routes.MapGet("/api/logs/stream/{pluginId}", StreamPlugin);
        return routes;
    }

    private static async Task StreamAll(HttpContext ctx, SseHub hub)
    {
        await StreamSse(ctx, hub, filterPluginId: null);
    }

    private static async Task StreamPlugin(string pluginId, HttpContext ctx, SseHub hub)
    {
        await StreamSse(ctx, hub, filterPluginId: pluginId);
    }

    private static async Task StreamSse(HttpContext ctx, SseHub hub, string? filterPluginId)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        var reader = hub.Subscribe();
        var ct = ctx.RequestAborted;

        try
        {
            await foreach (var ev in reader.ReadAllAsync(ct))
            {
                if (filterPluginId is not null
                    && !ev.Contains($"\"pluginId\":\"{filterPluginId}\"", StringComparison.Ordinal))
                {
                    continue;
                }

                await ctx.Response.WriteAsync($"data: {ev}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* klient się rozłączył */ }
        finally
        {
            hub.Unsubscribe(reader);
        }
    }
}
