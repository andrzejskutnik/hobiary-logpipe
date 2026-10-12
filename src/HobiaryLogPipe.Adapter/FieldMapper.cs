using System.Text.Json;
using HobiaryLogPipe.HobiaryLog;
using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter;

internal static class FieldMapper
{
    private static readonly Dictionary<string, HlogLevel> LevelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trace"] = HlogLevel.Trace,
        ["debug"] = HlogLevel.Debug,
        ["info"] = HlogLevel.Info,
        ["information"] = HlogLevel.Info,
        ["warn"] = HlogLevel.Warn,
        ["warning"] = HlogLevel.Warn,
        ["error"] = HlogLevel.Error,
        ["fatal"] = HlogLevel.Fatal,
        ["critical"] = HlogLevel.Fatal,
    };

    /// <summary>
    /// Mapuje surowy JSON (zgodnie z FieldMappingConfig) na HlogRecord.
    /// rawBody służy jako payload (przechowuje pełny JSON loga).
    /// </summary>
    public static HlogRecord Map(JsonElement root, FieldMappingConfig mapping, byte[] rawBody)
    {
        var level = ReadLevel(root, mapping.Level);
        var source = ReadString(root, mapping.Source);
        var service = ReadString(root, mapping.Service);
        long tsUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

        var traceId = ReadTraceId(root, mapping.TraceId);
        var corrId = ReadTraceId(root, mapping.CorrelationId); // ten sam format 32-char hex
        var spanId = ReadSpanId(root, mapping.SpanId);

        return new HlogRecord(level, tsUs, source, service, rawBody, traceId, spanId, corrId);
    }

    private static HlogLevel ReadLevel(JsonElement root, string fieldName)
    {
        if (root.TryGetProperty(fieldName, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var raw = el.GetString() ?? string.Empty;
            if (LevelMap.TryGetValue(raw, out var lvl)) return lvl;
        }
        return HlogLevel.Info;
    }

    private static string? ReadString(JsonElement root, string fieldName)
    {
        if (root.TryGetProperty(fieldName, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString();
        return null;
    }

    private static byte[]? ReadTraceId(JsonElement root, string fieldName)
    {
        if (!root.TryGetProperty(fieldName, out var el)) return null;
        if (el.ValueKind != JsonValueKind.String) return null;
        var hex = el.GetString();
        if (hex is null || hex.Length != 32) return null;
        try { return Convert.FromHexString(hex); }
        catch { return null; }
    }

    private static ulong ReadSpanId(JsonElement root, string fieldName)
    {
        if (!root.TryGetProperty(fieldName, out var el)) return 0;
        if (el.ValueKind == JsonValueKind.String)
        {
            var hex = el.GetString();
            if (hex is { Length: 16 })
                try { return BitConverter.ToUInt64(Convert.FromHexString(hex)); }
                catch { return 0; }
            if (ulong.TryParse(hex, out var parsed)) return parsed;
        }
        if (el.ValueKind == JsonValueKind.Number && el.TryGetUInt64(out var num)) return num;
        return 0;
    }
}
