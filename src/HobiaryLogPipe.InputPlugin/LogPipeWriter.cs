using HobiaryLogPipe.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.InputPlugin;

public sealed class LogPipeWriter : ILogPipeWriter, IHostedService, IAsyncDisposable
{
    private readonly ILogger<LogPipeWriter> _logger;
    private readonly ILogPipeTransport _transport;

    public LogPipeWriter(ILogger<LogPipeWriter> logger, IOptions<InputPluginOptions> options)
        : this(logger, options, static opts =>
            new NativeDirectLogClient(Path.Combine(opts.BaseLogDirectory, opts.PluginId)))
    {
    }

    internal LogPipeWriter(
        ILogger<LogPipeWriter> logger,
        IOptions<InputPluginOptions> options,
        Func<InputPluginOptions, IDirectLogClient> directClientFactory)
    {
        _logger = logger;
        _transport = options.Value.Transport == LogPipeTransport.Direct
            ? new DirectLogPipeTransport(logger, options.Value, directClientFactory)
            : new RabbitMqLogPipeTransport(logger, options.Value);
    }

    // ── IHostedService ────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken) => _transport.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => _transport.StopAsync(cancellationToken);

    // ── ILogPipeWriter ────────────────────────────────────────────────────

    public Task WriteAsync(LogMessage message, CancellationToken cancellationToken = default)
        => _transport.WriteAsync(message, cancellationToken);

    public const string GzipHeaderName = "x-hobiary-encoding";
    public const string GzipHeaderValue = "gzip";

    // ── IAsyncDisposable ──────────────────────────────────────────────────

    public async ValueTask DisposeAsync() => await _transport.DisposeAsync();
}
