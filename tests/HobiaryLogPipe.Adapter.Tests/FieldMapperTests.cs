using System.Text;
using System.Text.Json;
using HobiaryLogPipe.HobiaryLog;
using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class FieldMapperTests
{
    private static readonly FieldMappingConfig DefaultMapping = new();
    private static readonly byte[] EmptyPayload = Array.Empty<byte>();

    private static JsonElement ParseJson(string json)
        => JsonDocument.Parse(json).RootElement;

    // ── Parsowanie poziomów ────────────────────────────────────────────────

    [Theory]
    [InlineData("trace", HlogLevel.Trace)]
    [InlineData("debug", HlogLevel.Debug)]
    [InlineData("info", HlogLevel.Info)]
    [InlineData("information", HlogLevel.Info)]
    [InlineData("warn", HlogLevel.Warn)]
    [InlineData("warning", HlogLevel.Warn)]
    [InlineData("error", HlogLevel.Error)]
    [InlineData("fatal", HlogLevel.Fatal)]
    [InlineData("critical", HlogLevel.Fatal)]
    [InlineData("TRACE", HlogLevel.Trace)]
    [InlineData("ERROR", HlogLevel.Error)]
    public void Map_KnownLevel_MapsCorrectly(string levelStr, HlogLevel expected)
    {
        var root = ParseJson($"{{\"level\":\"{levelStr}\",\"message\":\"msg\"}}");

        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);

        Assert.Equal(expected, record.Level);
    }

    [Fact]
    public void Map_UnknownLevel_DefaultsToInfo()
    {
        var root = ParseJson("{\"level\":\"verbose\",\"message\":\"msg\"}");

        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);

        Assert.Equal(HlogLevel.Info, record.Level);
    }

    [Fact]
    public void Map_MissingLevel_DefaultsToInfo()
    {
        var root = ParseJson("{\"message\":\"msg\"}");

        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);

        Assert.Equal(HlogLevel.Info, record.Level);
    }

    // ── Mapowanie source/service ───────────────────────────────────────────

    [Fact]
    public void Map_SourceAndService_ReadFromJson()
    {
        var root = ParseJson("{\"level\":\"info\",\"source\":\"MyClass\",\"service\":\"api\"}");

        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);

        Assert.Equal("MyClass", record.Source);
        Assert.Equal("api", record.Service);
    }

    [Fact]
    public void Map_MissingSourceAndService_AreNull()
    {
        var root = ParseJson("{\"level\":\"info\"}");

        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);

        Assert.Null(record.Source);
        Assert.Null(record.Service);
    }

    // ── Custom fieldMapping ────────────────────────────────────────────────

    [Fact]
    public void Map_CustomFieldMapping_ReadsCustomKeys()
    {
        var mapping = new FieldMappingConfig
        {
            Level = "severity",
            Message = "body",
            Source = "serviceName",
            Service = "appId",
        };

        var root = ParseJson("{\"severity\":\"error\",\"body\":\"boom\",\"serviceName\":\"DB\",\"appId\":\"orders\"}");

        var record = FieldMapper.Map(root, mapping, EmptyPayload);

        Assert.Equal(HlogLevel.Error, record.Level);
        Assert.Equal("DB", record.Source);
        Assert.Equal("orders", record.Service);
    }

    // ── Payload ────────────────────────────────────────────────────────────

    [Fact]
    public void Map_Payload_IsPassedThrough()
    {
        var root = ParseJson("{\"level\":\"info\"}");
        var payload = Encoding.UTF8.GetBytes("{\"level\":\"info\"}");

        var record = FieldMapper.Map(root, DefaultMapping, payload);

        Assert.Equal(payload, record.Payload);
    }

    // ── TraceId / CorrelationId ────────────────────────────────────────────

    [Fact]
    public void Map_ValidTraceId_ParsedTo16Bytes()
    {
        var hex = "4bf92f3577b34da6a3ce929d0e0e4736";
        var root = ParseJson($"{{\"level\":\"info\",\"traceId\":\"{hex}\"}}");

        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);

        Assert.NotNull(record.TraceId);
        Assert.Equal(16, record.TraceId!.Length);
        Assert.Equal(hex, Convert.ToHexString(record.TraceId).ToLowerInvariant());
    }

    [Fact]
    public void Map_ValidCorrelationId_ParsedTo16Bytes()
    {
        var hex = "aabbccddeeff00112233445566778899";
        var root = ParseJson($"{{\"level\":\"info\",\"correlationId\":\"{hex}\"}}");

        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);

        Assert.NotNull(record.CorrelationId);
        Assert.Equal(16, record.CorrelationId!.Length);
        Assert.Equal(hex, Convert.ToHexString(record.CorrelationId).ToLowerInvariant());
    }

    [Fact]
    public void Map_MissingTraceId_IsNull()
    {
        var root = ParseJson("{\"level\":\"info\"}");
        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);
        Assert.Null(record.TraceId);
        Assert.Null(record.CorrelationId);
    }

    [Fact]
    public void Map_InvalidTraceId_IsNull()
    {
        var root = ParseJson("{\"level\":\"info\",\"traceId\":\"not-a-hex-string\"}");
        var record = FieldMapper.Map(root, DefaultMapping, EmptyPayload);
        Assert.Null(record.TraceId);
    }

    [Fact]
    public void Map_TraceId_WithCustomFieldName_ReadsCorrectly()
    {
        var hex = "4bf92f3577b34da6a3ce929d0e0e4736";
        var mapping = new FieldMappingConfig { TraceId = "tid", CorrelationId = "cid" };
        var root = ParseJson($"{{\"level\":\"info\",\"tid\":\"{hex}\"}}");

        var record = FieldMapper.Map(root, mapping, EmptyPayload);

        Assert.NotNull(record.TraceId);
        Assert.Equal(hex, Convert.ToHexString(record.TraceId!).ToLowerInvariant());
    }
}
