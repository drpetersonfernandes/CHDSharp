namespace CHDSharp.LZMA.RangeCoder;

/// <summary>Bit-tree decoder using adaptive probability models. Supports forward and reverse bit ordering.</summary>
internal readonly struct BitTreeDecoder
{
    private readonly BitDecoder[] _models;
    private readonly int _numBitLevels;

    /// <summary>Initialises a new bit-tree decoder with the specified number of bit levels.</summary>
    public BitTreeDecoder(int numBitLevels)
    {
        _numBitLevels = numBitLevels;
        _models = new BitDecoder[1 << numBitLevels];
    }

    /// <summary>Initialises all probability models in the tree.</summary>
    public void Init()
    {
        for (uint i = 1; i < (1 << _numBitLevels); i++)
            _models[i].Init();
    }

    /// <summary>Decodes a symbol using forward (MSB-first) bit ordering.</summary>
    public uint Decode(Decoder rangeDecoder)
    {
        uint m = 1;
        for (var bitIndex = _numBitLevels; bitIndex > 0; bitIndex--)
        {
            m = (m << 1) + _models[m].Decode(rangeDecoder);
        }

        return m - ((uint)1 << _numBitLevels);
    }

    /// <summary>Decodes a symbol using reverse (LSB-first) bit ordering on this instance.</summary>
    public uint ReverseDecode(Decoder rangeDecoder)
    {
        uint m = 1;
        uint symbol = 0;
        for (var bitIndex = 0; bitIndex < _numBitLevels; bitIndex++)
        {
            var bit = _models[m].Decode(rangeDecoder);
            m <<= 1;
            m += bit;
            symbol |= (bit << bitIndex);
        }

        return symbol;
    }

    /// <summary>Decodes a symbol using reverse bit ordering on an external model array.</summary>
    public static uint ReverseDecode(BitDecoder[] models, uint startIndex,
        Decoder rangeDecoder, int numBitLevels)
    {
        uint m = 1;
        uint symbol = 0;
        for (var bitIndex = 0; bitIndex < numBitLevels; bitIndex++)
        {
            var bit = models[startIndex + m].Decode(rangeDecoder);
            m <<= 1;
            m += bit;
            symbol |= (bit << bitIndex);
        }

        return symbol;
    }
}
