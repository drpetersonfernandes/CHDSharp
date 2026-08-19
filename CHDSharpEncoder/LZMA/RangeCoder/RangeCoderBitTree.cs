namespace CHDSharpEncoder.LZMA.RangeCoder;

/// <summary>Bit-tree encoder using adaptive probability models, ported from the LZMA SDK (public domain). Supports forward and reverse bit ordering.</summary>
internal struct BitTreeEncoder
{
    private BitEncoder[] _models;
    private readonly int _numBitLevels;

    internal BitTreeEncoder(int numBitLevels)
    {
        _numBitLevels = numBitLevels;
        _models = new BitEncoder[1 << numBitLevels];
    }

    internal void Init()
    {
        for (uint i = 1; i < (1 << _numBitLevels); i++)
        {
            _models[i].Init();
        }
    }

    internal void Encode(Encoder rangeEncoder, uint symbol)
    {
        uint m = 1;
        for (var bitIndex = _numBitLevels; bitIndex > 0;)
        {
            bitIndex--;
            var bit = (symbol >> bitIndex) & 1;
            _models[m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
        }
    }

    internal void ReverseEncode(Encoder rangeEncoder, uint symbol)
    {
        uint m = 1;
        for (uint i = 0; i < _numBitLevels; i++)
        {
            var bit = symbol & 1;
            _models[m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
            symbol >>= 1;
        }
    }

    internal uint GetPrice(uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var bitIndex = _numBitLevels; bitIndex > 0;)
        {
            bitIndex--;
            var bit = (symbol >> bitIndex) & 1;
            price += _models[m].GetPrice(bit);
            m = (m << 1) + bit;
        }

        return price;
    }

    internal uint ReverseGetPrice(uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var i = _numBitLevels; i > 0; i--)
        {
            var bit = symbol & 1;
            symbol >>= 1;
            price += _models[m].GetPrice(bit);
            m = (m << 1) | bit;
        }

        return price;
    }

    internal static uint ReverseGetPrice(BitEncoder[] models, uint startIndex,
        int numBitLevels, uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var i = numBitLevels; i > 0; i--)
        {
            var bit = symbol & 1;
            symbol >>= 1;
            price += models[startIndex + m].GetPrice(bit);
            m = (m << 1) | bit;
        }

        return price;
    }

    internal static void ReverseEncode(BitEncoder[] models, uint startIndex,
        Encoder rangeEncoder, int numBitLevels, uint symbol)
    {
        uint m = 1;
        for (var i = 0; i < numBitLevels; i++)
        {
            var bit = symbol & 1;
            models[startIndex + m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
            symbol >>= 1;
        }
    }
}