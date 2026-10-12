using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var pluginId = Environment.GetEnvironmentVariable("HOBIARY_PLUGIN_ID") ?? "orders-service";
var baseLogDirectory = Environment.GetEnvironmentVariable("HOBIARY_DIRECT_LOG_DIR") ?? "logs-direct";
var message = Environment.GetEnvironmentVariable("HOBIARY_DIRECT_MESSAGE")
    ?? $"Direct publisher test log at {DateTimeOffset.UtcNow:O}";

var options = Options.Create(new InputPluginOptions
{
    Transport = LogPipeTransport.Direct,
    PluginId = pluginId,
    BaseLogDirectory = baseLogDirectory,
});

await using var writer = new LogPipeWriter(NullLogger<LogPipeWriter>.Instance, options);

await writer.StartAsync(CancellationToken.None);
await writer.WriteAsync(new LogMessage
{
    Level = LogPipeLevel.Info,
    Message = message,
    Source = "DirectPublisher",
    Service = pluginId,
});
await writer.StopAsync(CancellationToken.None);

Console.WriteLine($"Wrote direct log for '{pluginId}' to '{Path.Combine(baseLogDirectory, pluginId)}'.");