namespace CHDSharp.LZMA.RangeCoder;

/// <summary>Adaptive probability bit model for the LZMA range-coder decoder.</summary>
internal struct BitDecoder
{
    /// <summary>Number of bits in the bit-model total.</summary>
    private const int KNumBitModelTotalBits = 11;
    /// <summary>Total probability range (2048).</summary>
    private const uint KBitModelTotal = (1 << KNumBitModelTotalBits);
    private const int KNumMoveBits = 5;

    private uint _prob;

    /// <summary>Updates the probability model toward the decoded symbol value.</summary>
    public void UpdateModel(int numMoveBits, uint symbol)
    {
        if (symbol == 0)
        {
            _prob += (KBitModelTotal - _prob) >> numMoveBits;
        }
        else
        {
            _prob -= (_prob) >> numMoveBits;
        }
    }

    /// <summary>Initialises the probability to the midpoint.</summary>
    public void Init()
    {
        _prob = KBitModelTotal >> 1;
    }

    /// <summary>Decodes a single adaptive bit from the range decoder.</summary>
    public uint Decode(Decoder rangeDecoder)
    {
        var newBound = (rangeDecoder.Range >> KNumBitModelTotalBits) * _prob;
        if (rangeDecoder.Code < newBound)
        {
            rangeDecoder.Range = newBound;
            _prob += (KBitModelTotal - _prob) >> KNumMoveBits;
            if (rangeDecoder.Range < Decoder.KTopValue)
            {
                rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
                rangeDecoder.Range <<= 8;
                rangeDecoder.Total++;
            }

            return 0;
        }
        else
        {
            rangeDecoder.Range -= newBound;
            rangeDecoder.Code -= newBound;
            _prob -= (_prob) >> KNumMoveBits;
            if (rangeDecoder.Range < Decoder.KTopValue)
            {
                rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
                rangeDecoder.Range <<= 8;
                rangeDecoder.Total++;
            }

            return 1;
        }
    }
}
