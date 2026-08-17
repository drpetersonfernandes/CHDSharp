namespace CHDSharpEncoder;

/// <summary>
/// MAME generic Huffman codec ('huff'), matching <c>chd_huffman_compressor</c> /
/// <c>huffman_8bit_encoder</c>: the hunk is histogrammed, a canonical tree with codes of
/// at most 16 bits is built (weight-scaled), the tree is exported Huffman-encoded via a
/// 24-symbol/6-bit small tree, and the data follows. Decodable by CHDSharpLib's
/// <c>ChdReaders.Huffman</c> and chdman.
/// </summary>
public sealed class HuffCodec : IChdCodec
{
    /// <summary>Number of symbols in the 8-bit alphabet.</summary>
    private const int NumCodes = 256;

    /// <summary>Maximum code length in bits.</summary>
    private const int MaxBits = 16;

    private readonly HuffmanEncoder _encoder = new(NumCodes, MaxBits);

    /// <inheritdoc/>
    public uint Tag => CodecTags.HUFF;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        // worst case: 16 bits per symbol + tree export overhead
        var bs = new BitStreamOut(data.Length * 2 + 512);

        _encoder.ResetHistogram();
        foreach (var b in data)
            _encoder.CountSymbol(b);
        _encoder.BuildTree();
        _encoder.ExportTreeHuffman(bs);

        foreach (var b in data)
            _encoder.Encode(bs, b);

        bs.Flush();
        var result = bs.ToArray();
        return result.Length < data.Length ? result : null;
    }
}