using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HobiaryLogPipe.InputPlugin;

namespace HobiaryLogPipe.InputPlugin.Tests;

public sealed class GzipCompressionTests
{
    [Fact]
    public void InputPluginOptions_GzipEnabled_DefaultFalse()
    {
        var opts = new InputPluginOptions();
        Assert.False(opts.GzipEnabled);
    }

    [Fact]
    public void InputPluginOptions_GzipEnabled_CanBeSet()
    {
        var opts = new InputPluginOptions { GzipEnabled = true };
        Assert.True(opts.GzipEnabled);
    }

    [Fact]
    public void GzipCompress_ProducesDecompressableOutput()
    {
        var input = """{"level":"info","message":"hello gzip"}"""u8.ToArray();
        var compressed = GzipCompress(input);

        using var ms = new MemoryStream(compressed);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        gz.CopyTo(outMs);
        var decompressed = outMs.ToArray();

        Assert.Equal(input, decompressed);
    }

    [Fact]
    public void GzipCompress_SmallerThanOriginalForRepetitiveData()
    {
        // JSON z powtarzającą się treścią — gzip powinien zmniejszyć rozmiar
        var sb = new StringBuilder();
        for (int i = 0; i < 50; i++)
            sb.Append("""{"level":"info","message":"test log entry repeated data"}""");
        var input = Encoding.UTF8.GetBytes(sb.ToString());
        var compressed = GzipCompress(input);

        Assert.True(compressed.Length < input.Length,
            $"Compressed ({compressed.Length}B) should be smaller than original ({input.Length}B)");
    }

    [Fact]
    public void GzipHeaderName_IsCorrect()
    {
        Assert.Equal("x-hobiary-encoding", LogPipeWriter.GzipHeaderName);
    }

    [Fact]
    public void GzipHeaderValue_IsCorrect()
    {
        Assert.Equal("gzip", LogPipeWriter.GzipHeaderValue);
    }

    private static byte[] GzipCompress(byte[] input)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(input);
        return ms.ToArray();
    }
}
