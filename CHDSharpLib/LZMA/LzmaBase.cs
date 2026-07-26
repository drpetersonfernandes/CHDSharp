namespace CHDSharp.LZMA;

/// <summary>LZMA decoder constants and utilities.</summary>
internal abstract class Base
{
    /// <summary>Number of repeat distances.</summary>
    internal const uint KNumRepDistances = 4;
    /// <summary>Number of states in the state machine.</summary>
    internal const uint KNumStates = 12;

    /// <summary>Number of position slot bits.</summary>
    internal const int KNumPosSlotBits = 6;
    /// <summary>Minimum dictionary size log.</summary>
    internal const int KDicLogSizeMin = 0;

    /// <summary>Number of bits for length-to-position mapping.</summary>
    internal const int KNumLenToPosStatesBits = 2; // it's for speed optimization
    /// <summary>Number of length-to-position states.</summary>
    internal const uint KNumLenToPosStates = 1 << KNumLenToPosStatesBits;

    /// <summary>Minimum match length.</summary>
    internal const uint KMatchMinLen = 2;

    /// <summary>Maps a length value to a position state index.</summary>
    internal static uint GetLenToPosState(uint len)
    {
        len -= KMatchMinLen;
        if (len < KNumLenToPosStates)
            return len;

        return KNumLenToPosStates - 1;
    }

    /// <summary>Number of alignment bits.</summary>
    internal const int KNumAlignBits = 4;
    /// <summary>Size of the alignment table.</summary>
    internal const uint KAlignTableSize = 1 << KNumAlignBits;
    /// <summary>Alignment mask.</summary>
    internal const uint KAlignMask = (KAlignTableSize - 1);

    /// <summary>Start position model index.</summary>
    internal const uint KStartPosModelIndex = 4;
    /// <summary>End position model index.</summary>
    internal const uint KEndPosModelIndex = 14;
    /// <summary>Number of position models.</summary>
    internal const uint KNumPosModels = KEndPosModelIndex - KStartPosModelIndex;

    /// <summary>Number of full distances.</summary>
    internal const uint KNumFullDistances = 1 << ((int)KEndPosModelIndex / 2);

    /// <summary>Maximum literal position state bits (encoding).</summary>
    internal const uint KNumLitPosStatesBitsEncodingMax = 4;
    /// <summary>Maximum literal context bits.</summary>
    internal const uint KNumLitContextBitsMax = 8;

    /// <summary>Maximum position state bits.</summary>
    internal const int KNumPosStatesBitsMax = 4;
    /// <summary>Maximum position states.</summary>
    internal const uint KNumPosStatesMax = (1 << KNumPosStatesBitsMax);
    /// <summary>Maximum position state bits (encoding).</summary>
    internal const int KNumPosStatesBitsEncodingMax = 4;
    /// <summary>Maximum position states (encoding).</summary>
    internal const uint KNumPosStatesEncodingMax = (1 << KNumPosStatesBitsEncodingMax);

    /// <summary>Number of low length bits.</summary>
    internal const int KNumLowLenBits = 3;
    /// <summary>Number of mid length bits.</summary>
    internal const int KNumMidLenBits = 3;
    /// <summary>Number of high length bits.</summary>
    internal const int KNumHighLenBits = 8;
    /// <summary>Number of low length symbols.</summary>
    internal const uint KNumLowLenSymbols = 1 << KNumLowLenBits;
    /// <summary>Number of mid length symbols.</summary>
    internal const uint KNumMidLenSymbols = 1 << KNumMidLenBits;
    /// <summary>Total number of length symbols.</summary>
    internal const uint KNumLenSymbols = KNumLowLenSymbols + KNumMidLenSymbols +
                                       (1 << KNumHighLenBits);
    /// <summary>Maximum match length.</summary>
    internal const uint KMatchMaxLen = KMatchMinLen + KNumLenSymbols - 1;
}