using HobiaryLogPipe.Adapter;
using Microsoft.AspNetCore.Http;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class HttpApiSecurityTests
{
    [Theory]
    [InlineData("/dashboard", true)]
    [InlineData("/api/logs", true)]
    [InlineData("/metrics", true)]
    [InlineData("/health", true)]
    [InlineData("/favicon.ico", false)]
    public void IsProtectedPath_ReturnsExpected(string path, bool expected)
    {
        Assert.Equal(expected, HttpApiSecurity.IsProtectedPath(path));
    }

    [Fact]
    public void IsAuthorized_ReturnsTrue_WhenApiKeyDisabled()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/logs";

        Assert.True(HttpApiSecurity.IsAuthorized(ctx.Request, ""));
    }

    [Fact]
    public void IsAuthorized_AcceptsHeaderApiKey()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/logs";
        ctx.Request.Headers["X-Api-Key"] = "secret-key";

        Assert.True(HttpApiSecurity.IsAuthorized(ctx.Request, "secret-key"));
    }

    [Fact]
    public void IsAuthorized_AcceptsQueryApiKey()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/dashboard";
        ctx.Request.QueryString = new QueryString("?apiKey=secret-key");

        Assert.True(HttpApiSecurity.IsAuthorized(ctx.Request, "secret-key"));
    }

    [Fact]
    public void IsAuthorized_AcceptsAuthorizationApiKey()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/logs";
        ctx.Request.Headers["Authorization"] = "ApiKey secret-key";

        Assert.True(HttpApiSecurity.IsAuthorized(ctx.Request, "secret-key"));
    }

    [Fact]
    public void IsAuthorized_AcceptsAuthorizationBearer()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/logs";
        ctx.Request.Headers["Authorization"] = "Bearer secret-key";

        Assert.True(HttpApiSecurity.IsAuthorized(ctx.Request, "secret-key"));
    }

    [Fact]
    public void GetSuppliedApiKey_ExtractsBearerToken()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Authorization"] = "Bearer my-token";

        Assert.Equal("my-token", HttpApiSecurity.GetSuppliedApiKey(ctx.Request));
    }

    [Fact]
    public void GetSuppliedApiKey_BearerCaseInsensitive()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Authorization"] = "BEARER secret-key";

        Assert.Equal("secret-key", HttpApiSecurity.GetSuppliedApiKey(ctx.Request));
    }

    [Fact]
    public void IsAuthorized_RejectsWrongApiKey()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/metrics";
        ctx.Request.Headers["X-Api-Key"] = "wrong-key";

        Assert.False(HttpApiSecurity.IsAuthorized(ctx.Request, "secret-key"));
    }
}