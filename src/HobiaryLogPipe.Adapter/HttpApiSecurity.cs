using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter;

internal static class HttpApiSecurity
{
    internal static bool IsProtectedPath(PathString path) =>
        path.StartsWithSegments("/dashboard", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    internal static string? GetSuppliedApiKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var header)
            && !string.IsNullOrWhiteSpace(header.FirstOrDefault()))
        {
            return header.FirstOrDefault();
        }

        if (request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            var authorizationValue = authorizationHeader.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authorizationValue))
            {
                if (authorizationValue.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                {
                    var apiKey = authorizationValue[7..].Trim();
                    if (!string.IsNullOrWhiteSpace(apiKey))
                        return apiKey;
                }
                else if (authorizationValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authorizationValue[7..].Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                        return token;
                }
            }
        }

        var query = request.Query["apiKey"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(query) ? null : query;
    }

    internal static bool IsAuthorized(HttpRequest request, string configuredApiKey)
    {
        if (string.IsNullOrEmpty(configuredApiKey)) return true;
        if (!IsProtectedPath(request.Path)) return true;

        var supplied = GetSuppliedApiKey(request);
        if (supplied is null) return false;

        var expectedBytes = Encoding.UTF8.GetBytes(configuredApiKey);
        var actualBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    internal static IApplicationBuilder UseHttpApiKeyProtection(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            var options = ctx.RequestServices.GetRequiredService<IOptionsMonitor<AdapterOptions>>().CurrentValue;
            if (IsAuthorized(ctx.Request, options.HttpApiKey))
            {
                await next();
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "ApiKey, Bearer";
            await ctx.Response.WriteAsJsonAsync(
                ApiResponses.Unauthorized(
                    "Unauthorized. Supply X-Api-Key header, Authorization: ApiKey <key>, Authorization: Bearer <key>, or ?apiKey=... query parameter."));
        });
    }
}