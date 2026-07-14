namespace CHDSharp.LZMA;

public class LzmaEncoderProperties
{
    internal CoderPropID[] propIDs;
    internal object[] properties;

    public LzmaEncoderProperties()
        : this(false)
    {
    }

    public LzmaEncoderProperties(bool eos)
        : this(eos, 1 << 20)
    {
    }

    public LzmaEncoderProperties(bool eos, int dictionary)
        : this(eos, dictionary, 32)
    {
    }

    public LzmaEncoderProperties(bool eos, int dictionary, int numFastBytes)
    {
        var posStateBits = 2;
        var litContextBits = 4;
        var litPosBits = 0;
        var algorithm = 2;
        var mf = "bt4";

        propIDs =
        [
            CoderPropID.DictionarySize,
            CoderPropID.PosStateBits,
            CoderPropID.LitContextBits,
            CoderPropID.LitPosBits,
            CoderPropID.Algorithm,
            CoderPropID.NumFastBytes,
            CoderPropID.MatchFinder,
            CoderPropID.EndMarker
        ];
        properties =
        [
            dictionary,
            posStateBits,
            litContextBits,
            litPosBits,
            algorithm,
            numFastBytes,
            mf,
            eos
        ];
    }
}