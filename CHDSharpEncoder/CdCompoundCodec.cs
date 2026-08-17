using CHDSharpEncoder.Interfaces;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoder;

/// <summary>
/// Shared implementation for the CD compound codecs ('cdzl', 'cdlz', 'cdzs'), matching
/// MAME's <c>chd_cd_compressor&lt;BaseCompressor, SubcodeCompressor&gt;</c>: frames are
/// deinterleaved into a data portion (frames × 2352) and a subcode portion (frames × 96);
/// sectors with a valid sync header and ECC get their sync header + ECC cleared and a
/// bitmap bit set (the decompressor regenerates them). The result is
/// <c>[ecc bitmap][2/3-byte base length][base compressed][subcode compressed]</c>.
/// </summary>
public abstract class CdCompoundCodec : IChdCodec
{
    private readonly int _framesPerHunk;
    private readonly int _dataBytes;
    private readonly int _subcodeBytes;
    private readonly IChdCodec _baseCodec;
    private readonly IChdCodec _subcodeCodec;
    private readonly byte[] _dataBuffer;
    private readonly byte[] _subcodeBuffer;
    private readonly byte[] _eccBitmap;

    /// <inheritdoc/>
    public abstract uint Tag { get; }

    /// <summary>Creates a CD compound codec for CD-sized hunks.</summary>
    /// <param name="hunkBytes">Hunk size in bytes; must be a multiple of the CD frame size.</param>
    /// <param name="baseCodec">Codec for the data portion (frames × 2352 bytes).</param>
    /// <param name="subcodeCodec">Codec for the subcode portion (frames × 96 bytes).</param>
    protected CdCompoundCodec(uint hunkBytes, IChdCodec baseCodec, IChdCodec subcodeCodec)
    {
        if (hunkBytes % CdConstants.FrameSize != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of the CD frame size ({CdConstants.FrameSize})");

        _framesPerHunk = (int)(hunkBytes / CdConstants.FrameSize);
        _dataBytes = _framesPerHunk * CdConstants.MaxSectorData;
        _subcodeBytes = _framesPerHunk * CdConstants.MaxSubcodeData;
        _baseCodec = baseCodec;
        _subcodeCodec = subcodeCodec;
        _dataBuffer = new byte[_dataBytes];
        _subcodeBuffer = new byte[_subcodeBytes];
        _eccBitmap = new byte[(_framesPerHunk + 7) / 8];
    }

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        if (data.Length != _framesPerHunk * CdConstants.FrameSize)
            throw new ArgumentException($"hunk size mismatch: expected {_framesPerHunk * CdConstants.FrameSize}, got {data.Length}");

        int complenBytes = data.Length < 65536 ? 2 : 3;
        int eccBytes = _eccBitmap.Length;
        int headerBytes = eccBytes + complenBytes;

        Array.Clear(_eccBitmap);

        // deinterleave frames; clear sync + ECC for sectors that have a valid sync
        // header and ECC (mirrors MAME's chd_cd_compressor::compress)
        for (int f = 0; f < _framesPerHunk; f++)
        {
            int src = f * CdConstants.FrameSize;
            Array.Copy(data, src, _dataBuffer, f * CdConstants.MaxSectorData, CdConstants.MaxSectorData);
            Array.Copy(data, src + CdConstants.MaxSectorData, _subcodeBuffer, f * CdConstants.MaxSubcodeData, CdConstants.MaxSubcodeData);

            if (data.AsSpan(src, CdEcc.SyncHeader.Length).SequenceEqual(CdEcc.SyncHeader) && CdEcc.EccVerify(data, src))
            {
                _eccBitmap[f / 8] |= (byte)(1 << (f % 8));
                CdEcc.EccClear(_dataBuffer, f * CdConstants.MaxSectorData);
            }
        }

        // encode the base portion
        var baseData = _baseCodec.Compress(_dataBuffer);
        if (baseData == null)
            return null;

        // encode the subcode
        var subcodeData = _subcodeCodec.Compress(_subcodeBuffer);
        if (subcodeData == null)
            return null;

        int total = headerBytes + baseData.Length + subcodeData.Length;
        if (total >= data.Length)
            return null;

        var result = new byte[total];
        Array.Copy(_eccBitmap, 0, result, 0, eccBytes);

        // write the compressed length of the base portion
        if (complenBytes > 2)
        {
            result[eccBytes + 0] = (byte)(baseData.Length >> 16);
            result[eccBytes + 1] = (byte)(baseData.Length >> 8);
            result[eccBytes + 2] = (byte)baseData.Length;
        }
        else
        {
            result[eccBytes + 0] = (byte)(baseData.Length >> 8);
            result[eccBytes + 1] = (byte)baseData.Length;
        }

        Array.Copy(baseData, 0, result, headerBytes, baseData.Length);
        Array.Copy(subcodeData, 0, result, headerBytes + baseData.Length, subcodeData.Length);
        return result;
    }
}

/// <summary>CD zlib codec ('cdzl'): deflate for sector data and subcode.</summary>
public sealed class CdzlCodec : CdCompoundCodec
{
    /// <summary>Creates a CD zlib codec for CD-sized hunks.</summary>
    /// <param name="hunkBytes">Hunk size in bytes; must be a multiple of the CD frame size.</param>
    public CdzlCodec(uint hunkBytes)
        : base(hunkBytes, new ZlibCodec(), new ZlibCodec())
    {
    }

    /// <inheritdoc/>
    public override uint Tag => CodecTags.Cdzl;
}

/// <summary>CD LZMA codec ('cdlz'): LZMA for sector data, deflate for subcode (MAME parity).</summary>
public sealed class CdlzCodec : CdCompoundCodec
{
    /// <summary>Creates a CD LZMA codec for CD-sized hunks.</summary>
    /// <param name="hunkBytes">Hunk size in bytes; must be a multiple of the CD frame size.</param>
    public CdlzCodec(uint hunkBytes)
        : base(hunkBytes, new LzmaCodec(hunkBytes / CdConstants.FrameSize * CdConstants.MaxSectorData), new ZlibCodec())
    {
    }

    /// <inheritdoc/>
    public override uint Tag => CodecTags.Cdlz;
}

/// <summary>CD Zstandard codec ('cdzs'): zstd for sector data and subcode.</summary>
public sealed class CdzsCodec : CdCompoundCodec
{
    /// <summary>Creates a CD Zstandard codec for CD-sized hunks.</summary>
    /// <param name="hunkBytes">Hunk size in bytes; must be a multiple of the CD frame size.</param>
    public CdzsCodec(uint hunkBytes)
        : base(hunkBytes, new ZstdCodec(), new ZstdCodec())
    {
    }

    /// <inheritdoc/>
    public override uint Tag => CodecTags.Cdzs;
}