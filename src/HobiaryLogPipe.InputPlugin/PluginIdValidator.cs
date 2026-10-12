using System.Text.RegularExpressions;

namespace HobiaryLogPipe.InputPlugin;

internal static partial class PluginIdValidator
{
    [GeneratedRegex(@"^[a-zA-Z0-9_.\-]{1,64}$", RegexOptions.Compiled)]
    private static partial Regex PluginIdRegex();

    public static bool IsValid(string pluginId)
        => !string.IsNullOrEmpty(pluginId)
           && pluginId is not "." and not ".."
           && PluginIdRegex().IsMatch(pluginId);
}