using HobiaryLogPipe.InputPlugin;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HobiaryLogPipe.AspNetCore;

/// <summary>
/// Rozszerzenia IApplicationBuilder do rejestracji middleware logowania HTTP.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Dodaje middleware logujący żądania HTTP przez HobiaryLogPipe.
    /// Wymaga, by ILogPipeWriter był zarejestrowany w DI (np. przez AddHobiaryLogPipe()).
    /// </summary>
    public static IApplicationBuilder UseHobiaryHttpLogging(
        this IApplicationBuilder app,
        HobiaryHttpLoggingOptions? options = null)
    {
        var writer = app.ApplicationServices.GetRequiredService<ILogPipeWriter>();
        return app.UseMiddleware<HobiaryHttpLoggingMiddleware>(writer, options ?? new HobiaryHttpLoggingOptions());
    }
}
