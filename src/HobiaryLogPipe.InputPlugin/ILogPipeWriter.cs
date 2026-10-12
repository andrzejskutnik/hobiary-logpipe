using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.InputPlugin;

public interface ILogPipeWriter
{
    Task WriteAsync(LogMessage message, CancellationToken cancellationToken = default);
}
