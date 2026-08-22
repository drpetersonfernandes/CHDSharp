#nullable disable
// Original code and comments Copyright (C) 1995-2005, 2014, 2016 Jean-loup Gailly, Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using CHDSharpEncoder.ZLib.Deflate;
using CHDSharpEncoder.ZLib.Inflate;

namespace CHDSharpEncoder.ZLib;

internal static class Compressor
{
    //private const int Max = int.MaxValue;

    internal static int Compress(Span<byte> dest, ref uint destLen, ReadOnlySpan<byte> source, uint sourceLen, int level)
    {
        var left = destLen;
        destLen = 0;

        ZStream stream = new();
        var err = Deflater.DeflateInit(ref stream, level);
        if (err != ZOk)
            return err;

        stream.Output = dest;
        stream.AvailOut = 0;
        stream.Input = source;
        stream.AvailIn = 0;

        do
        {
            if (stream.AvailOut == 0)
            {
                stream.AvailOut = left; // left > Max ? Max : left;
                left -= stream.AvailOut;
            }

            if (stream.AvailIn == 0)
            {
                stream.AvailIn = sourceLen; //sourceLen > Max ? Max : sourceLen;
                sourceLen -= stream.AvailIn;
            }

            err = Deflater.Deflate(ref stream, sourceLen != 0 ? ZNoFlush : ZFinish);
        } while (err == ZOk);

        destLen = stream.total_out;
        _ = Deflater.DeflateEnd(ref stream);
        return err == ZStreamEnd ? ZOk : err;
    }

    internal static int Uncompress(Span<byte> dest, ref uint destLen, ReadOnlySpan<byte> source, ref uint sourceLen)
    {
        uint left;
        var len = sourceLen;
        byte[] buf = null; // for detection of incomplete stream when destLen == 0
        if (destLen != 0)
        {
            left = destLen;
            destLen = 0;
        }
        else
        {
            left = 1;
            buf = new byte[1];
            dest = buf;
        }

        ZStream stream = new()
        {
            Input = source,
            AvailIn = 0
        };

        var err = Inflater.InflateInit(ref stream, DefaultWindowBits);
        if (err != ZOk)
            return err;

        stream.Output = dest;
        stream.AvailOut = 0;

        do
        {
            if (stream.AvailOut == 0)
            {
                stream.AvailOut = left; // left > Max ? Max : left;
                left -= stream.AvailOut;
            }

            if (stream.AvailIn == 0)
            {
                stream.AvailIn = len; // len > Max ? Max : len;
                len -= stream.AvailIn;
            }

            err = Inflater.Inflate(ref stream, ZNoFlush);
        } while (err == ZOk);

        sourceLen -= len + stream.AvailIn;
        if (dest != buf)
        {
            destLen = stream.total_out;
        }
        else if (stream.total_out != 0 && err == ZBufError)
        {
            left = 1;
        }

        _ = Inflater.InflateEnd(ref stream);
        return err == ZStreamEnd ? ZOk :
               err == ZNeedDict ? ZDataError :
               err == ZBufError && left + stream.AvailOut != 0 ? ZDataError :
               err;
    }

    internal static uint CompressBound(uint sourceLen)
    {
        return sourceLen + (sourceLen >> 12) + (sourceLen >> 14) +
               (sourceLen >> 25) + 13;
    }
}