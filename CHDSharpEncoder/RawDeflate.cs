using System.IO.Compression;
using ZLibDotNet;

namespace CHDSharpEncoder;

/// <summary>Provides raw DEFLATE compression and decompression utilities.</summary>
public static class RawDeflate
{
    /// <summary>
    /// Compresses data using raw DEFLATE, stripping any Zlib header/trailer.
    /// Uses the vendored zlib 1.3.1 C# port with chdman's exact parameters — byte-for-byte
    /// identical to <c>chdman -c zlib</c> (verified 562/562 hunks vs chdman).
    /// </summary>
    /// <param name="data">The uncompressed input data.</param>
    /// <returns>The compressed bytes, or <c>null</c> if compression did not reduce size.</returns>
    public static byte[]? Compress(byte[] data)
    {
        var zlib = new ZLib();
        var output = new byte[zlib.CompressBound((uint)data.Length)];
        var zs = new ZStream { Input = data, Output = output };
        _ = zlib.DeflateInit(ref zs, ZLib.Z_BEST_COMPRESSION, ZLib.Z_DEFLATED, -15, 8, ZLib.Z_DEFAULT_STRATEGY);
        int status;
        do
        {
            status = zlib.Deflate(ref zs, ZLib.Z_FINISH);
        } while (status == ZLib.Z_OK);

        _ = zlib.DeflateEnd(ref zs);

        var result = output.AsSpan(0, (int)zs.TotalOut).ToArray();

        if (result.Length >= data.Length)
            return null;

        return result;
    }

    /// <summary>Decompresses raw DEFLATE data to the specified original size.</summary>
    /// <param name="compressed">The compressed input data.</param>
    /// <param name="originalSize">The expected number of uncompressed bytes.</param>
    /// <returns>The decompressed byte array.</returns>
    public static byte[] Decompress(byte[] compressed, int originalSize)
    {
        using var ms = new MemoryStream(compressed);
        using var ds = new DeflateStream(ms, CompressionMode.Decompress);
        var result = new byte[originalSize];
        var offset = 0;
        while (offset < originalSize)
        {
            var read = ds.Read(result, offset, originalSize - offset);
            if (read == 0)
                throw new InvalidDataException("Deflate decompression ended prematurely");

            offset += read;
        }

        return result;
    }
}
