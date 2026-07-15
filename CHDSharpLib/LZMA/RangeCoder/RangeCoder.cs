namespace CHDSharp.LZMA.RangeCoder;

internal class Decoder
{
    public const uint KTopValue = (1 << 24);
    public uint Range;

    public uint Code;

    public Stream Stream = null!;
    public long Total;

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

    public void ReleaseStream()
    {
        Stream = null!;
    }

    public void CloseStream()
    {
        Stream.Dispose();
    }

    public void Normalize()
    {
        while (Range < KTopValue)
        {
            Code = (Code << 8) | (byte)Stream.ReadByte();
            Range <<= 8;
            Total++;
        }
    }

    public void Normalize2()
    {
        if (Range < KTopValue)
        {
            Code = (Code << 8) | (byte)Stream.ReadByte();
            Range <<= 8;
            Total++;
        }
    }

    public uint GetThreshold(uint total)
    {
        return Code / (Range /= total);
    }

    public void Decode(uint start, uint size)
    {
        Code -= start * Range;
        Range *= size;
        Normalize();
    }

    public uint DecodeDirectBits(int numTotalBits)
    {
        var range = Range;
        var code = Code;
        uint result = 0;
        for (var i = numTotalBits; i > 0; i--)
        {
            range >>= 1;
            /*
            result <<= 1;
            if (code >= range)
            {
                code -= range;
                result |= 1;
            }
            */
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

    public bool IsFinished => Code == 0;

}