using CHDSharpEncoder.Interfaces;

namespace CHDSharpEncoder;

/// <summary>
/// MAME A/V Huffman codec ('avhu'), matching <c>chd_avhuff_compressor</c>: each hunk is one
/// raw 'chav' A/V frame (assembled by <see cref="ChdEncoder.EncodeLaserDisc"/>) compressed as
/// delta-RLE Huffman video + per-channel mono FLAC audio via <see cref="AvHuffEncoder"/>.
/// Decodable by CHDSharpLib's <c>ChdReaders.AvHuff</c> and chdman.
/// </summary>
public sealed class AvHuffCodec : IChdCodec
{
    private readonly AvHuffEncoder _encoder = new();

    /// <inheritdoc/>
    public uint Tag => CodecTags.Avhu;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        var dest = new byte[data.Length];
        int length;
        try
        {
            length = _encoder.EncodeData(data, dest);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        // only store when actually smaller than the raw frame (MAME keeps the best of the
        // configured codecs; with the single-codec avhu list this is the only candidate)
        return length < data.Length ? dest.AsSpan(0, length).ToArray() : null;
    }
}
