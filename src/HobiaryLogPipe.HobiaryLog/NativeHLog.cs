using System.Runtime.InteropServices;

namespace HobiaryLogPipe.HobiaryLog;

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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HlogRecordHeader
    {
        public byte magic;
        public uint record_len;
        public byte level;
        public ushort flags;
        public uint payload_len;
        public long timestamp;
        public long ingested_at;
        public ushort source_id;
        public ushort service_id;
        public ushort operation_id;
        public ushort entity_id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] trace_id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] correlation_id;
        public ulong span_id;
        public uint _padding;
    }
}

internal static class NativeHLogFacade
{
    private const string Dll = "hobiarylogdb";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int HlogVisitFn(IntPtr seg,
                                    ref NativeHLog.HlogRecordHeader hdr,
                                    IntPtr payload,
                                    IntPtr ctx);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr hlog_open(string dir, uint maxSegmentBytes);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hlog_close(IntPtr log);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hlog_append(IntPtr log, ref NativeHLog.HlogRecordInput rec);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint hlog_segment_count(IntPtr log);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong hlog_active_record_count(IntPtr log);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hlog_visit_range(IntPtr log, long fromTs, long toTs,
        HlogVisitFn fn, IntPtr ctx);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hlog_visit_level(IntPtr log, byte level,
        HlogVisitFn fn, IntPtr ctx);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr hlog_segment_str(IntPtr seg, ushort id);
}