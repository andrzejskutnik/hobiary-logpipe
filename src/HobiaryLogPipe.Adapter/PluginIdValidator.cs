using System.Text.RegularExpressions;

namespace HobiaryLogPipe.Adapter;

internal static partial class PluginIdValidator
{
    // [a-zA-Z0-9_.-]{1,64}
    [GeneratedRegex(@"^[a-zA-Z0-9_.\-]{1,64}$", RegexOptions.Compiled)]
    private static partial Regex PluginIdRegex();

    public static bool IsValid(string pluginId)
        => !string.IsNullOrEmpty(pluginId) && PluginIdRegex().IsMatch(pluginId);

    public static bool IsMappingKeyValid(string key)
        => !string.IsNullOrEmpty(key) && PluginIdRegex().IsMatch(key);
}
