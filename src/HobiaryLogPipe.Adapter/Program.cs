using HobiaryLogPipe.Adapter;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

// Create the web application builder.
var builder = WebApplication.CreateBuilder(args);

// Configure options from the appsettings.json file.
builder.Services.Configure<AdapterOptions>(
    builder.Configuration.GetSection("HobiaryLogPipe"));

// Configure RabbitMQ options from the appsettings.json file.
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMQ"));

// Register option validators.
builder.Services.AddSingleton<IValidateOptions<AdapterOptions>, AdapterOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>();

// Register application services.
builder.Services.AddSingleton<PluginRegistry>();
builder.Services.AddSingleton<PipeMetrics>();
builder.Services.AddSingleton<SseHub>();
builder.Services.AddSingleton<IRabbitMqConnectionFactory>(sp =>
    new RabbitMqConnectionFactory(sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value));
builder.Services.AddSingleton<RabbitMqConnectionManager>();
builder.Services.AddSingleton<RabbitMqConsumerTransport>();
builder.Services.AddSingleton<IAdapterMessageTransport, RabbitMqAdapterMessageTransport>();
builder.Services.AddSingleton(sp => new LogMessageProcessor(
    sp.GetRequiredService<ILogger<LogMessageProcessor>>(),
    sp.GetRequiredService<PluginRegistry>(),
    sp.GetRequiredService<PipeMetrics>(),
    sp.GetRequiredService<SseHub>(),
    sp.GetRequiredService<IOptionsMonitor<AdapterOptions>>().CurrentValue));
builder.Services.AddSingleton<PluginRegistrationService>();
builder.Services.AddSingleton<DeadLetterHandler>();

// AdapterService rejestrowany jako singleton, żeby RabbitMqHealthCheck mógł wstrzyknąć tę samą instancję.
// Rejestracja usług hostowanych.
builder.Services.AddSingleton(sp => new AdapterService(
    sp.GetRequiredService<ILogger<AdapterService>>(),
    sp.GetRequiredService<IOptionsMonitor<AdapterOptions>>(),
    sp.GetRequiredService<IAdapterMessageTransport>(),
    sp.GetRequiredService<LogMessageProcessor>(),
    sp.GetRequiredService<PluginRegistrationService>(),
    sp.GetRequiredService<DeadLetterHandler>(),
    sp.GetRequiredService<PluginRegistry>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<AdapterService>());
builder.Services.AddHostedService<MetricsLogger>();
builder.Services.AddHostedService<HeartbeatMonitor>();

// Configure health checks.
builder.Services.AddHealthChecks()
    .AddCheck<RabbitMqHealthCheck>("rabbitmq")
    .AddCheck<HobiaryLogDbHealthCheck>("hobiarylogdb");

var app = builder.Build();

// Configure middleware and endpoints.
app.UseHttpApiKeyProtection();

// Map health check endpoint.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});
// Map other API endpoints.
app.MapDashboardApi();
app.MapLogQueryApi();
app.MapMetricsApi();
app.MapSseApi();

app.Run();
