namespace CHDSharp.LZMA.RangeCoder;

internal class Encoder
{
    public const uint KTopValue = (1 << 24);

    private Stream _stream;

    public ulong Low;
    public uint Range;
    private uint _cacheSize;
    private byte _cache;

    //long StartPosition;

    public void SetStream(Stream stream)
    {
        _stream = stream;
    }

    public void ReleaseStream()
    {
        _stream = null;
    }

    public void Init()
    {
        //StartPosition = Stream.Position;

        Low = 0;
        Range = 0xFFFFFFFF;
        _cacheSize = 1;
        _cache = 0;
    }

    public void FlushData()
    {
        for (var i = 0; i < 5; i++)
            ShiftLow();
    }

    public void FlushStream()
    {
        _stream.Flush();
    }

    public void CloseStream()
    {
        _stream.Dispose();
    }

    public void Encode(uint start, uint size, uint total)
    {
        Low += start * (Range /= total);
        Range *= size;
        while (Range < KTopValue)
        {
            Range <<= 8;
            ShiftLow();
        }
    }

    public void ShiftLow()
    {
        if ((uint)Low < (uint)0xFF000000 || (uint)(Low >> 32) == 1)
        {
            var temp = _cache;
            do
            {
                _stream.WriteByte((byte)(temp + (Low >> 32)));
                temp = 0xFF;
            } while (--_cacheSize != 0);

            _cache = (byte)(((uint)Low) >> 24);
        }

        _cacheSize++;
        Low = ((uint)Low) << 8;
    }

    public void EncodeDirectBits(uint v, int numTotalBits)
    {
        for (var i = numTotalBits - 1; i >= 0; i--)
        {
            Range >>= 1;
            if (((v >> i) & 1) == 1)
            {
                Low += Range;
            }

            if (Range < KTopValue)
            {
                Range <<= 8;
                ShiftLow();
            }
        }
    }

    public void EncodeBit(uint size0, int numTotalBits, uint symbol)
    {
        var newBound = (Range >> numTotalBits) * size0;
        if (symbol == 0)
        {
            Range = newBound;
        }
        else
        {
            Low += newBound;
            Range -= newBound;
        }

        while (Range < KTopValue)
        {
            Range <<= 8;
            ShiftLow();
        }
    }

    public long GetProcessedSizeAdd()
    {
        return -1;
        //return _cacheSize + Stream.Position - StartPosition + 4;
        // (long)Stream.GetProcessedSize();
    }
}

internal class Decoder
{
    public const uint KTopValue = (1 << 24);
    public uint Range;

    public uint Code;

    // public Buffer.InBuffer Stream = new Buffer.InBuffer(1 << 16);
    public Stream Stream;
    public long Total;

    public void Init(Stream stream)
    {
        // Stream.Init(stream);
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
        // Stream.ReleaseStream();
        Stream = null;
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

    // ulong GetProcessedSize() {return Stream.GetProcessedSize(); }
}