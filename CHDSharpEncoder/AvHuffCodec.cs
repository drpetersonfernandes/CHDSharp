using CHDSharpEncoder.Interfaces;

namespace CHDSharpEncoder;

/// <summary>
/// MAME A/V Huffman codec ('avhu'), matching <c>chd_avhuff_compressor</c>: each hunk is one
/// raw 'chav' A/V frame (assembled by <see cref="ChdEncoder.EncodeLaserDisc"/>) compressed as
/// delta-RLE Huffman video + per-channel mono FLAC audio via <see cref="AvHuffEncoder"/>.
/// Multi-frame hunks (hunkBytes > bytesPerFrame) are stored raw, matching MAME's codec-chain
/// behavior where the avhuff compress fails on already-encoded data.
/// Decodable by CHDSharpLib's <c>ChdReaders.AvHuff</c> and chdman.
/// </summary>
public sealed class AvHuffCodec : IChdCodec
{
    /// <inheritdoc/>
    public uint Tag => CodecTags.Avhu;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        if (data.Length < 12 || data[0] != 'c' || data[1] != 'h' || data[2] != 'a' || data[3] != 'v')
            return null;

        // Determine raw frame size from the 'chav' header. If the hunk contains
        // multiple frames (data.Length > rawFrameSize), store raw — avhuff only
        // compresses single-frame hunks, matching MAME's codec-chain behavior.
        uint channels = data[5];
        var samples = (uint)((data[6] << 8) | data[7]);
        var width = (uint)((data[8] << 8) | data[9]);
        var height = (uint)((data[10] << 8) | data[11]);
        var rawFrameSize = AvHuffEncoder.RawDataSize(width, height, channels, samples);
        if (rawFrameSize > 0 && data.Length > rawFrameSize)
            return null;

        var encoder = new AvHuffEncoder();
        var dest = new byte[data.Length];
        int length;
        try
        {
            length = encoder.EncodeData(data, dest);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return length < data.Length ? dest.AsSpan(0, length).ToArray() : null;
    }
}
