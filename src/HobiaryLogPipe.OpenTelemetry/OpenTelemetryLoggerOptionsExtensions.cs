using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace HobiaryLogPipe.OpenTelemetry;

public static class OpenTelemetryLoggerOptionsExtensions
{
    /// <summary>
    /// Dodaje HobiaryLogPipe jako exporter do OpenTelemetry logging pipeline.
    /// </summary>
    /// <example>
    /// builder.Logging.AddOpenTelemetry(options =>
    ///     options.AddHobiaryLogPipeExporter(opts =>
    ///     {
    ///         opts.PluginId = "my-service";
    ///         opts.Host = "rabbitmq.local";
    ///     }));
    /// </example>
    public static OpenTelemetryLoggerOptions AddHobiaryLogPipeExporter(
        this OpenTelemetryLoggerOptions options,
        Action<InputPluginOptions> configure)
    {
        var opts = new InputPluginOptions();
        configure(opts);

        var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            Options.Create(opts));

        writer.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        return options.AddProcessor(new SimpleLogRecordExportProcessor(new HobiaryLogPipeExporter(writer)));
    }
}
