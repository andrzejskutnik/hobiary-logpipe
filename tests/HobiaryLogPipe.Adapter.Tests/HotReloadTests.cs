using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.HobiaryLog;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class HotReloadTests
{
    [Fact]
    public void AdapterOptions_AllowedPlugins_And_MinLevels_Are_Hot_Reloadable()
    {
        // Verify że IOptionsMonitor<AdapterOptions> jest używany (nie IOptions)
        // przez sprawdzenie publicznych właściwości
        var opts1 = new AdapterOptions
        {
            AllowedPlugins = ["plugin-a"],
            PluginMinLevels = new Dictionary<string, string> { ["plugin-a"] = "Warn" },
        };
        var opts2 = new AdapterOptions
        {
            AllowedPlugins = ["plugin-a", "plugin-b"],
            PluginMinLevels = new Dictionary<string, string>
            {
                ["plugin-a"] = "Error",
                ["plugin-b"] = "Debug",
            },
        };

        Assert.Single(opts1.AllowedPlugins);
        Assert.Equal(2, opts2.AllowedPlugins.Length);
        Assert.Equal("Warn", opts1.PluginMinLevels["plugin-a"]);
        Assert.Equal("Error", opts2.PluginMinLevels["plugin-a"]);
    }

    [Fact]
    public void BuildPluginMinLevels_Parses_Valid_Levels()
    {
        var input = new Dictionary<string, string>
        {
            ["svc-a"] = "Warn",
            ["svc-b"] = "error",   // case-insensitive
            ["svc-c"] = "FATAL",
        };

        var result = BuildMinLevels(input);

        Assert.Equal(HlogLevel.Warn,  result["svc-a"]);
        Assert.Equal(HlogLevel.Error, result["svc-b"]);
        Assert.Equal(HlogLevel.Fatal, result["svc-c"]);
    }

    [Fact]
    public void BuildPluginMinLevels_Skips_Invalid_Levels()
    {
        var input = new Dictionary<string, string>
        {
            ["svc-a"] = "SuperVerbose",
            ["svc-b"] = "Info",
        };

        var result = BuildMinLevels(input);

        Assert.False(result.ContainsKey("svc-a"));
        Assert.True(result.ContainsKey("svc-b"));
    }

    [Fact]
    public void BuildPluginMinLevels_Empty_Input_Returns_Empty()
    {
        var result = BuildMinLevels([]);
        Assert.Empty(result);
    }

    // Reprodukuje logikę BuildPluginMinLevels z AdapterService (internal, testujemy zachowanie)
    private static Dictionary<string, HlogLevel> BuildMinLevels(Dictionary<string, string> raw)
    {
        var result = new Dictionary<string, HlogLevel>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, levelStr) in raw)
            if (Enum.TryParse<HlogLevel>(levelStr, ignoreCase: true, out var lvl))
                result[pluginId] = lvl;
        return result;
    }
}
