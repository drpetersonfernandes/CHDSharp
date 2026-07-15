namespace CHDSharp.LZMA.RangeCoder;

/// <summary>LZMA range-coder decoder that adaptively decodes bits from a compressed stream.</summary>
internal class Decoder
{
    /// <summary>Top value used for range normalisation.</summary>
    public const uint KTopValue = (1 << 24);
    /// <summary>Current range value.</summary>
    public uint Range;

    /// <summary>Current code (compressed data) being decoded.</summary>
    public uint Code;

    /// <summary>Input stream providing compressed data.</summary>
    public Stream Stream = null!;
    /// <summary>Total number of bytes consumed from the stream.</summary>
    public long Total;

    /// <summary>Initialises the range decoder by reading the first five bytes from the stream.</summary>
    public void Init(Stream stream)
    {
        Stream = stream;

        Code = 0;
        Range = 0xFFFFFFFF;
        for (var i = 0; i < 5; i++)
        {
            Code = (Code << 8) | (byte)Stream.ReadByte();
        }

        Total = 5;
    }

    /// <summary>Releases the reference to the input stream.</summary>
    public void ReleaseStream()
    {
        Stream = null!;
    }

    /// <summary>Closes and disposes the input stream.</summary>
    public void CloseStream()
    {
        Stream.Dispose();
    }

    /// <summary>Normalises the range by reading bytes from the stream until <see cref="Range"/> >= <see cref="KTopValue"/>.</summary>
    public void Normalize()
    {
        while (Range < KTopValue)
        {
            Code = (Code << 8) | (byte)Stream.ReadByte();
            Range <<= 8;
            Total++;
        }
    }

    /// <summary>Single-iteration normalise (used when only one byte may be needed).</summary>
    public void Normalize2()
    {
        if (Range < KTopValue)
        {
            Code = (Code << 8) | (byte)Stream.ReadByte();
            Range <<= 8;
            Total++;
        }
    }

    /// <summary>Computes the threshold value for a given total frequency.</summary>
    public uint GetThreshold(uint total)
    {
        return Code / (Range /= total);
    }

    /// <summary>Decodes a symbol given its frequency range.</summary>
    public void Decode(uint start, uint size)
    {
        Code -= start * Range;
        Range *= size;
        Normalize();
    }

    /// <summary>Decodes a specified number of raw (non-adaptive) bits.</summary>
    public uint DecodeDirectBits(int numTotalBits)
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
                code = (code << 8) | (byte)Stream.ReadByte();
                range <<= 8;
                Total++;
            }
        }

        Range = range;
        Code = code;
        return result;
    }

    /// <summary>Decodes a single adaptive bit using a probability model.</summary>
    public uint DecodeBit(uint size0, int numTotalBits)
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
    public bool IsFinished => Code == 0;

}
