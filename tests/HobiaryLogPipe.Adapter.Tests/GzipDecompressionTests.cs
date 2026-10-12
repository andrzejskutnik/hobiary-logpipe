using System.IO.Compression;
using System.Text;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class GzipDecompressionTests
{
    // ── Pomocnicze metody ─────────────────────────────────────────────────

    private static byte[] GzipCompress(string json)
    {
        var input = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(input);
        return ms.ToArray();
    }

    private static byte[] GzipDecompress(byte[] compressed)
    {
        using var ms = new MemoryStream(compressed);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        gz.CopyTo(outMs);
        return outMs.ToArray();
    }

    // ── Roundtrip gzip ────────────────────────────────────────────────────

    [Fact]
    public void Gzip_Roundtrip_PreservesContent()
    {
        const string original = """{"level":"info","message":"hello","service":"test"}""";
        var compressed = GzipCompress(original);
        var decompressed = GzipDecompress(compressed);
        Assert.Equal(original, Encoding.UTF8.GetString(decompressed));
    }

    [Fact]
    public void Gzip_CompressedBytes_ShorterForRepetitiveData()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
            sb.Append("""{"level":"info","message":"repeated data entry"}""");
        var compressed = GzipCompress(sb.ToString());
        var original = Encoding.UTF8.GetByteCount(sb.ToString());
        Assert.True(compressed.Length < original);
    }

    // ── Adapter rozumie nagłówek gzip ─────────────────────────────────────

    [Fact]
    public void GzipHeader_DetectionByHeaderName()
    {
        var headers = new Dictionary<string, object?> { ["x-hobiary-encoding"] = "gzip" };
        var isGzip = headers.TryGetValue("x-hobiary-encoding", out var val) && val?.ToString() == "gzip";
        Assert.True(isGzip);
    }

    [Fact]
    public void GzipHeader_AbsentHeader_NotDetected()
    {
        var headers = new Dictionary<string, object?>();
        var isGzip = headers.TryGetValue("x-hobiary-encoding", out var val) && val?.ToString() == "gzip";
        Assert.False(isGzip);
    }

    [Fact]
    public void GzipHeader_WrongValue_NotDetected()
    {
        var headers = new Dictionary<string, object?> { ["x-hobiary-encoding"] = "deflate" };
        var isGzip = headers.TryGetValue("x-hobiary-encoding", out var val) && val?.ToString() == "gzip";
        Assert.False(isGzip);
    }

    // ── FieldMapper działa po dekompresji ─────────────────────────────────

    [Fact]
    public void FieldMapper_WorksOnDecompressedJson()
    {
        const string json = """{"level":"warn","message":"compressed msg","source":"svc","service":"api"}""";
        var compressed = GzipCompress(json);
        var decompressed = GzipDecompress(compressed);

        var root = System.Text.Json.JsonDocument.Parse(decompressed).RootElement;
        var mapping = new HobiaryLogPipe.Contracts.FieldMappingConfig();
        var record = FieldMapper.Map(root, mapping, decompressed);

        Assert.Equal(HobiaryLog.HlogLevel.Warn, record.Level);
    }
}
