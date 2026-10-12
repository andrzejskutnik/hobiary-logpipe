namespace HobiaryLogPipe.Adapter.Tests;

public sealed class PluginIdValidatorTests
{
    // ── IsValid ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("orders-service", true)]
    [InlineData("my_plugin.1", true)]
    [InlineData("abc", true)]
    [InlineData("A1", true)]
    [InlineData("a-b_c.d", true)]
    [InlineData("a", true)]
    // 64 znaki — dokładnie na granicy
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    // 65 znaków — za długie
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("a@b", false)]
    [InlineData("a!b", false)]
    public void IsValid_ReturnsExpected(string pluginId, bool expected)
        => Assert.Equal(expected, PluginIdValidator.IsValid(pluginId));

    // ── IsMappingKeyValid ──────────────────────────────────────────────────

    [Theory]
    [InlineData("level", true)]
    [InlineData("severity", true)]
    [InlineData("log_level", true)]
    [InlineData("", false)]
    [InlineData("level name", false)]
    [InlineData("lev/el", false)]
    public void IsMappingKeyValid_ReturnsExpected(string key, bool expected)
        => Assert.Equal(expected, PluginIdValidator.IsMappingKeyValid(key));
}
