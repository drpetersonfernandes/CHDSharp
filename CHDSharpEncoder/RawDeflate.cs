using System.IO.Compression;

namespace CHDSharpEncoder;

/// <summary>Provides raw DEFLATE compression and decompression utilities.</summary>
public static class RawDeflate
{
    /// <summary>Compresses data using raw DEFLATE, stripping any Zlib header/trailer.</summary>
    /// <param name="data">The uncompressed input data.</param>
    /// <returns>The compressed bytes, or <c>null</c> if compression did not reduce size.</returns>
    public static byte[]? Compress(byte[] data)
    {
        using var ms = new MemoryStream(data.Length);
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            ds.Write(data, 0, data.Length);
        }

        var result = ms.ToArray();

        if (HasZlibHeader(result))
        {
            result = result.AsSpan(2, result.Length - 6).ToArray();
        }

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

    private static bool HasZlibHeader(byte[] data)
    {
        if (data.Length < 6)
            return false;

        var cmf = data[0];
        var flg = data[1];

        if (((cmf & 0x0F) != 8) || ((cmf >> 4) > 7))
            return false;

        if (((cmf * 256) + flg) % 31 != 0)
            return false;

        return true;
    }
}
