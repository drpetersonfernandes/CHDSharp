namespace CHDSharpEncoder.LZMA.RangeCoder;

/// <summary>LZMA range-coder encoder, ported from the LZMA SDK (public domain).</summary>
internal class Encoder
{
    /// <summary>Top value used for range normalisation.</summary>
    internal const uint KTopValue = (1 << 24);

    private Stream? _stream;

    internal ulong Low;
    internal uint Range;
    private uint _cacheSize;
    private byte _cache;

    private long _startPosition;

    internal void SetStream(Stream stream)
    {
        _stream = stream;
    }

    internal void ReleaseStream()
    {
        _stream = null;
    }

    internal void Init()
    {
        _startPosition = _stream!.Position;

        Low = 0;
        Range = 0xFFFFFFFF;
        _cacheSize = 0;
        _cache = 0;
    }

    internal void FlushData()
    {
        for (var i = 0; i < 5; i++)
        {
            ShiftLow();
        }
    }

    internal void FlushStream()
    {
        _stream!.Flush();
    }

    internal void Encode(uint start, uint size, uint total)
    {
        Low += start * (Range /= total);
        Range *= size;
        while (Range < KTopValue)
        {
            Range <<= 8;
            ShiftLow();
        }
    }

    internal void ShiftLow()
    {
        var low = (uint)Low;
        var high = (uint)(Low >> 32);
        Low = (uint)(low << 8);
        if (low < 0xFF000000 || high != 0)
        {
            _stream!.WriteByte((byte)(_cache + high));
            _cache = (byte)(low >> 24);
            if (_cacheSize == 0)
            {
                return;
            }

            high += 0xFF;
            while (true)
            {
                _stream!.WriteByte((byte)high);
                if (--_cacheSize == 0)
                {
                    return;
                }
            }
        }

        _cacheSize++;
    }

    internal void EncodeDirectBits(uint v, int numTotalBits)
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

    internal void EncodeBit(uint size0, int numTotalBits, uint symbol)
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

    internal long GetProcessedSizeAdd()
    {
        return _cacheSize +
            _stream!.Position - _startPosition + 4;
    }
}