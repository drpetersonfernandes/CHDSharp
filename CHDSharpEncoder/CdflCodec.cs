using CHDSharpEncoder.Flac;

namespace CHDSharpEncoder;

/// <summary>
/// CD FLAC codec ('cdfl'), matching MAME's <c>chd_cd_flac_compressor</c>: the CD audio
/// portion (frames × 2352 bytes) is encoded as raw FLAC frames (no stream header,
/// little-endian samples, 2ch/16-bit/44100 Hz, 2352 samples per frame — MAME's cdfl
/// blocksize) and the subcode portion (frames × 96 bytes) is deflated and appended. The
/// result is <c>[FLAC frames][deflated subcode]</c>, decodable by MAME/chdman and
/// CHDSharpLib.
/// </summary>
public sealed class CdflCodec : IChdCodec
{
    private readonly int _framesPerHunk;
    private readonly int _dataBytes;
    private readonly int _subcodeBytes;
    private readonly byte[] _leBuffer;
    private readonly byte[] _flacBuffer;

    /// <summary>Creates a CD FLAC codec for CD-sized hunks.</summary>
    /// <param name="hunkBytes">Hunk size in bytes; must be a multiple of the CD frame size.</param>
    public CdflCodec(uint hunkBytes)
    {
        if (hunkBytes % CdConstants.FrameSize != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of the CD frame size ({CdConstants.FrameSize})");

        _framesPerHunk = (int)(hunkBytes / CdConstants.FrameSize);
        _dataBytes = _framesPerHunk * CdConstants.MaxSectorData;
        _subcodeBytes = _framesPerHunk * CdConstants.MaxSubcodeData;
        _leBuffer = new byte[_dataBytes];
        // worst case: verbatim subframes; add room for frame headers
        _flacBuffer = new byte[_dataBytes + _framesPerHunk * 64];
    }

    /// <inheritdoc/>
    public uint Tag => CodecTags.CDFL;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        // deinterleave the frames: the CHD hunk interleaves data and subcode per frame
        // ([2352 data][96 subcode] per frame); FLAC sees the data portion and zlib the
        // subcode portion, each contiguous (mirrors MAME's chd_cd_flac_compressor)
        var subcode = new byte[_subcodeBytes];
        for (int f = 0; f < _framesPerHunk; f++)
        {
            int src = f * CdConstants.FrameSize;
            Array.Copy(data, src, _leBuffer, f * CdConstants.MaxSectorData, CdConstants.MaxSectorData);
            Array.Copy(data, src + CdConstants.MaxSectorData, subcode, f * CdConstants.MaxSubcodeData, CdConstants.MaxSubcodeData);
        }

        // FLAC stores samples little-endian; CHD audio is big-endian, so swap
        for (int i = 0; i < _dataBytes; i += 2)
        {
            (_leBuffer[i], _leBuffer[i + 1]) = (_leBuffer[i + 1], _leBuffer[i]);
        }

        int flacLen = FlacFrameEncoder.Encode(_flacBuffer, _leBuffer);

        var compressedSubcode = RawDeflate.Compress(subcode);
        if (compressedSubcode == null)
            return null;

        int total = flacLen + compressedSubcode.Length;
        if (total >= data.Length)
            return null;

        var result = new byte[total];
        Array.Copy(_flacBuffer, 0, result, 0, flacLen);
        Array.Copy(compressedSubcode, 0, result, flacLen, compressedSubcode.Length);
        return result;
    }
}