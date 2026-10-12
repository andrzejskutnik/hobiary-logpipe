using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.AspNetCore.Http;

namespace HobiaryLogPipe.AspNetCore;

/// <summary>
/// Middleware logujący każde żądanie HTTP (metoda, ścieżka, status, czas) przez ILogPipeWriter.
/// </summary>
public sealed class HobiaryHttpLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogPipeWriter _writer;
    private readonly HobiaryHttpLoggingOptions _options;

    public HobiaryHttpLoggingMiddleware(
        RequestDelegate next,
        ILogPipeWriter writer,
        HobiaryHttpLoggingOptions options)
    {
        _next = next;
        _writer = writer;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var level = context.Response.StatusCode >= 500 ? LogPipeLevel.Error
                      : context.Response.StatusCode >= 400 ? LogPipeLevel.Warn
                      : LogPipeLevel.Info;

            if (level >= _options.MinLevel)
            {
                var message = new LogMessage
                {
                    Level   = level,
                    Message = $"{context.Request.Method} {context.Request.Path} → {context.Response.StatusCode} ({sw.ElapsedMilliseconds} ms)",
                    Source  = _options.Source,
                    Service = _options.Service,
                };
                // Fire-and-forget; błędy ignorujemy, żeby nie przerywać potoku HTTP.
                _ = _writer.WriteAsync(message, CancellationToken.None);
            }
        }
    }
}
