using System.Text.Json;
using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class SchemaValidatorTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ── Brak konfiguracji ─────────────────────────────────────────────────

    [Fact]
    public void NoConfig_ReturnsNull()
    {
        var result = SchemaValidator.FindMissingField(
            Parse("""{"level":"Info"}"""),
            "orders",
            new Dictionary<string, string[]>());
        Assert.Null(result);
    }

    [Fact]
    public void EmptyRequiredArray_ReturnsNull()
    {
        var result = SchemaValidator.FindMissingField(
            Parse("""{"level":"Info"}"""),
            "orders",
            new Dictionary<string, string[]> { ["orders"] = [] });
        Assert.Null(result);
    }

    // ── Wszystkie pola obecne ─────────────────────────────────────────────

    [Fact]
    public void AllFieldsPresent_ReturnsNull()
    {
        var result = SchemaValidator.FindMissingField(
            Parse("""{"level":"Info","orderId":"123","service":"orders"}"""),
            "orders",
            new Dictionary<string, string[]> { ["orders"] = ["level", "orderId"] });
        Assert.Null(result);
    }

    // ── Brakujące pola ────────────────────────────────────────────────────

    [Fact]
    public void MissingOneField_ReturnsThatField()
    {
        var result = SchemaValidator.FindMissingField(
            Parse("""{"level":"Info"}"""),
            "orders",
            new Dictionary<string, string[]> { ["orders"] = ["level", "orderId"] });
        Assert.Equal("orderId", result);
    }

    [Fact]
    public void AllFieldsMissing_ReturnsFirst()
    {
        var result = SchemaValidator.FindMissingField(
            Parse("""{}"""),
            "orders",
            new Dictionary<string, string[]> { ["orders"] = ["level", "orderId", "service"] });
        Assert.Equal("level", result);
    }

    // ── Inny plugin — konfiguracja nie dotyczy ────────────────────────────

    [Fact]
    public void OtherPlugin_ReturnsNull()
    {
        var result = SchemaValidator.FindMissingField(
            Parse("""{}"""),
            "auth",
            new Dictionary<string, string[]> { ["orders"] = ["orderId"] });
        Assert.Null(result);
    }

    // ── Case-sensitive nazwy pól ──────────────────────────────────────────

    [Fact]
    public void FieldNameCaseSensitive_MismatchReturnsMissing()
    {
        var result = SchemaValidator.FindMissingField(
            Parse("""{"Level":"Info"}"""),
            "orders",
            new Dictionary<string, string[]> { ["orders"] = ["level"] });
        // JSON property lookup jest case-sensitive
        Assert.Equal("level", result);
    }

    // ── AdapterOptions ────────────────────────────────────────────────────

    [Fact]
    public void AdapterOptions_RequiredFields_DefaultEmpty()
    {
        var opts = new AdapterOptions();
        Assert.Empty(opts.PluginRequiredFields);
    }

    [Fact]
    public void AdapterOptions_RequiredFields_CustomEntry()
    {
        var opts = new AdapterOptions
        {
            PluginRequiredFields = new Dictionary<string, string[]>
            {
                ["orders"] = ["level", "orderId"],
            }
        };
        Assert.Equal(["level", "orderId"], opts.PluginRequiredFields["orders"]);
    }

    // ── PipeMetrics ───────────────────────────────────────────────────────

    [Fact]
    public void PipeMetrics_SchemaInvalid_InitiallyZero()
    {
        var m = new PipeMetrics();
        Assert.Equal(0, m.GetSnapshot().MessagesSchemaInvalid);
    }

    [Fact]
    public void PipeMetrics_IncrementSchemaInvalid()
    {
        var m = new PipeMetrics();
        m.IncrementSchemaInvalid();
        m.IncrementSchemaInvalid();
        Assert.Equal(2, m.GetSnapshot().MessagesSchemaInvalid);
    }
}
