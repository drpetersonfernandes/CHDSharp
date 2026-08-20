using CHDSharpEncoder.Flac;
using CHDSharpEncoder.Interfaces;

namespace CHDSharpEncoder;

/// <summary>
/// Raw FLAC codec ('flac'), matching MAME's <c>chd_flac_compressor</c>: the hunk is
/// treated as interleaved 2-channel 16-bit 44100 Hz samples and encoded twice (as
/// little-endian and big-endian samples); the smaller result wins and a leading marker
/// byte ('L'/'B') records the stored endianness. The block size follows MAME's formula
/// (hunk samples halved until ≤ 2048). Decodable by CHDSharpLib's <c>ChdReaders.Flac</c>
/// and chdman.
/// </summary>
public sealed class FlacCodec : IChdCodec
{
    private readonly int _blockSize;
    private readonly byte[] _swappedBuffer;

    /// <summary>Creates a raw FLAC codec for the given hunk size.</summary>
    /// <param name="hunkBytes">Hunk size in bytes; must be a multiple of 4 (2ch × 16-bit).</param>
    public FlacCodec(uint hunkBytes)
    {
        if (hunkBytes % 4 != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of 4 for 2ch/16-bit samples");

        // MAME's chd_flac_compressor::blocksize: samples per hunk, halved until ≤ 2048
        _blockSize = (int)(hunkBytes / 4);
        while (_blockSize > 2048)
        {
            _blockSize /= 2;
        }

        _swappedBuffer = new byte[hunkBytes];
    }

    /// <inheritdoc/>
    public uint Tag => CodecTags.Flac;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        // worst case: verbatim subframes + frame headers
        var leOut = new byte[data.Length * 2];
        var beOut = new byte[data.Length * 2];

        var encoder = new LibFlacEncoder(_blockSize);

        // little-endian pass: samples read as little-endian (bytes as-is)
        int leLen = encoder.Encode(leOut, data);

        // big-endian pass: samples read as big-endian (each 16-bit pair swapped)
        SwapPairs(data, _swappedBuffer);
        int beLen = encoder.Encode(beOut, _swappedBuffer);

        if (leLen + 1 >= data.Length && beLen + 1 >= data.Length)
            return null;

        // pick the smaller; marker 'L' = stored little-endian, 'B' = stored big-endian
        // (MAME: dest[0] = 'L'; 'B' only when the big-endian pass won)
        int winnerLen = Math.Min(leLen, beLen);
        var winner = leLen <= beLen ? leOut : beOut;

        var result = new byte[winnerLen + 1];
        result[0] = leLen <= beLen ? (byte)'L' : (byte)'B';
        Array.Copy(winner, 0, result, 1, winnerLen);
        return result;
    }

    private static void SwapPairs(byte[] source, byte[] dest)
    {
        for (int i = 0; i < source.Length; i += 2)
        {
            dest[i] = source[i + 1];
            dest[i + 1] = source[i];
        }
    }
}