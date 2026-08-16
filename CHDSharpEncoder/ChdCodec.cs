using System.IO.Compression;
using SharpCompress.Compressors.LZMA;
using ZstdSharp;

namespace CHDSharpEncoder;

/// <summary>Defines CHD v5 codec tag constants and conversion utilities.</summary>
public static class CodecTags
{
    /// <summary>Zlib (deflate) compression codec tag.</summary>
    public const uint ZLIB = 0x7A6C6962; // 'zlib' in big-endian
    /// <summary>Zstandard compression codec tag.</summary>
    public const uint ZSTD = 0x7A737464; // 'zstd'
    /// <summary>LZMA compression codec tag.</summary>
    public const uint LZMA = 0x6C7A6D61; // 'lzma'
    /// <summary>Huffman (MAME generic) codec tag (not implemented by the encoder).</summary>
    public const uint HUFF = 0x68756666; // 'huff'
    /// <summary>FLAC (audio) codec tag (not implemented by the encoder).</summary>
    public const uint FLAC = 0x666C6163; // 'flac'
    /// <summary>CD zlib codec tag (not implemented by the encoder; plain zlib works on CD hunks).</summary>
    public const uint CDZL = 0x63647A6C; // 'cdzl'
    /// <summary>CD FLAC codec tag (not implemented by the encoder).</summary>
    public const uint CDFL = 0x6364666C; // 'cdfl'
    /// <summary>No-compression codec tag.</summary>
    public const uint NONE = 0x00000000;

    /// <summary>Converts a 32-bit codec tag to a four-character ASCII string.</summary>
    /// <param name="tag">The codec tag value.</param>
    /// <returns>A 4-character string representation of the tag.</returns>
    public static string ToString(uint tag)
    {
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)((tag >> 24) & 0xFF);
        chars[1] = (char)((tag >> 16) & 0xFF);
        chars[2] = (char)((tag >> 8) & 0xFF);
        chars[3] = (char)(tag & 0xFF);
        return new string(chars);
    }
}

/// <summary>A hunk compression codec; compression type 0-3 in the map maps to codecs[0-3].</summary>
public interface IChdCodec
{
    /// <summary>The four-character codec tag (see <see cref="CodecTags"/>).</summary>
    uint Tag { get; }

    /// <summary>Compresses a full hunk. Returns <c>null</c> when the codec does not reduce the size.</summary>
    byte[]? Compress(byte[] data);
}

/// <summary>Zlib compression (raw DEFLATE), matching <c>chdman -c zlib</c>.</summary>
public sealed class ZlibCodec : IChdCodec
{
    /// <summary>The codec tag.</summary>
    public uint Tag => CodecTags.ZLIB;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        using var ms = new MemoryStream(data.Length);
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            ds.Write(data, 0, data.Length);
        }

        var result = ms.ToArray();
        if (result.Length >= 2 && result[0] == 0x78)
        {
            // .NET Framework-era zlib wrapper; strip 2-byte header + 4-byte Adler32 trailer
            result = result.AsSpan(2, result.Length - 6).ToArray();
        }

        return result.Length < data.Length ? result : null;
    }
}

/// <summary>
/// Zstandard compression at zstd's maximum level, matching MAME's
/// <c>chd_zstd_compressor</c> (<c>ZSTD_maxCLevel()</c>).
/// </summary>
public sealed class ZstdCodec : IChdCodec
{
    private readonly ZstdSharp.Compressor _compressor = new(ZstdSharp.Compressor.MaxCompressionLevel);

    /// <inheritdoc/>
    public uint Tag => CodecTags.ZSTD;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        // the returned span is only valid until the next Wrap call; copy immediately
        var result = _compressor.Wrap(data);
        return result.Length < data.Length ? result.ToArray() : null;
    }
}

/// <summary>
/// LZMA compression (level-8 equivalent settings), matching MAME's
/// <c>chd_lzma_compressor</c>: raw headerless LZMA with no end marker, properties
/// lc=3/lp=0/pb=2 and dictionary size = hunk bytes. SharpCompress's LzmaStream encoder
/// computes the properties but never writes them to the output, so the stream is already
/// in CHD's raw format; the decoder synthesizes the properties (see CHDSharpLib's
/// CHDReaders.Lzma).
/// </summary>
public sealed class LzmaCodec : IChdCodec
{
    private readonly LzmaEncoderProperties _properties;

    /// <summary>Creates an LZMA codec for the given hunk size.</summary>
    /// <param name="hunkBytes">Hunk size in bytes (becomes the LZMA dictionary size).</param>
    public LzmaCodec(uint hunkBytes)
    {
        // endMarker: false; dictionary size limited to the hunk size so back-references
        // never exceed what the decoder's per-hunk dictionary buffer provides
        _properties = new LzmaEncoderProperties(false, (int)hunkBytes);
    }

    /// <inheritdoc/>
    public uint Tag => CodecTags.LZMA;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        using var ms = new MemoryStream(data.Length / 2);
        using (var lzma = new LzmaStream(_properties, false, ms))
        {
            lzma.Write(data, 0, data.Length);
        }

        var result = ms.ToArray();
        return result.Length < data.Length ? result : null;
    }
}

/// <summary>Creates codec instances from four-character tags.</summary>
public static class ChdCodecs
{
    /// <summary>
    /// Creates one codec instance per tag, in order (up to 4, per the CHD header).
    /// Unsupported tags produce a codec that always fails to compress, so hunks fall
    /// back to COMPRESSION_NONE instead of corrupting the file.
    /// </summary>
    /// <param name="codecTags">The codec tags to instantiate.</param>
    /// <param name="hunkBytes">The hunk size in bytes (codec configuration).</param>
    /// <returns>An array of up to 4 codec instances.</returns>
    public static IChdCodec[] CreateAll(IReadOnlyList<uint> codecTags, uint hunkBytes)
    {
        var result = new List<IChdCodec>(Math.Min(codecTags.Count, 4));
        foreach (var tag in codecTags.Take(4))
        {
            IChdCodec codec = tag switch
            {
                CodecTags.ZLIB => new ZlibCodec(),
                CodecTags.ZSTD => new ZstdCodec(),
                CodecTags.LZMA => new LzmaCodec(hunkBytes),
                // CD FLAC only applies to CD-sized hunks (whole frames); elsewhere it can't compress
                CodecTags.CDFL when hunkBytes % CdConstants.FrameSize == 0 => new CdflCodec(hunkBytes),
                _ => new UnsupportedCodec(tag),
            };
            result.Add(codec);
        }
        return result.ToArray();
    }

    /// <summary>Parses a comma-separated codec list ("zlib,zstd,lzma") into tags.</summary>
    /// <param name="codecString">The comma-separated codec names.</param>
    /// <returns>The parsed codec tags.</returns>
    /// <exception cref="ArgumentException">An unknown codec name was supplied.</exception>
    public static uint[] ParseCodecTags(string? codecString)
    {
        if (string.IsNullOrWhiteSpace(codecString))
            return [CodecTags.ZLIB];

        var tags = new List<uint>();
        foreach (var name in codecString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            tags.Add(name.ToLowerInvariant() switch
            {
                "zlib" => CodecTags.ZLIB,
                "zstd" => CodecTags.ZSTD,
                "lzma" => CodecTags.LZMA,
                "cdfl" => CodecTags.CDFL,
                "none" => CodecTags.NONE,
                _ => throw new ArgumentException($"Unknown codec [{name}]"),
            });
        }
        return tags.ToArray();
    }

    private sealed class UnsupportedCodec : IChdCodec
    {
        public UnsupportedCodec(uint tag)
        {
            Tag = tag;
        }

        public uint Tag { get; }

        public byte[]? Compress(byte[] data)
        {
            return null;
        }
    }
}