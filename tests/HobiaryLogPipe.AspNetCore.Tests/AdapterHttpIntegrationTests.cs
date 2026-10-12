using System.Net;
using System.Net.Http.Json;
using System.Text;
using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.HobiaryLog;
using HobiaryLogPipe.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.AspNetCore.Tests;

public sealed class AdapterHttpIntegrationTests
{
    [Fact]
    public async Task Dashboard_WithoutApiKey_ReturnsUnauthorized()
    {
        using var registry = new PluginRegistry();
        using var host = await BuildHostAsync(registry, httpApiKey: "secret-key");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/dashboard");
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("unauthorized", payload!.Code);
        Assert.Contains("Unauthorized", payload.Error);
    }

    [Fact]
    public async Task Dashboard_WithApiKey_ReturnsHtml()
    {
        using var registry = new PluginRegistry();
        using var host = await BuildHostAsync(registry, httpApiKey: "secret-key");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/dashboard");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hobiary LogPipe Dashboard", html);
    }

    [Fact]
    public async Task Dashboard_WithAuthorizationApiKey_ReturnsHtml()
    {
        using var registry = new PluginRegistry();
        using var host = await BuildHostAsync(registry, httpApiKey: "secret-key");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey secret-key");

        var response = await client.GetAsync("/dashboard");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hobiary LogPipe Dashboard", html);
    }

    [Fact]
    public async Task Logs_PagedQuery_ReturnsPagingEnvelope_WhenDllAvailable()
    {
        var setup = await CreateRegistryWithLogsAsync(recordCount: 5);
        if (!setup.Success)
            return;

        await using var _ = setup;
        using var host = await BuildHostAsync(setup.Registry!, httpApiKey: "secret-key");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync($"/api/logs?pluginId={setup.PluginId}&paged=true&limit=2");
        var payload = await response.Content.ReadFromJsonAsync<PagedLogsDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.NotNull(payload.NextCursor);
        Assert.Null(payload.Cursor);
        Assert.True(payload.HasMore);
    }

    [Fact]
    public async Task Export_WithCursorAndLimit_ReturnsChunkAndHeaders_WhenDllAvailable()
    {
        var setup = await CreateRegistryWithLogsAsync(recordCount: 5);
        if (!setup.Success)
            return;

        await using var _ = setup;
        using var host = await BuildHostAsync(setup.Registry!, httpApiKey: "secret-key");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var firstPage = await client.GetFromJsonAsync<PagedLogsDto>($"/api/logs?pluginId={setup.PluginId}&paged=true&limit=2");
        Assert.NotNull(firstPage);
        Assert.NotNull(firstPage!.NextCursor);

        var response = await client.GetAsync($"/api/plugins/{setup.PluginId}/logs/export?cursor={firstPage.NextCursor}&limit=2");
        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, lines.Length);
        Assert.Equal("2", Assert.Single(response.Headers.GetValues("X-Export-Limit")));
        Assert.Equal("2", Assert.Single(response.Headers.GetValues("X-Export-Count")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("X-Has-More")));
        Assert.Equal(firstPage.NextCursor!.Value.ToString(), Assert.Single(response.Headers.GetValues("X-Cursor-Applied")));
        Assert.True(response.Headers.Contains("X-Next-Cursor"));
    }

    [Fact]
    public async Task Logs_InvalidPluginId_ReturnsBadRequestErrorContract()
    {
        using var registry = new PluginRegistry();
        using var host = await BuildHostAsync(registry, httpApiKey: "secret-key");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/api/logs?pluginId=../../bad&paged=true");
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("invalid_plugin_id", payload!.Code);
        Assert.Equal("Invalid pluginId.", payload.Error);
    }

    [Fact]
    public async Task Logs_InvalidLevel_ReturnsBadRequestErrorContract()
    {
        using var registry = new PluginRegistry();
        registry.Register(new PluginRegistration
        {
            PluginId = "demo-plugin",
            FieldMapping = new FieldMappingConfig(),
            MessageFormat = "json",
            Client = null!,
            RegisteredAt = DateTimeOffset.UtcNow,
        });
        using var host = await BuildHostAsync(registry, httpApiKey: "secret-key");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/api/logs?pluginId=demo-plugin&level=Verbose");
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("invalid_level", payload!.Code);
        Assert.Contains("Unknown level 'Verbose'", payload.Error);
    }

    [Fact]
    public async Task Dashboard_WithAuthorizationBearer_ReturnsHtml()
    {
        using var registry = new PluginRegistry();
        using var host = await BuildHostAsync(registry, httpApiKey: "secret-key");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer secret-key");

        var response = await client.GetAsync("/dashboard");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hobiary LogPipe Dashboard", html);
    }

    [Fact]
    public async Task PluginDetail_MissingPlugin_ReturnsNotFoundErrorContract()
    {
        using var registry = new PluginRegistry();
        using var host = await BuildHostAsync(registry, httpApiKey: "secret-key");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/api/plugins/missing-plugin");
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("plugin_not_found", payload!.Code);
        Assert.Contains("missing-plugin", payload.Error);
    }

    [Fact]
    public async Task Logs_SourceFilter_ReturnsOnlyMatchingRecords_WhenDllAvailable()
    {
        var pluginId = "demo-plugin";
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_srcf_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using var registry = new PluginRegistry();
        registry.Register(new PluginRegistration
        {
            PluginId = pluginId,
            FieldMapping = new FieldMappingConfig(),
            MessageFormat = "json",
            Client = client,
            RegisteredAt = DateTimeOffset.UtcNow,
        });

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
        client.Append(new HlogRecord(HlogLevel.Info, ts + 1, Source: "orders", Payload: Encoding.UTF8.GetBytes("msg-a")));
        client.Append(new HlogRecord(HlogLevel.Info, ts + 2, Source: "payments", Payload: Encoding.UTF8.GetBytes("msg-b")));
        client.Append(new HlogRecord(HlogLevel.Info, ts + 3, Source: "orders", Payload: Encoding.UTF8.GetBytes("msg-c")));

        using var host = await BuildHostAsync(registry, httpApiKey: "");
        var http = host.GetTestClient();

        var response = await http.GetFromJsonAsync<LogEntryDto[]>(
            $"/api/logs?pluginId={pluginId}&source=orders&limit=100");

        Assert.NotNull(response);
        Assert.All(response!, r => Assert.Equal("orders", r.Source));
        Assert.Equal(2, response!.Length);
    }

    [Fact]
    public async Task Logs_ServiceFilter_ReturnsOnlyMatchingRecords_WhenDllAvailable()
    {
        var pluginId = "demo-plugin";
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_svcf_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using var registry = new PluginRegistry();
        registry.Register(new PluginRegistration
        {
            PluginId = pluginId,
            FieldMapping = new FieldMappingConfig(),
            MessageFormat = "json",
            Client = client,
            RegisteredAt = DateTimeOffset.UtcNow,
        });

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
        client.Append(new HlogRecord(HlogLevel.Info, ts + 1, Service: "api-gw", Payload: Encoding.UTF8.GetBytes("gw-1")));
        client.Append(new HlogRecord(HlogLevel.Info, ts + 2, Service: "worker", Payload: Encoding.UTF8.GetBytes("w-1")));
        client.Append(new HlogRecord(HlogLevel.Info, ts + 3, Service: "api-gw", Payload: Encoding.UTF8.GetBytes("gw-2")));

        using var host = await BuildHostAsync(registry, httpApiKey: "");
        var http = host.GetTestClient();

        var response = await http.GetFromJsonAsync<LogEntryDto[]>(
            $"/api/logs?pluginId={pluginId}&service=api-gw&limit=100");

        Assert.NotNull(response);
        Assert.All(response!, r => Assert.Equal("api-gw", r.Service));
        Assert.Equal(2, response!.Length);
    }

    [Fact]
    public async Task Logs_TraceIdFilter_ReturnsOnlyMatchingRecords_WhenDllAvailable()
    {
        var pluginId = "demo-plugin";
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_trcf_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null) return;

        using var registry = new PluginRegistry();
        registry.Register(new PluginRegistration
        {
            PluginId = pluginId,
            FieldMapping = new FieldMappingConfig(),
            MessageFormat = "json",
            Client = client,
            RegisteredAt = DateTimeOffset.UtcNow,
        });

        var traceA = Guid.NewGuid();
        var traceB = Guid.NewGuid();
        var traceABytes = traceA.ToByteArray();
        var traceBBytes = traceB.ToByteArray();
        var traceAHex = Convert.ToHexString(traceABytes).ToLowerInvariant();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
        client.Append(new HlogRecord(HlogLevel.Info, ts + 1,
            TraceId: traceABytes,
            Payload: Encoding.UTF8.GetBytes("trace-a-1")));
        client.Append(new HlogRecord(HlogLevel.Info, ts + 2,
            TraceId: traceBBytes,
            Payload: Encoding.UTF8.GetBytes("trace-b-1")));
        client.Append(new HlogRecord(HlogLevel.Info, ts + 3,
            TraceId: traceABytes,
            Payload: Encoding.UTF8.GetBytes("trace-a-2")));

        using var host = await BuildHostAsync(registry, httpApiKey: "");
        var http = host.GetTestClient();

        var response = await http.GetFromJsonAsync<LogEntryDto[]>(
            $"/api/logs?pluginId={pluginId}&traceId={Uri.EscapeDataString(traceAHex)}&limit=100");

        Assert.NotNull(response);
        Assert.All(response!, r => Assert.Equal(traceAHex, r.TraceId));
        Assert.Equal(2, response!.Length);
    }

    [Fact]
    public async Task PluginHealth_MissingPlugin_ReturnsNotFound()
    {
        using var registry = new PluginRegistry();
        using var host = await BuildHostAsync(registry, httpApiKey: "");
        var http = host.GetTestClient();

        var response = await http.GetAsync("/api/plugins/ghost/health");
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("plugin_not_found", payload!.Code);
    }

    [Fact]
    public async Task PluginHealth_RegisteredPlugin_ReturnsHealthDto()
    {
        using var registry = new PluginRegistry();
        registry.Register(new PluginRegistration
        {
            PluginId = "demo-plugin",
            FieldMapping = new FieldMappingConfig(),
            MessageFormat = "json",
            Client = null!,
            RegisteredAt = DateTimeOffset.UtcNow,
        });
        using var host = await BuildHostAsync(registry, httpApiKey: "");
        var http = host.GetTestClient();

        var response = await http.GetAsync("/api/plugins/demo-plugin/health");
        var payload = await response.Content.ReadFromJsonAsync<PluginHealthDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("demo-plugin", payload!.PluginId);
        Assert.False(payload.IsDeregistered);
        Assert.NotEqual(default, payload.CheckedAt);
    }

    private static async Task<IHost> BuildHostAsync(PluginRegistry registry, string httpApiKey)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IOptionsMonitor<AdapterOptions>>(
                        new TestOptionsMonitor<AdapterOptions>(new AdapterOptions
                        {
                            BaseLogDirectory = "logs",
                            AllowedPlugins = ["demo-plugin"],
                            UnknownPluginAction = "deadletter",
                            HttpApiKey = httpApiKey,
                        }));
                    services.AddSingleton(registry);
                    services.AddSingleton<PipeMetrics>();
                    services.AddSingleton<SseHub>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseHttpApiKeyProtection();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapDashboardApi();
                        endpoints.MapLogQueryApi();
                        endpoints.MapMetricsApi();
                        endpoints.MapSseApi();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    private static async Task<RegistrySetup> CreateRegistryWithLogsAsync(int recordCount)
    {
        var pluginId = "demo-plugin";
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_http_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);
        if (!ok || client is null)
            return RegistrySetup.Failed();

        var registry = new PluginRegistry();
        registry.Register(new PluginRegistration
        {
            PluginId = pluginId,
            FieldMapping = new FieldMappingConfig(),
            MessageFormat = "json",
            Client = client,
            RegisteredAt = DateTimeOffset.UtcNow,
        });

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
        for (int i = 0; i < recordCount; i++)
        {
            client.Append(new HlogRecord(
                HlogLevel.Info,
                ts + i,
                Source: $"src-{i}",
                Payload: Encoding.UTF8.GetBytes($"payload-{i}")));
        }

        await Task.Yield();
        return RegistrySetup.Succeeded(registry, pluginId);
    }

    private sealed class PagedLogsDto
    {
        public required List<LogEntryDto> Items { get; init; }
        public long? Cursor { get; init; }
        public int Limit { get; init; }
        public long? NextCursor { get; init; }
        public bool HasMore { get; init; }
    }

    private sealed class LogEntryDto
    {
        public required string Level { get; init; }
        public long TimestampUs { get; init; }
        public string? Source { get; init; }
        public string? Service { get; init; }
        public string? Payload { get; init; }
        public string? TraceId { get; init; }
        public string? CorrelationId { get; init; }
    }

    private sealed class ApiErrorDto
    {
        public required string Code { get; init; }
        public required string Error { get; init; }
    }

    private sealed class PluginHealthDto
    {
        public required string PluginId { get; init; }
        public bool? IsOnline { get; init; }
        public bool IsDeregistered { get; init; }
        public DateTimeOffset? LastHeartbeatAt { get; init; }
        public double? HeartbeatAgeSec { get; init; }
        public int? HeartbeatTimeoutSec { get; init; }
        public ulong RecordCount { get; init; }
        public DateTimeOffset CheckedAt { get; init; }
    }

    private sealed class RegistrySetup : IAsyncDisposable
    {
        private RegistrySetup(bool success, PluginRegistry? registry, string? pluginId)
        {
            Success = success;
            Registry = registry;
            PluginId = pluginId;
        }

        public bool Success { get; }
        public PluginRegistry? Registry { get; }
        public string? PluginId { get; }

        public static RegistrySetup Failed() => new(false, null, null);
        public static RegistrySetup Succeeded(PluginRegistry registry, string pluginId) => new(true, registry, pluginId);

        public ValueTask DisposeAsync()
        {
            Registry?.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}