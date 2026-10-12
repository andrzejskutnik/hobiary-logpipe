using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace HobiaryLogPipe.InputPlugin.Tests;

public sealed class LogPipeWriterTests
{
    private static LogPipeWriter CreateWriter(string pluginId = "test-plugin")
    {
        var opts = Options.Create(new InputPluginOptions { PluginId = pluginId });
        return new LogPipeWriter(NullLogger<LogPipeWriter>.Instance, opts);
    }

    [Fact]
    public async Task WriteAsync_BeforeStartAsync_ThrowsInvalidOperationException()
    {
        await using var writer = CreateWriter();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.WriteAsync(new LogMessage { Message = "test" }));
    }

    [Fact]
    public async Task StopAsync_WithoutStartAsync_DoesNotThrow()
    {
        await using var writer = CreateWriter();

        // StopAsync na niezainicjalizowanym połączeniu nie powinno rzucić wyjątku
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DisposeAsync_WithoutStartAsync_DoesNotThrow()
    {
        var writer = CreateWriter();

        // Dispose bez połączenia — null-safe
        await writer.DisposeAsync();
    }

    [Fact]
    public async Task DirectTransport_StartsAndWrites_WithoutBroker()
    {
        var fakeClient = new FakeDirectLogClient();
        var opts = Options.Create(new InputPluginOptions
        {
            PluginId = "direct-plugin",
            Transport = LogPipeTransport.Direct,
            BaseLogDirectory = "logs-test"
        });

        await using var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            opts,
            _ => fakeClient);

        await writer.StartAsync(CancellationToken.None);
        await writer.WriteAsync(new LogMessage
        {
            Level = LogPipeLevel.Error,
            Message = "brokerless",
            Source = "orders-api",
            Service = "orders"
        });

        var record = Assert.Single(fakeClient.Records);
        Assert.Equal(DirectHlogLevel.Error, record.Level);
        Assert.Equal("orders-api", record.Source);
        Assert.Equal("orders", record.Service);
        Assert.Contains("brokerless", Encoding.UTF8.GetString(record.Payload!));
    }

    [Fact]
    public async Task DirectTransport_InvalidPluginId_ThrowsOnStart()
    {
        var opts = Options.Create(new InputPluginOptions
        {
            PluginId = "bad plugin!",
            Transport = LogPipeTransport.Direct,
        });

        await using var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            opts,
            _ => new FakeDirectLogClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task DirectTransport_DotPluginId_ThrowsOnStart(string pluginId)
    {
        var opts = Options.Create(new InputPluginOptions
        {
            PluginId = pluginId,
            Transport = LogPipeTransport.Direct,
        });

        await using var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            opts,
            _ => new FakeDirectLogClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DirectTransport_EncryptionEnabled_ThrowsOnStart()
    {
        var opts = Options.Create(new InputPluginOptions
        {
            PluginId = "direct-plugin",
            Transport = LogPipeTransport.Direct,
            PayloadEncryption = new PayloadEncryptionOptions { Enabled = true },
        });

        await using var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            opts,
            _ => new FakeDirectLogClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync(CancellationToken.None));
        Assert.Contains("PayloadEncryption", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectTransport_GzipEnabled_ThrowsOnStart()
    {
        var opts = Options.Create(new InputPluginOptions
        {
            PluginId = "direct-plugin",
            Transport = LogPipeTransport.Direct,
            GzipEnabled = true,
        });

        await using var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            opts,
            _ => new FakeDirectLogClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync(CancellationToken.None));
        Assert.Contains("GzipEnabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectTransport_InvalidBaseLogDirectory_ThrowsDetailedDiagnosticMessage()
    {
        var invalidChar = Path.GetInvalidPathChars()[0];
        var opts = Options.Create(new InputPluginOptions
        {
            PluginId = "direct-plugin",
            Transport = LogPipeTransport.Direct,
            BaseLogDirectory = Path.Combine(Path.GetTempPath(), $"logs_{invalidChar}_{Guid.NewGuid()}")
        });

        await using var writer = new LogPipeWriter(
            NullLogger<LogPipeWriter>.Instance,
            opts);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync(CancellationToken.None));
        Assert.Contains("Nie można przygotować katalogu logu", ex.Message);
        Assert.Contains("Sprawdź poprawność ścieżki", ex.Message);
    }

    private sealed class FakeDirectLogClient : IDirectLogClient
    {
        public List<DirectHlogRecord> Records { get; } = [];

        public void Append(DirectHlogRecord record) => Records.Add(record);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
