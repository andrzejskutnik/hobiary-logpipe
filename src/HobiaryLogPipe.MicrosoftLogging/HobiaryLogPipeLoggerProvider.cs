using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging;

namespace HobiaryLogPipe.MicrosoftLogging;

/// <summary>
/// ILoggerProvider zapisujący logi przez HobiaryLogPipe (RabbitMQ → HobiaryLogDB).
/// </summary>
public sealed class HobiaryLogPipeLoggerProvider : ILoggerProvider
{
    private readonly ILogPipeWriter _writer;

    public HobiaryLogPipeLoggerProvider(ILogPipeWriter writer)
    {
        _writer = writer;
    }

    public ILogger CreateLogger(string categoryName)
        => new HobiaryLogPipeLogger(categoryName, _writer);

    public void Dispose() { }
}
