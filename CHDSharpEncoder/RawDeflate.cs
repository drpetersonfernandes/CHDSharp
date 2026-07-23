using System.IO.Compression;

namespace CHDSharpEncoder;

public static class RawDeflate
{
    public static byte[]? Compress(byte[] data)
    {
        using var ms = new MemoryStream(data.Length);
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            ds.Write(data, 0, data.Length);
        }

        byte[] result = ms.ToArray();

        if (HasZlibHeader(result))
            result = result.AsSpan(2, result.Length - 6).ToArray();

        if (result.Length >= data.Length)
            return null;

        return result;
    }

    public static byte[] Decompress(byte[] compressed, int originalSize)
    {
        using var ms = new MemoryStream(compressed);
        using var ds = new DeflateStream(ms, CompressionMode.Decompress);
        byte[] result = new byte[originalSize];
        int offset = 0;
        while (offset < originalSize)
        {
            int read = ds.Read(result, offset, originalSize - offset);
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

        byte cmf = data[0];
        byte flg = data[1];

        if (((cmf & 0x0F) != 8) || ((cmf >> 4) > 7))
            return false;

        if (((cmf * 256) + flg) % 31 != 0)
            return false;

        return true;
    }
}
