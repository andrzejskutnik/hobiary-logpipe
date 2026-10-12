using Microsoft.AspNetCore.Http;

namespace HobiaryLogPipe.Adapter;

internal sealed record ApiError(
    string Code,
    string Error,
    object? Details = null);

internal static class ApiResponses
{
    internal static IResult BadRequest(string code, string error, object? details = null) =>
        Results.BadRequest(new ApiError(code, error, details));

    internal static IResult NotFound(string code, string error, object? details = null) =>
        Results.NotFound(new ApiError(code, error, details));

    internal static ApiError Unauthorized(string error, object? details = null) =>
        new("unauthorized", error, details);
}