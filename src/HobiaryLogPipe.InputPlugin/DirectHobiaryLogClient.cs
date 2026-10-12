using System.Runtime.InteropServices;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.HobiaryLog;

namespace HobiaryLogPipe.InputPlugin;

internal enum DirectHlogLevel : byte
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5,
}

internal sealed record DirectHlogRecord(
    DirectHlogLevel Level,
    long Timestamp,
    string? Source = null,
    string? Service = null,
    byte[]? Payload = null,
    byte[]? TraceId = null,
    ulong SpanId = 0,
    byte[]? CorrelationId = null);

internal interface IDirectLogClient : IAsyncDisposable
{
    void Append(DirectHlogRecord record);
}

internal sealed class NativeDirectLogClient : IDirectLogClient
{
    private readonly string _directoryPath;
    private IntPtr _log;
    private bool _disposed;

    public NativeDirectLogClient(string dir, uint maxSegmentBytes = 0)
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

    public void Append(DirectHlogRecord record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IntPtr srcPtr = record.Source != null ? Marshal.StringToHGlobalAnsi(record.Source) : IntPtr.Zero;
        IntPtr svcPtr = record.Service != null ? Marshal.StringToHGlobalAnsi(record.Service) : IntPtr.Zero;
        IntPtr payPtr = IntPtr.Zero;

        try
        {
            uint payLen = 0;
            if (record.Payload is { Length: > 0 })
            {
                payLen = (uint)Math.Min(record.Payload.Length, NativeHLog.HLOG_MAX_PAYLOAD);
                payPtr = Marshal.AllocHGlobal((int)payLen);
                Marshal.Copy(record.Payload, 0, payPtr, (int)payLen);
            }

            long nowUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

            var input = new NativeHLog.HlogRecordInput
            {
                level = (byte)record.Level,
                flags = 0,
                timestamp = record.Timestamp,
                ingested_at = nowUs,
                source = srcPtr,
                service = svcPtr,
                operation = IntPtr.Zero,
                entity_id = IntPtr.Zero,
                span_id = record.SpanId,
                trace_id = record.TraceId is { Length: 16 } ? record.TraceId : new byte[16],
                correlation_id = record.CorrelationId is { Length: 16 } ? record.CorrelationId : new byte[16],
                payload = payPtr,
                payload_len = payLen,
            };

            var result = NativeHLogFacade.hlog_append(_log, ref input);
            if (result != NativeHLog.HLOG_OK)
                throw new InvalidOperationException(HobiaryLogDiagnostics.BuildAppendFailureMessage(result, _directoryPath, payLen));
        }
        finally
        {
            if (srcPtr != IntPtr.Zero) Marshal.FreeHGlobal(srcPtr);
            if (svcPtr != IntPtr.Zero) Marshal.FreeHGlobal(svcPtr);
            if (payPtr != IntPtr.Zero) Marshal.FreeHGlobal(payPtr);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        if (_log != IntPtr.Zero)
        {
            NativeHLogFacade.hlog_close(_log);
            _log = IntPtr.Zero;
        }
        return ValueTask.CompletedTask;
    }
}

internal static class DirectHlogLevelMapper
{
    public static DirectHlogLevel Map(LogPipeLevel level) => level switch
    {
        LogPipeLevel.Trace => DirectHlogLevel.Trace,
        LogPipeLevel.Debug => DirectHlogLevel.Debug,
        LogPipeLevel.Info => DirectHlogLevel.Info,
        LogPipeLevel.Warn => DirectHlogLevel.Warn,
        LogPipeLevel.Error => DirectHlogLevel.Error,
        LogPipeLevel.Fatal => DirectHlogLevel.Fatal,
        _ => DirectHlogLevel.Info,
    };
}

internal static class DirectIdParser
{
    public static byte[]? ParseTraceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 32)
            return null;

        try { return Convert.FromHexString(value); }
        catch { return null; }
    }

    public static byte[]? ParseCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Length == 32)
        {
            try { return Convert.FromHexString(value); }
            catch { }
        }

        if (Guid.TryParse(value, out var guid))
            return guid.ToByteArray();

        return null;
    }
}

internal static class NativeHLog
{
    public const int HLOG_OK = 0;
    public const uint HLOG_MAX_PAYLOAD = 65536u;

    [StructLayout(LayoutKind.Sequential)]
    public struct HlogRecordInput
    {
        public byte level;
        public ushort flags;
        public long timestamp;
        public long ingested_at;
        public IntPtr source;
        public IntPtr service;
        public IntPtr operation;
        public IntPtr entity_id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] trace_id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] correlation_id;
        public ulong span_id;
        public IntPtr payload;
        public uint payload_len;
    }
}

internal static class NativeHLogFacade
{
    private const string Dll = "hobiarylogdb";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr hlog_open(string dir, uint maxSegmentBytes);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hlog_close(IntPtr log);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hlog_append(IntPtr log, ref NativeHLog.HlogRecordInput rec);
}