using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class LevelFilterTests
{
    [Fact]
    public void PluginMinLevels_Default_Is_Empty()
    {
        var opts = new AdapterOptions();
        Assert.Empty(opts.PluginMinLevels);
    }

    [Fact]
    public void PluginMinLevels_Custom_Entry()
    {
        var opts = new AdapterOptions
        {
            PluginMinLevels = new Dictionary<string, string>
            {
                ["orders-service"] = "Warn",
                ["auth-service"]   = "Error",
            }
        };
        Assert.Equal("Warn",  opts.PluginMinLevels["orders-service"]);
        Assert.Equal("Error", opts.PluginMinLevels["auth-service"]);
    }

    [Fact]
    public void PipeMetrics_Filtered_Initially_Zero()
    {
        var m = new PipeMetrics();
        Assert.Equal(0, m.GetSnapshot().MessagesFiltered);
    }

    [Fact]
    public void PipeMetrics_IncrementFiltered()
    {
        var m = new PipeMetrics();
        m.IncrementFiltered();
        m.IncrementFiltered();
        Assert.Equal(2, m.GetSnapshot().MessagesFiltered);
    }
}
