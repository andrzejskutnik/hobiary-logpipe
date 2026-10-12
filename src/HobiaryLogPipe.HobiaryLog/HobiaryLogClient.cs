using System.Runtime.InteropServices;

namespace HobiaryLogPipe.HobiaryLog;

/// <summary>
/// Represents a log query record.
/// </summary>
public sealed record LogQueryRecord(
    HlogLevel Level,
    long TimestampUs,
    string? Source,
    string? Service,
    byte[]? Payload,
    string? TraceId = null,
    string? CorrelationId = null);

/// <summary>
/// Represents a log record.
/// </summary>
public enum HlogLevel : byte
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5,
}

/// <summary>
/// Represents a log record for appending to the log.
/// </summary>
public sealed record HlogRecord(
    HlogLevel Level,
    long Timestamp,
    string? Source = null,
    string? Service = null,
    byte[]? Payload = null,
    byte[]? TraceId = null,
    ulong SpanId = 0,
    byte[]? CorrelationId = null);

/// <summary>
/// Represents a client for interacting with the Hobiary log.
/// </summary>
public sealed class HobiaryLogClient : IDisposable
{
    /// <summary>
    /// The maximum size of a log payload in bytes.
    /// </summary>
    public const uint MaxPayloadBytes = NativeHLog.HLOG_MAX_PAYLOAD;

    private readonly string _directoryPath;
    private IntPtr _log;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HobiaryLogClient"/> class.
    /// </summary>
    /// <param name="dir">The directory path where the log is stored.</param>
    /// <param name="maxSegmentBytes">The maximum size of a log segment in bytes.</param>
    /// <exception cref="InvalidOperationException">Thrown if the log cannot be opened.</exception>
    /// <exception cref="ArgumentException">Thrown if the directory path is invalid.</exception>
    /// <exception cref="NotSupportedException">Thrown if the directory path is not supported.</exception>
    /// <exception cref="PathTooLongException">Thrown if the directory path is too long.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if access to the directory is denied.</exception>
    /// <exception cref="IOException">Thrown if an I/O error occurs while preparing the directory.</exception>
    public HobiaryLogClient(string dir, uint maxSegmentBytes = 0)
    {
        _directoryPath = dir;

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(HobiaryLogDiagnostics.BuildDirectoryPreparationFailureMessage(dir, ex), ex);
        }

        _log = NativeHLogFacade.hlog_open(dir, maxSegmentBytes);
        if (_log == IntPtr.Zero)
            throw new InvalidOperationException(HobiaryLogDiagnostics.BuildOpenFailureMessage(dir));
    }

    public static bool TryCreate(string dir,
        out HobiaryLogClient? client,
        out string? error)
    {
        try
        {
            client = new HobiaryLogClient(dir);
            error = null;
            return true;
        }
        catch (DllNotFoundException ex)
        {
            client = null;
            error = HobiaryLogDiagnostics.BuildNativeLibraryLoadFailureMessage(ex);
            return false;
        }
        catch (BadImageFormatException ex)
        {
            client = null;
            error = HobiaryLogDiagnostics.BuildNativeLibraryLoadFailureMessage(ex);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            client = null;
            error = ex.Message;
            return false;
        }
    }

    public ulong ActiveRecordCount => NativeHLogFacade.hlog_active_record_count(_log);

    public int Append(HlogRecord rec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IntPtr srcPtr = rec.Source != null ? Marshal.StringToHGlobalAnsi(rec.Source) : IntPtr.Zero;
        IntPtr svcPtr = rec.Service != null ? Marshal.StringToHGlobalAnsi(rec.Service) : IntPtr.Zero;
        IntPtr payPtr = IntPtr.Zero;

        try
        {
            uint payLen = 0;
            if (rec.Payload is { Length: > 0 })
            {
                payLen = (uint)Math.Min(rec.Payload.Length, NativeHLog.HLOG_MAX_PAYLOAD);
                payPtr = Marshal.AllocHGlobal((int)payLen);
                Marshal.Copy(rec.Payload, 0, payPtr, (int)payLen);
            }

            long nowUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

            var input = new NativeHLog.HlogRecordInput
            {
                level = (byte)rec.Level,
                flags = 0,
                timestamp = rec.Timestamp,
                ingested_at = nowUs,
                source = srcPtr,
                service = svcPtr,
                operation = IntPtr.Zero,
                entity_id = IntPtr.Zero,
                span_id = rec.SpanId,
                trace_id = rec.TraceId is { Length: 16 } ? rec.TraceId : new byte[16],
                correlation_id = rec.CorrelationId is { Length: 16 } ? rec.CorrelationId : new byte[16],
                payload = payPtr,
                payload_len = payLen,
            };

            var result = NativeHLogFacade.hlog_append(_log, ref input);
            if (result != NativeHLog.HLOG_OK)
                throw new InvalidOperationException(HobiaryLogDiagnostics.BuildAppendFailureMessage(result, _directoryPath, payLen));

            return result;
        }
        finally
        {
            if (srcPtr != IntPtr.Zero) Marshal.FreeHGlobal(srcPtr);
            if (svcPtr != IntPtr.Zero) Marshal.FreeHGlobal(svcPtr);
            if (payPtr != IntPtr.Zero) Marshal.FreeHGlobal(payPtr);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_log != IntPtr.Zero)
        {
            NativeHLogFacade.hlog_close(_log);
            _log = IntPtr.Zero;
        }
    }

    public List<LogQueryRecord> QueryRange(long fromUs, long toUs,
        HlogLevel? minLevel = null, int limit = 200)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var results = new List<LogQueryRecord>(Math.Min(limit, 64));

        NativeHLogFacade.HlogVisitFn visitor = (segPtr, ref hdr, payloadPtr, _) =>
        {
            if (results.Count >= limit) return 1;
            if (minLevel.HasValue && hdr.level < (byte)minLevel.Value) return 0;

            string? source = hdr.source_id != 0
                ? Marshal.PtrToStringAnsi(NativeHLogFacade.hlog_segment_str(segPtr, hdr.source_id))
                : null;
            string? service = hdr.service_id != 0
                ? Marshal.PtrToStringAnsi(NativeHLogFacade.hlog_segment_str(segPtr, hdr.service_id))
                : null;
            byte[]? payload = null;
            if (hdr.payload_len > 0 && payloadPtr != IntPtr.Zero)
            {
                payload = new byte[hdr.payload_len];
                Marshal.Copy(payloadPtr, payload, 0, (int)hdr.payload_len);
            }

            results.Add(new LogQueryRecord(
                (HlogLevel)hdr.level,
                hdr.timestamp,
                source,
                service,
                payload,
                TraceId: HexOrNull(hdr.trace_id),
                CorrelationId: HexOrNull(hdr.correlation_id)));
            return 0;
        };

        NativeHLogFacade.hlog_visit_range(_log, fromUs, toUs, visitor, IntPtr.Zero);
        return results;
    }

    private static string? HexOrNull(byte[]? bytes)
    {
        if (bytes is not { Length: 16 }) return null;
        foreach (var b in bytes) if (b != 0) return Convert.ToHexString(bytes).ToLowerInvariant();
        return null;
    }

    public long[] CountByLevel(long fromUs, long toUs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var counts = new long[6];

        NativeHLogFacade.HlogVisitFn visitor = (_, ref hdr, _, _) =>
        {
            var idx = hdr.level < 6 ? hdr.level : (byte)2;
            counts[idx]++;
            return 0;
        };

        NativeHLogFacade.hlog_visit_range(_log, fromUs, toUs, visitor, IntPtr.Zero);
        return counts;
    }
}