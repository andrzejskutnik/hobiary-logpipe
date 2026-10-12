using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.Contracts;
using System.Text.Json;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class LogQueryApiTests
{
    private static PluginRegistry BuildRegistry(params string[] pluginIds)
    {
        var registry = new PluginRegistry();
        foreach (var id in pluginIds)
        {
            // Rejestrujemy stub bez prawdziwego klienta — testujemy tylko API endpoint logic
            var reg = new PluginRegistration
            {
                PluginId = id,
                FieldMapping = new FieldMappingConfig(),
                MessageFormat = "json",
                RegisteredAt = DateTimeOffset.UtcNow,
            };
            registry.Register(reg);
        }
        return registry;
    }

    [Fact]
    public void All_Returns_All_Registered_Plugins()
    {
        var registry = BuildRegistry("orders-svc", "auth-svc");
        var plugins = registry.All.ToList();
        Assert.Equal(2, plugins.Count);
        Assert.Contains(plugins, p => p.PluginId == "orders-svc");
        Assert.Contains(plugins, p => p.PluginId == "auth-svc");
    }

    [Fact]
    public void TryGet_ReturnsPlugin_When_Present()
    {
        var registry = BuildRegistry("my-plugin");
        var found = registry.TryGet("my-plugin", out var reg);
        Assert.True(found);
        Assert.NotNull(reg);
        Assert.Equal("my-plugin", reg!.PluginId);
    }

    [Fact]
    public void TryGet_ReturnsFalse_When_Missing()
    {
        var registry = BuildRegistry("plugin-a");
        var found = registry.TryGet("plugin-z", out var reg);
        Assert.False(found);
        Assert.Null(reg);
    }

    [Fact]
    public void PipeMetrics_Snapshot_Counters_AreConsistent()
    {
        var metrics = new PipeMetrics();
        metrics.IncrementReceived();
        metrics.IncrementReceived();
        metrics.IncrementWritten();
        metrics.IncrementRejected();
        metrics.IncrementDlq();

        var snap = metrics.GetSnapshot();
        Assert.Equal(2L, snap.MessagesReceived);
        Assert.Equal(1L, snap.MessagesWritten);
        Assert.Equal(1L, snap.MessagesRejected);
        Assert.Equal(1L, snap.DlqMessages);
        Assert.Equal(0L, snap.Reconnects);
    }

    [Fact]
    public void LogQueryPaging_FirstPage_ReturnsNextCursor()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(i => new LogEntry("info", i, $"src-{i}", null, null))
            .ToArray();

        var page = LogQueryPaging.PageByCursor(entries, cursor: null, limit: 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(1, page.NextCursor);
        Assert.Null(page.Cursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void LogQueryPaging_WithCursor_ReturnsNextCursor()
    {
        var entries = Enumerable.Range(10, 3)
            .Select(i => new LogEntry("info", i, null, null, null))
            .ToArray();

        var page = LogQueryPaging.PageByCursor(entries, cursor: 9, limit: 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(11, page.NextCursor);
        Assert.Equal(9, page.Cursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void LogQueryPaging_EmptyResult_ReturnsEmptyPage()
    {
        var entries = Array.Empty<LogEntry>();

        var page = LogQueryPaging.PageByCursor(entries, cursor: 42, limit: 2);

        Assert.Empty(page.Items);
        Assert.Equal(42, page.Cursor);
        Assert.Null(page.NextCursor);
        Assert.False(page.HasMore);
    }
}
