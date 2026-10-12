using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class DashboardApiTests
{
    [Fact]
    public void BuildDashboardHtml_ContainsMainSections()
    {
        var html = DashboardApi.BuildDashboardHtml();

        Assert.Contains("Hobiary LogPipe Dashboard", html);
        Assert.Contains("Embedded dashboard", html);
        Assert.Contains("Plugins", html);
        Assert.Contains("Level distribution", html);
        Assert.Contains("Historical logs", html);
        Assert.Contains("Live feed", html);
    }

    [Fact]
    public void BuildDashboardHtml_ReferencesExistingApiEndpoints()
    {
        var html = DashboardApi.BuildDashboardHtml();

        Assert.Contains("/api/plugins", html);
        Assert.Contains("/api/logs", html);
        Assert.Contains("/api/logs/stream", html);
        Assert.Contains("/api/plugins/${pluginId}/stats", html);
        Assert.Contains("/api/plugins/${pluginId}/logs/export", html);
        Assert.Contains("paged", html);
        Assert.Contains("cursor", html);
        Assert.Contains("cursorStack", html);
    }

    [Fact]
    public void BuildDashboardHtml_UsesEventSource_ForLiveUpdates()
    {
        var html = DashboardApi.BuildDashboardHtml();

        Assert.Contains("new EventSource(withApiKey(url))", html);
        Assert.Contains("Live stream reconnecting (", html);
        Assert.Contains("streamReconnects", html);
        Assert.Contains("traceId", html);
        Assert.Contains("correlationId", html);
        Assert.Contains("apiKey", html);
    }

    [Fact]
    public void BuildDashboardHtml_ContainsTimeRange_AndExportControls()
    {
        var html = DashboardApi.BuildDashboardHtml();

        Assert.Contains("id=\"fromFilter\"", html);
        Assert.Contains("id=\"toFilter\"", html);
        Assert.Contains("id=\"exportLogsButton\"", html);
        Assert.Contains("id=\"clearLiveButton\"", html);
        Assert.Contains("window.location.assign(withApiKey(`/api/plugins/${pluginId}/logs/export?${params.toString()}`));", html);
        Assert.Contains("id=\"prevPageButton\"", html);
        Assert.Contains("id=\"nextPageButton\"", html);
    }
}