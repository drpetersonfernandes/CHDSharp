namespace CHDSharp.LZMA;

/// <summary>LZMA decoder constants and utilities.</summary>
internal abstract class Base
{
    /// <summary>Number of repeat distances.</summary>
    public const uint KNumRepDistances = 4;
    /// <summary>Number of states in the state machine.</summary>
    public const uint KNumStates = 12;

    /// <summary>Number of position slot bits.</summary>
    public const int KNumPosSlotBits = 6;
    /// <summary>Minimum dictionary size log.</summary>
    public const int KDicLogSizeMin = 0;

    /// <summary>Number of bits for length-to-position mapping.</summary>
    public const int KNumLenToPosStatesBits = 2; // it's for speed optimization
    /// <summary>Number of length-to-position states.</summary>
    public const uint KNumLenToPosStates = 1 << KNumLenToPosStatesBits;

    /// <summary>Minimum match length.</summary>
    public const uint KMatchMinLen = 2;

    /// <summary>Maps a length value to a position state index.</summary>
    public static uint GetLenToPosState(uint len)
    {
        len -= KMatchMinLen;
        if (len < KNumLenToPosStates)
            return len;

        return KNumLenToPosStates - 1;
    }

    /// <summary>Number of alignment bits.</summary>
    public const int KNumAlignBits = 4;
    /// <summary>Size of the alignment table.</summary>
    public const uint KAlignTableSize = 1 << KNumAlignBits;
    /// <summary>Alignment mask.</summary>
    public const uint KAlignMask = (KAlignTableSize - 1);

    /// <summary>Start position model index.</summary>
    public const uint KStartPosModelIndex = 4;
    /// <summary>End position model index.</summary>
    public const uint KEndPosModelIndex = 14;
    /// <summary>Number of position models.</summary>
    public const uint KNumPosModels = KEndPosModelIndex - KStartPosModelIndex;

    /// <summary>Number of full distances.</summary>
    public const uint KNumFullDistances = 1 << ((int)KEndPosModelIndex / 2);

    /// <summary>Maximum literal position state bits (encoding).</summary>
    public const uint KNumLitPosStatesBitsEncodingMax = 4;
    /// <summary>Maximum literal context bits.</summary>
    public const uint KNumLitContextBitsMax = 8;

    /// <summary>Maximum position state bits.</summary>
    public const int KNumPosStatesBitsMax = 4;
    /// <summary>Maximum position states.</summary>
    public const uint KNumPosStatesMax = (1 << KNumPosStatesBitsMax);
    /// <summary>Maximum position state bits (encoding).</summary>
    public const int KNumPosStatesBitsEncodingMax = 4;
    /// <summary>Maximum position states (encoding).</summary>
    public const uint KNumPosStatesEncodingMax = (1 << KNumPosStatesBitsEncodingMax);

    /// <summary>Number of low length bits.</summary>
    public const int KNumLowLenBits = 3;
    /// <summary>Number of mid length bits.</summary>
    public const int KNumMidLenBits = 3;
    /// <summary>Number of high length bits.</summary>
    public const int KNumHighLenBits = 8;
    /// <summary>Number of low length symbols.</summary>
    public const uint KNumLowLenSymbols = 1 << KNumLowLenBits;
    /// <summary>Number of mid length symbols.</summary>
    public const uint KNumMidLenSymbols = 1 << KNumMidLenBits;
    /// <summary>Total number of length symbols.</summary>
    public const uint KNumLenSymbols = KNumLowLenSymbols + KNumMidLenSymbols +
                                       (1 << KNumHighLenBits);
    /// <summary>Maximum match length.</summary>
    public const uint KMatchMaxLen = KMatchMinLen + KNumLenSymbols - 1;
}