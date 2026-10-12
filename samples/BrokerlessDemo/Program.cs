using System.Text;
using System.Text.Json;
using HobiaryLogPipe.HobiaryLog;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using HobiaryLogPipe.MicrosoftLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var cli = DemoCliOptions.Parse(args);
var pluginId = cli.PluginId
    ?? Environment.GetEnvironmentVariable("HOBIARY_PLUGIN_ID")
    ?? "brokerless-demo";
var baseLogDirectory = cli.LogDirectory
    ?? Environment.GetEnvironmentVariable("HOBIARY_DIRECT_LOG_DIR")
    ?? Path.Combine("samples", "BrokerlessDemo", "demo-logs");
var holdSeconds = cli.HoldSeconds
    ?? (int.TryParse(Environment.GetEnvironmentVariable("HOBIARY_HOLD_SECONDS"), out var parsedHoldSeconds)
        ? parsedHoldSeconds
        : 0);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        logging.Services.AddSingleton<ILoggerProvider, HobiaryLogPipeLoggerProvider>();
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton(sp => new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            Options.Create(new InputPluginOptions
            {
                Transport = LogPipeTransport.Direct,
                PluginId = pluginId,
                BaseLogDirectory = baseLogDirectory,
            })));
        services.AddSingleton<ILogPipeWriter>(sp => sp.GetRequiredService<LogPipeWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<LogPipeWriter>());
        services.AddSingleton(new DemoRuntime(pluginId, baseLogDirectory, holdSeconds));
        services.AddHostedService<BrokerlessDemoWorker>();
    })
    .Build();

Console.WriteLine($"BrokerlessDemo starting for plugin '{pluginId}'. Output: '{Path.Combine(baseLogDirectory, pluginId)}'.");
Console.WriteLine("Arguments: --plugin-id <id> --log-dir <path> --hold-seconds <n>");

await host.RunAsync();

var logDirectory = Path.Combine(baseLogDirectory, pluginId);
if (!HobiaryLogClient.TryCreate(logDirectory, out var client, out var error) || client is null)
{
    Console.Error.WriteLine($"Unable to open generated HobiaryLogDB files: {error}");
    Environment.ExitCode = 1;
    return;
}

using (client)
{
    var nowUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
    var timeWindowUs = (long)TimeSpan.FromMinutes(10).TotalMilliseconds * 1000L;
    var records = client.QueryRange(nowUs - timeWindowUs, nowUs + timeWindowUs, limit: 20);
    var views = records.Select(ToDemoLogView).ToList();
    var demoViews = views
        .Where(view => string.Equals(view.Service, pluginId, StringComparison.Ordinal)
            || string.Equals(view.Source, nameof(BrokerlessDemoWorker), StringComparison.Ordinal))
        .ToList();
    var recordsToDisplay = demoViews;
    if (demoViews.Count < 4
        || !demoViews.Any(view => view.Message == "Simulated business error stored without broker"))
        throw new InvalidOperationException("Demo readback did not contain the expected synthetic records.");

    Console.WriteLine();
    Console.WriteLine($"Read back {records.Count} record(s) from '{logDirectory}'. Displaying {recordsToDisplay.Count} demo-focused record(s):");

    foreach (var record in recordsToDisplay)
    {
        Console.WriteLine($"- [{record.Level}] source={record.Source} service={record.Service}");
        Console.WriteLine($"  message={record.Message}");

        if (!string.Equals(record.HeaderSource, record.Source, StringComparison.Ordinal)
            || !string.Equals(record.HeaderService, record.Service, StringComparison.Ordinal))
        {
            Console.WriteLine($"  headerSource={record.HeaderSource} headerService={record.HeaderService}");
        }
    }
}

static DemoLogView ToDemoLogView(LogQueryRecord record)
{
    var rawPayload = record.Payload is { Length: > 0 }
        ? Encoding.UTF8.GetString(record.Payload)
        : "<empty>";

    if (TryParsePayload(rawPayload, out var payloadView))
    {
        return new DemoLogView(
            record.Level.ToString(),
            payloadView.Source ?? record.Source ?? "<null>",
            payloadView.Service ?? record.Service ?? "<null>",
            payloadView.Message ?? rawPayload,
            record.Source ?? "<null>",
            record.Service ?? "<null>");
    }

    return new DemoLogView(
        record.Level.ToString(),
        record.Source ?? "<null>",
        record.Service ?? "<null>",
        rawPayload,
        record.Source ?? "<null>",
        record.Service ?? "<null>");
}

static bool TryParsePayload(string payload, out PayloadView payloadView)
{
    payloadView = new PayloadView(null, null, null);
    if (string.IsNullOrWhiteSpace(payload) || payload[0] != '{')
        return false;

    try
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        payloadView = new PayloadView(
            ReadString(root, "source"),
            ReadString(root, "service"),
            ReadString(root, "message"));
        return true;
    }
    catch (JsonException)
    {
        return false;
    }
}

static string? ReadString(JsonElement element, string propertyName)
    => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;

internal sealed record DemoRuntime(string PluginId, string BaseLogDirectory, int HoldSeconds);
internal sealed record DemoLogView(
    string Level,
    string Source,
    string Service,
    string Message,
    string HeaderSource,
    string HeaderService);
internal sealed record PayloadView(string? Source, string? Service, string? Message);

internal sealed record DemoCliOptions(string? PluginId, string? LogDirectory, int? HoldSeconds)
{
    public static DemoCliOptions Parse(string[] args)
    {
        string? pluginId = null;
        string? logDirectory = null;
        int? holdSeconds = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--plugin-id" or "-p")
            {
                pluginId = ReadValue(args, ref index, arg);
                continue;
            }

            if (arg is "--log-dir" or "-d")
            {
                logDirectory = ReadValue(args, ref index, arg);
                continue;
            }

            if (arg is "--hold-seconds" or "-h")
            {
                var raw = ReadValue(args, ref index, arg);
                if (!int.TryParse(raw, out var parsed))
                    throw new ArgumentException($"Invalid integer for {arg}: '{raw}'.");
                holdSeconds = parsed;
            }
        }

        return new DemoCliOptions(pluginId, logDirectory, holdSeconds);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {option}.");

        index++;
        return args[index];
    }
}

internal sealed class BrokerlessDemoWorker(
    ILogger<BrokerlessDemoWorker> logger,
    ILogPipeWriter logPipeWriter,
    DemoRuntime runtime,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Brokerless demo started for {PluginId}", runtime.PluginId);
        logger.LogWarning("This warning is written through Microsoft.Extensions.Logging provider.");

        await logPipeWriter.WriteAsync(new LogMessage
        {
            Level = LogPipeLevel.Info,
            Message = "Direct write through ILogPipeWriter",
            Source = nameof(BrokerlessDemoWorker),
            Service = runtime.PluginId,
            Timestamp = DateTimeOffset.UtcNow,
        }, stoppingToken);

        await logPipeWriter.WriteAsync(new LogMessage
        {
            Level = LogPipeLevel.Error,
            Message = "Simulated business error stored without broker",
            Source = nameof(BrokerlessDemoWorker),
            Service = runtime.PluginId,
            Timestamp = DateTimeOffset.UtcNow,
        }, stoppingToken);

        if (runtime.HoldSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(runtime.HoldSeconds), stoppingToken);

        lifetime.StopApplication();
    }
}