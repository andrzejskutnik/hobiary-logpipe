using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.InputPlugin;

internal interface ILogPipeTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task WriteAsync(LogMessage message, CancellationToken cancellationToken);
}