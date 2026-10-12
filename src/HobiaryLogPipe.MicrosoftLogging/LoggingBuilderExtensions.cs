using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.MicrosoftLogging;

public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Dodaje HobiaryLogPipe jako provider logowania Microsoft.Extensions.Logging.
    /// </summary>
    /// <example>
    /// builder.Logging.AddHobiaryLogPipe(opts =>
    /// {
    ///     opts.PluginId = "my-service";
    ///     opts.Host = "rabbitmq.local";
    /// });
    /// </example>
    public static ILoggingBuilder AddHobiaryLogPipe(
        this ILoggingBuilder builder,
        Action<InputPluginOptions> configure)
    {
        var opts = new InputPluginOptions();
        configure(opts);

        var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            Options.Create(opts));

        writer.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        builder.Services.AddSingleton<ILoggerProvider>(new HobiaryLogPipeLoggerProvider(writer));
        return builder;
    }
}
