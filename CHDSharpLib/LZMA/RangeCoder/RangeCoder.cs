namespace CHDSharp.LZMA.RangeCoder;

/// <summary>LZMA range-coder decoder that adaptively decodes bits from a compressed stream.</summary>
internal class Decoder
{
    /// <summary>Top value used for range normalisation.</summary>
    internal const uint KTopValue = (1 << 24);
    /// <summary>Current range value.</summary>
    internal uint Range;

    /// <summary>Current code (compressed data) being decoded.</summary>
    internal uint Code;

    /// <summary>Input stream providing compressed data.</summary>
    internal Stream Stream = null!;
    /// <summary>Total number of bytes consumed from the stream.</summary>
    internal long Total;

    /// <summary>Reads the next byte from the stream, throwing <see cref="DataErrorException"/> on EOF (truncated stream).</summary>
    internal byte ReadByteChecked()
    {
        var b = Stream.ReadByte();
        if (b < 0)
            throw new DataErrorException();

        return (byte)b;
    }

    /// <summary>Initialises the range decoder by reading the first five bytes from the stream.</summary>
    internal void Init(Stream stream)
    {
        Stream = stream;

        Code = 0;
        Range = 0xFFFFFFFF;
        for (var i = 0; i < 5; i++)
        {
            Code = (Code << 8) | ReadByteChecked();
        }

        Total = 5;
    }

    /// <summary>Releases the reference to the input stream.</summary>
    internal void ReleaseStream()
    {
        Stream = null!;
    }

    /// <summary>Closes and disposes the input stream.</summary>
    internal void CloseStream()
    {
        Stream.Dispose();
    }

    /// <summary>Normalises the range by reading bytes from the stream until <see cref="Range"/> >= <see cref="KTopValue"/>.</summary>
    internal void Normalize()
    {
        while (Range < KTopValue)
        {
            Code = (Code << 8) | ReadByteChecked();
            Range <<= 8;
            Total++;
        }
    }

    /// <summary>Single-iteration normalise (used when only one byte may be needed).</summary>
    internal void Normalize2()
    {
        if (Range < KTopValue)
        {
            Code = (Code << 8) | ReadByteChecked();
            Range <<= 8;
            Total++;
        }
    }

    /// <summary>Computes the threshold value for a given total frequency.</summary>
    internal uint GetThreshold(uint total)
    {
        if (total == 0)
            throw new DataErrorException();

        return Code / (Range /= total);
    }

    /// <summary>Decodes a symbol given its frequency range.</summary>
    internal void Decode(uint start, uint size)
    {
        Code -= start * Range;
        Range *= size;
        Normalize();
    }

    /// <summary>Decodes a specified number of raw (non-adaptive) bits.</summary>
    internal uint DecodeDirectBits(int numTotalBits)
    {
        var range = Range;
        var code = Code;
        uint result = 0;
        for (var i = numTotalBits; i > 0; i--)
        {
            range >>= 1;
            var t = (code - range) >> 31;
            code -= range & (t - 1);
            result = (result << 1) | (1 - t);

            if (range < KTopValue)
            {
                code = (code << 8) | ReadByteChecked();
                range <<= 8;
                Total++;
            }
        }

        Range = range;
        Code = code;
        return result;
    }

    /// <summary>Decodes a single adaptive bit using a probability model.</summary>
    internal uint DecodeBit(uint size0, int numTotalBits)
    {
        var newBound = (Range >> numTotalBits) * size0;
        uint symbol;
        if (Code < newBound)
        {
            symbol = 0;
            Range = newBound;
        }
        else
        {
            symbol = 1;
            Code -= newBound;
            Range -= newBound;
        }

        Normalize();
        return symbol;
    }

    /// <summary>Gets whether the decoder has finished (all data has been consumed).</summary>
    internal bool IsFinished => Code == 0;
}
