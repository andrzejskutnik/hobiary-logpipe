using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var transportRaw = Environment.GetEnvironmentVariable("HOBIARY_TRANSPORT");
var transport = Enum.TryParse<LogPipeTransport>(transportRaw, ignoreCase: true, out var parsedTransport)
    ? parsedTransport
    : LogPipeTransport.RabbitMq;
var pluginId = Environment.GetEnvironmentVariable("HOBIARY_PLUGIN_ID") ?? "orders-service";
var host = Environment.GetEnvironmentVariable("HOBIARY_RABBIT_HOST") ?? "localhost";
var port = int.TryParse(Environment.GetEnvironmentVariable("HOBIARY_RABBIT_PORT"), out var parsedPort)
    ? parsedPort
    : 5672;
var username = Environment.GetEnvironmentVariable("HOBIARY_RABBIT_USER") ?? "guest";
var password = Environment.GetEnvironmentVariable("HOBIARY_RABBIT_PASSWORD") ?? "guest";
var virtualHost = Environment.GetEnvironmentVariable("HOBIARY_RABBIT_VHOST") ?? "/";
var holdSeconds = int.TryParse(Environment.GetEnvironmentVariable("HOBIARY_HOLD_SECONDS"), out var parsedHoldSeconds)
    ? parsedHoldSeconds
    : 15;
var announceDelayMs = int.TryParse(Environment.GetEnvironmentVariable("HOBIARY_ANNOUNCE_DELAY_MS"), out var parsedAnnounceDelayMs)
    ? parsedAnnounceDelayMs
    : 1000;
var directLogDirectory = Environment.GetEnvironmentVariable("HOBIARY_DIRECT_LOG_DIR") ?? "logs-direct";

var options = Options.Create(new InputPluginOptions
{
    Transport = transport,
    PluginId = pluginId,
    BaseLogDirectory = directLogDirectory,
    Host = host,
    Port = port,
    Username = username,
    Password = password,
    VirtualHost = virtualHost,
});

await using var writer = new LogPipeWriter(NullLogger<LogPipeWriter>.Instance, options);

await writer.StartAsync(CancellationToken.None);

if (transport == LogPipeTransport.RabbitMq && announceDelayMs > 0)
    await Task.Delay(announceDelayMs);

await writer.WriteAsync(new LogMessage
{
    Level = LogPipeLevel.Info,
    Message = $"Smoke publisher test log at {DateTimeOffset.UtcNow:O}",
    Source = "SmokePublisher",
    Service = pluginId,
});

if (transport == LogPipeTransport.RabbitMq && holdSeconds > 0)
    await Task.Delay(TimeSpan.FromSeconds(holdSeconds));

await writer.StopAsync(CancellationToken.None);

Console.WriteLine(transport == LogPipeTransport.Direct
    ? $"Wrote direct log for '{pluginId}' to '{Path.Combine(directLogDirectory, pluginId)}'."
    : $"Published announce and log for '{pluginId}' to {host}:{port}. Waited {announceDelayMs}ms after announce and held registration for {holdSeconds}s.");