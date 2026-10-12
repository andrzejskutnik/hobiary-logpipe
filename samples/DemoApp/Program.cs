// DemoApp — przykład użycia wszystkich trzech sinkow HobiaryLogPipe:
//   1. Serilog  → HobiaryLogPipeSink
//   2. Microsoft.Extensions.Logging → HobiaryLogPipeLoggerProvider
//   3. OpenTelemetry → HobiaryLogPipeExporter
//
// Wymagania:
// - tryb RabbitMq: działający broker i Adapter
// - tryb Direct: lokalne hobiarylogdb.dll/libhobiarylogdb.so

using HobiaryLogPipe.InputPlugin;
using HobiaryLogPipe.MicrosoftLogging;
using HobiaryLogPipe.OpenTelemetry;
using HobiaryLogPipe.Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Serilog;

var transportRaw = Environment.GetEnvironmentVariable("HOBIARY_TRANSPORT");
var transport = Enum.TryParse<LogPipeTransport>(transportRaw, ignoreCase: true, out var parsedTransport)
    ? parsedTransport
    : LogPipeTransport.RabbitMq;
var baseLogDirectory = Environment.GetEnvironmentVariable("HOBIARY_DIRECT_LOG_DIR") ?? "logs-direct-demo";

// ── Konfiguracja połączenia z RabbitMQ ────────────────────────────────────
var rabbitHost = Environment.GetEnvironmentVariable("HOBIARY_RABBIT_HOST") ?? "localhost";
var rabbitPort = int.TryParse(Environment.GetEnvironmentVariable("HOBIARY_RABBIT_PORT"), out var parsedPort)
    ? parsedPort
    : 5672;
var rabbitUser = Environment.GetEnvironmentVariable("HOBIARY_RABBIT_USER") ?? "guest";
var rabbitPass = Environment.GetEnvironmentVariable("HOBIARY_RABBIT_PASSWORD") ?? "guest";
var serilogPluginId = Environment.GetEnvironmentVariable("HOBIARY_SERILOG_PLUGIN_ID") ?? "demo-serilog";
var melPluginId = Environment.GetEnvironmentVariable("HOBIARY_MEL_PLUGIN_ID") ?? "demo-mel";
var otelPluginId = Environment.GetEnvironmentVariable("HOBIARY_OTEL_PLUGIN_ID") ?? "demo-otel";
var holdSeconds = int.TryParse(Environment.GetEnvironmentVariable("HOBIARY_HOLD_SECONDS"), out var parsedHoldSeconds)
    ? parsedHoldSeconds
    : 15;

// ═══════════════════════════════════════════════════════════════════════════
// 1. Serilog sink
// ═══════════════════════════════════════════════════════════════════════════
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()
    .WriteTo.HobiaryLogPipe(opts =>
    {
        opts.Transport = transport;
        opts.PluginId = serilogPluginId;
        opts.BaseLogDirectory = baseLogDirectory;
        opts.Host = rabbitHost;
        opts.Port = rabbitPort;
        opts.Username = rabbitUser;
        opts.Password = rabbitPass;
    })
    .CreateLogger();

Log.Information("Serilog: aplikacja startuje");
Log.Warning("Serilog: ostrzeżenie testowe");
Log.Error("Serilog: błąd testowy");

// ═══════════════════════════════════════════════════════════════════════════
// 2. Microsoft.Extensions.Logging + 3. OpenTelemetry — przez IHost
// ═══════════════════════════════════════════════════════════════════════════
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();

        // 2. MEL provider
        logging.AddConsole();
        logging.AddHobiaryLogPipe(opts =>
        {
            opts.Transport = transport;
            opts.PluginId = melPluginId;
            opts.BaseLogDirectory = baseLogDirectory;
            opts.Host = rabbitHost;
            opts.Port = rabbitPort;
            opts.Username = rabbitUser;
            opts.Password = rabbitPass;
        });

        // 3. OpenTelemetry exporter
        logging.AddOpenTelemetry(otel =>
        {
            otel.IncludeFormattedMessage = true;
            otel.AddHobiaryLogPipeExporter(opts =>
            {
                opts.Transport = transport;
                opts.PluginId = otelPluginId;
                opts.BaseLogDirectory = baseLogDirectory;
                opts.Host = rabbitHost;
                opts.Port = rabbitPort;
                opts.Username = rabbitUser;
                opts.Password = rabbitPass;
            });
        });
    })
    .ConfigureServices(services => services.AddHostedService(sp => new DemoWorker(holdSeconds, sp.GetRequiredService<ILogger<DemoWorker>>())))
    .Build();

Console.WriteLine(transport == LogPipeTransport.Direct
    ? $"DemoApp configured for Direct transport in '{baseLogDirectory}'. Plugins: serilog='{serilogPluginId}', mel='{melPluginId}', otel='{otelPluginId}'."
    : $"DemoApp configured for RabbitMQ {rabbitHost}:{rabbitPort}. Plugins: serilog='{serilogPluginId}', mel='{melPluginId}', otel='{otelPluginId}'. HoldSeconds={holdSeconds}.");

await host.RunAsync();

await Log.CloseAndFlushAsync();

// ── Worker ────────────────────────────────────────────────────────────────

internal sealed class DemoWorker(int holdSeconds, ILogger<DemoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogTrace("DemoWorker: Trace");
        logger.LogDebug("DemoWorker: Debug");
        logger.LogInformation("DemoWorker: Information");
        logger.LogWarning("DemoWorker: Warning");
        logger.LogError("DemoWorker: Error");
        logger.LogCritical("DemoWorker: Critical");

        if (holdSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(holdSeconds), stoppingToken);

        // Zatrzymaj host po demonstracji
        Environment.Exit(0);
    }
}
