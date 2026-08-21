using System.Runtime.InteropServices;

namespace CHDSharpEncoder;

/// <summary>
/// P/Invoke wrapper around the real C zstd library (libzstd), used so the encoder emits zstd frames
/// that are byte-identical to MAME/chdman's <c>chd_zstd_compressor</c>.
///
/// chdman calls <c>ZSTD_initCStream(stream, ZSTD_maxCLevel())</c> followed by a single
/// <c>ZSTD_compressStream2(stream, &amp;output, &amp;input, ZSTD_e_end)</c> over the whole buffer.
/// <see cref="ZstdNative.CStream.Compress"/> mirrors that exactly.
///
/// ZstdSharp.Port (the managed <c>ZstdSharp</c> package) is a pure-C# reimplementation of zstd and
/// its frames differ from C zstd in the trailing-byte finalization, so it cannot achieve byte parity
/// for CD compound ('cdzs') hunks. This native binding is the source of truth for byte-identical output.
/// </summary>
internal static class ZstdNative
{
    private const string Lib = "libzstd";

    private static readonly bool? _available = TryLoad();

    /// <summary>True if the native libzstd could be loaded and the API is callable.</summary>
    public static bool Available => _available ?? false;

    private static bool TryLoad()
    {
        try
        {
            // Probe: maxCLevel() is cheap and forces the DLL to resolve.
            return ZSTD_maxCLevel() > 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ZSTD_createCStream();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint ZSTD_freeCStream(IntPtr zcs);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint ZSTD_initCStream(IntPtr zcs, int compressionLevel);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint ZSTD_compressStream2(IntPtr zcs, ref ZstdOutBuffer output, ref ZstdInBuffer input, ZstdEndDirective endOp);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint ZSTD_compressBound(nuint srcSize);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ZSTD_maxCLevel();

    /// <summary>ZSTD_endDirective values (from zstd.h).</summary>
    public enum ZstdEndDirective : int
    {
        /// <summary>Collect more data, encoder decides when to emit output.</summary>
        ZSTD_e_continue = 0,

        /// <summary>Flush any data still in the buffer.</summary>
        ZSTD_e_flush = 1,

        /// <summary>Flush and close the current frame.</summary>
        ZSTD_e_end = 2
    }

    /// <summary>ZSTD_inBuffer (zstd.h): pointer + total size + consumed position.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ZstdInBuffer
    {
        public IntPtr src;
        public nuint size;
        public nuint pos;
    }

    /// <summary>ZSTD_outBuffer (zstd.h): pointer + total capacity + filled position.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ZstdOutBuffer
    {
        public IntPtr dst;
        public nuint size;
        public nuint pos;
    }

    /// <summary>A reusable C zstd compression stream, matching chdman's per-hunk reset pattern.</summary>
    public sealed class CStream : IDisposable
    {
        private IntPtr _handle;

        /// <summary>Creates a new C zstd stream context.</summary>
        public CStream()
        {
            _handle = ZSTD_createCStream();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("ZSTD_createCStream returned null");
        }

        /// <summary>
        /// Compresses <paramref name="data"/> the way chdman does: reset the stream to
        /// <c>ZSTD_maxCLevel()</c> and flush a single end-directed frame. Returns <c>null</c> when the
        /// data does not compress smaller than the original.
        /// </summary>
        public byte[]? Compress(byte[] data)
        {
            var stream = _handle;
            ZSTD_initCStream(stream, ZSTD_maxCLevel());

            int bound = (int)ZSTD_compressBound((nuint)data.Length);
            var dest = new byte[bound];

            GCHandle hin = GCHandle.Alloc(data, GCHandleType.Pinned);
            GCHandle hout = GCHandle.Alloc(dest, GCHandleType.Pinned);
            try
            {
                var input = new ZstdInBuffer
                {
                    src = hin.AddrOfPinnedObject(),
                    size = (nuint)data.Length,
                    pos = 0
                };
                var output = new ZstdOutBuffer
                {
                    dst = hout.AddrOfPinnedObject(),
                    size = (nuint)bound,
                    pos = 0
                };

                nuint ret = ZSTD_compressStream2(stream, ref output, ref input, ZstdEndDirective.ZSTD_e_end);
                if (ret != 0)
                    return null; // not fully flushed (buffer should always be large enough) or error

                int written = (int)output.pos;
                return written < data.Length ? dest.AsSpan(0, written).ToArray() : null;
            }
            finally
            {
                hin.Free();
                hout.Free();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                ZSTD_freeCStream(_handle);
                _handle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>Frees the native stream if not disposed.</summary>
        ~CStream() => Dispose();
    }
}
