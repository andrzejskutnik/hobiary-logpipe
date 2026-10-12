using System.Net;
using HobiaryLogPipe.AspNetCore;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HobiaryLogPipe.AspNetCore.Tests;

public sealed class HobiaryHttpLoggingMiddlewareTests
{
    private static IHost BuildHost(FakeWriter writer, HobiaryHttpLoggingOptions? options = null)
    {
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(svc =>
                {
                    svc.AddSingleton<ILogPipeWriter>(writer);
                    svc.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseHobiaryHttpLogging(options);
                    app.Run(ctx =>
                    {
                        ctx.Response.StatusCode = ctx.Request.Path == "/error" ? 500 : 200;
                        return Task.CompletedTask;
                    });
                });
            })
            .Build();
    }

    [Fact]
    public async Task Get_200_LogsInfoLevel()
    {
        var writer = new FakeWriter();
        using var host = BuildHost(writer);
        await host.StartAsync();
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        await Task.Delay(50); // fire-and-forget

        var msg = Assert.Single(writer.Messages);
        Assert.Equal(LogPipeLevel.Info, msg.Level);
        Assert.Contains("GET", msg.Message);
        Assert.Contains("200", msg.Message);
    }

    [Fact]
    public async Task Get_500_LogsErrorLevel()
    {
        var writer = new FakeWriter();
        using var host = BuildHost(writer);
        await host.StartAsync();
        var client = host.GetTestClient();

        await client.GetAsync("/error");

        await Task.Delay(50);

        var msg = Assert.Single(writer.Messages);
        Assert.Equal(LogPipeLevel.Error, msg.Level);
        Assert.Contains("500", msg.Message);
    }

    [Fact]
    public async Task MinLevel_Error_Suppresses200()
    {
        var writer = new FakeWriter();
        var opts = new HobiaryHttpLoggingOptions { MinLevel = LogPipeLevel.Error };
        using var host = BuildHost(writer, opts);
        await host.StartAsync();
        var client = host.GetTestClient();

        await client.GetAsync("/ok");

        await Task.Delay(50);

        Assert.Empty(writer.Messages);
    }

    [Fact]
    public async Task Source_And_Service_Are_Propagated()
    {
        var writer = new FakeWriter();
        var opts = new HobiaryHttpLoggingOptions { Source = "api", Service = "my-service" };
        using var host = BuildHost(writer, opts);
        await host.StartAsync();
        var client = host.GetTestClient();

        await client.GetAsync("/anything");

        await Task.Delay(50);

        var msg = Assert.Single(writer.Messages);
        Assert.Equal("api", msg.Source);
        Assert.Equal("my-service", msg.Service);
    }

    private sealed class FakeWriter : ILogPipeWriter
    {
        public List<LogMessage> Messages { get; } = [];
        public Task WriteAsync(LogMessage message, CancellationToken cancellationToken = default)
        {
            lock (Messages) Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
