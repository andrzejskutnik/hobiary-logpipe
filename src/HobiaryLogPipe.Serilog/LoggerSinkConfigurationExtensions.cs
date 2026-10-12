using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Configuration;

namespace HobiaryLogPipe.Serilog;

public static class LoggerSinkConfigurationExtensions
{
    /// <summary>
    /// Dodaje sink HobiaryLogPipe do konfiguracji Serilog.
    /// </summary>
    /// <example>
    /// Log.Logger = new LoggerConfiguration()
    ///     .WriteTo.HobiaryLogPipe(opts =>
    ///     {
    ///         opts.PluginId = "my-app";
    ///         opts.Host = "rabbitmq.local";
    ///     })
    ///     .CreateLogger();
    /// </example>
    public static LoggerConfiguration HobiaryLogPipe(
        this LoggerSinkConfiguration sink,
        Action<InputPluginOptions> configure)
    {
        var opts = new InputPluginOptions();
        configure(opts);

        var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            Options.Create(opts));

        // StartAsync nie może być tu await-owane — uruchamiamy synchronicznie
        writer.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        return sink.Sink(new HobiaryLogPipeSink(writer));
    }
}
