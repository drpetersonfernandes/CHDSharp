namespace CHDSharp.LZMA;

internal abstract class Base
{
    public const uint KNumRepDistances = 4;
    public const uint KNumStates = 12;

    public const int KNumPosSlotBits = 6;
    public const int KDicLogSizeMin = 0;

    public const int KNumLenToPosStatesBits = 2; // it's for speed optimization
    public const uint KNumLenToPosStates = 1 << KNumLenToPosStatesBits;

    public const uint KMatchMinLen = 2;

    public static uint GetLenToPosState(uint len)
    {
        len -= KMatchMinLen;
        if (len < KNumLenToPosStates)
            return len;

        return (uint)(KNumLenToPosStates - 1);
    }

    public const int KNumAlignBits = 4;
    public const uint KAlignTableSize = 1 << KNumAlignBits;
    public const uint KAlignMask = (KAlignTableSize - 1);

    public const uint KStartPosModelIndex = 4;
    public const uint KEndPosModelIndex = 14;
    public const uint KNumPosModels = KEndPosModelIndex - KStartPosModelIndex;

    public const uint KNumFullDistances = 1 << ((int)KEndPosModelIndex / 2);

    public const uint KNumLitPosStatesBitsEncodingMax = 4;
    public const uint KNumLitContextBitsMax = 8;

    public const int KNumPosStatesBitsMax = 4;
    public const uint KNumPosStatesMax = (1 << KNumPosStatesBitsMax);
    public const int KNumPosStatesBitsEncodingMax = 4;
    public const uint KNumPosStatesEncodingMax = (1 << KNumPosStatesBitsEncodingMax);

    public const int KNumLowLenBits = 3;
    public const int KNumMidLenBits = 3;
    public const int KNumHighLenBits = 8;
    public const uint KNumLowLenSymbols = 1 << KNumLowLenBits;
    public const uint KNumMidLenSymbols = 1 << KNumMidLenBits;
    public const uint KNumLenSymbols = KNumLowLenSymbols + KNumMidLenSymbols +
                                       (1 << KNumHighLenBits);
    public const uint KMatchMaxLen = KMatchMinLen + KNumLenSymbols - 1;
}