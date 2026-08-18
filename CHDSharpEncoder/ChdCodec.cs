using System.IO.Compression;
using CHDSharpEncoder.Interfaces;
using CHDSharpEncoder.Models;
using SharpCompress.Compressors.LZMA;

namespace CHDSharpEncoder;

/// <summary>Defines CHD v5 codec tag constants and conversion utilities.</summary>
public static class CodecTags
{
    /// <summary>Zlib (deflate) compression codec tag.</summary>
    public const uint Zlib = 0x7A6C6962; // 'zlib' in big-endian

    /// <summary>Zstandard compression codec tag.</summary>
    public const uint Zstd = 0x7A737464; // 'zstd'

    /// <summary>LZMA compression codec tag.</summary>
    public const uint Lzma = 0x6C7A6D61; // 'lzma'

    /// <summary>Huffman (MAME generic) codec tag (recognized but not implemented by the encoder yet).</summary>
    public const uint Huff = 0x68756666; // 'huff'

    /// <summary>FLAC (audio) codec tag (recognized but not implemented by the encoder yet).</summary>
    public const uint Flac = 0x666C6163; // 'flac'

    /// <summary>CD zlib codec tag (recognized but not implemented by the encoder yet).</summary>
    public const uint Cdzl = 0x63647A6C; // 'cdzl'

    /// <summary>CD LZMA codec tag (recognized but not implemented by the encoder yet).</summary>
    public const uint Cdlz = 0x63646C7A; // 'cdlz'

    /// <summary>CD Zstandard codec tag (recognized but not implemented by the encoder yet).</summary>
    public const uint Cdzs = 0x63647A73; // 'cdzs'

    /// <summary>CD FLAC codec tag (implemented by the encoder; CD-sized hunks only).</summary>
    public const uint Cdfl = 0x6364666C; // 'cdfl'

    /// <summary>No-compression codec tag (recognized but not supported by the encoder yet).</summary>
    public const uint None = 0x00000000;

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

    /// <summary>Converts a codec name ("zlib", "huff", ...) to its tag.</summary>
    /// <param name="name">The codec name.</param>
    /// <returns>The codec tag.</returns>
    /// <exception cref="ArgumentException">An unknown codec name was supplied.</exception>
    public static uint FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.ToLowerInvariant() switch
        {
            "zlib" => Zlib,
            "zstd" => Zstd,
            "lzma" => Lzma,
            "huff" => Huff,
            "flac" => Flac,
            "cdzl" => Cdzl,
            "cdlz" => Cdlz,
            "cdzs" => Cdzs,
            "cdfl" => Cdfl,
            "none" => None,
            _ => throw new ArgumentException($"Unknown codec [{name}]")
        };
    }
}

/// <summary>Zlib compression (raw DEFLATE), matching <c>chdman -c zlib</c>.</summary>
public sealed class ZlibCodec : IChdCodec
{
    /// <summary>The codec tag.</summary>
    public uint Tag => CodecTags.Zlib;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        using var ms = new MemoryStream(data.Length);
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            ds.Write(data, 0, data.Length);
        }

        var result = ms.ToArray();
        if (result is [0x78, _, ..])
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
    public uint Tag => CodecTags.Zstd;

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
    public uint Tag => CodecTags.Lzma;

    /// <inheritdoc/>
    public byte[]? Compress(byte[] data)
    {
        using var ms = new MemoryStream(data.Length / 2);
        using (var lzma = LzmaStream.Create(_properties, false, ms))
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
    /// <summary>Comma-separated list of codec names the encoder can actually compress with.</summary>
    public const string SupportedCodecNames = "zlib, zstd, lzma, huff, flac, cdzl, cdlz, cdzs, cdfl, none";

    /// <summary>
    /// Creates one codec instance per tag, in order (up to 4, per the CHD header).
    /// The single tag <see cref="CodecTags.None"/> produces an empty codec list (uncompressed
    /// CHD: hunks are stored raw and the encoder writes the V5 raw map instead of the
    /// Huffman-compressed one). Any other request that the encoder does not implement throws
    /// instead of silently degrading: a requested codec that the encoder cannot write would
    /// otherwise store every hunk uncompressed while the header claims the codec is in use.
    /// </summary>
    /// <param name="codecTags">The codec tags to instantiate.</param>
    /// <param name="hunkBytes">The hunk size in bytes (codec configuration).</param>
    /// <returns>An array of codec instances (empty when the single tag is <see cref="CodecTags.None"/>).</returns>
    /// <exception cref="ArgumentException">A tag is unknown, not implemented by the encoder, combined
    /// with other codecs, or (<see cref="CodecTags.Cdfl"/>) used on non-CD-sized hunks.</exception>
    public static IChdCodec[] CreateAll(IReadOnlyList<uint> codecTags, uint hunkBytes)
    {
        ArgumentNullException.ThrowIfNull(codecTags);
        switch (codecTags.Count)
        {
            case 0:
                throw new ArgumentException("At least one codec is required; use 'zlib', 'zstd', 'lzma', 'cdfl' or 'none'", nameof(codecTags));
            case > 4:
                throw new ArgumentException($"At most 4 codecs are supported, got {codecTags.Count}", nameof(codecTags));
        }

        if (codecTags is [CodecTags.None])
            return [];

        var result = new List<IChdCodec>(codecTags.Count);
        foreach (var tag in codecTags)
        {
            if (tag == CodecTags.None)
                throw new ArgumentException("Codec 'none' cannot be combined with other codecs", nameof(codecTags));

            IChdCodec codec = tag switch
            {
                CodecTags.Zlib => new ZlibCodec(),
                CodecTags.Zstd => new ZstdCodec(),
                CodecTags.Lzma => new LzmaCodec(hunkBytes),
                CodecTags.Huff => new HuffCodec(),
                CodecTags.Flac => new FlacCodec(hunkBytes),
                // CD codecs only apply to CD-sized hunks (whole frames); elsewhere they can't compress
                CodecTags.Cdfl when hunkBytes % CdConstants.FrameSize == 0 => new CdflCodec(hunkBytes),
                CodecTags.Cdzl or CodecTags.Cdlz or CodecTags.Cdzs when hunkBytes % CdConstants.FrameSize == 0 =>
                    CreateCdCodec(tag, hunkBytes),
                CodecTags.Cdfl or CodecTags.Cdzl or CodecTags.Cdlz or CodecTags.Cdzs => throw new ArgumentException(
                    $"Codec '{CodecTags.ToString(tag)}' requires CD-sized hunks (multiple of {CdConstants.FrameSize} bytes); hunk is {hunkBytes} bytes",
                    nameof(codecTags)),
                _ => throw new ArgumentException(
                    $"Unknown codec tag [{CodecTags.ToString(tag)}]; supported codecs: {SupportedCodecNames}",
                    nameof(codecTags))
            };
            result.Add(codec);
        }

        return result.ToArray();
    }

    private static IChdCodec CreateCdCodec(uint tag, uint hunkBytes)
    {
        return tag switch
        {
            CodecTags.Cdzl => new CdzlCodec(hunkBytes),
            CodecTags.Cdlz => new CdlzCodec(hunkBytes),
            CodecTags.Cdzs => new CdzsCodec(hunkBytes),
            _ => throw new ArgumentException($"Unknown CD codec tag [{CodecTags.ToString(tag)}]")
        };
    }

    /// <summary>Parses a comma-separated codec list ("zlib,zstd,lzma") into tags.</summary>
    /// <param name="codecString">The comma-separated codec names.</param>
    /// <returns>The parsed codec tags.</returns>
    /// <exception cref="ArgumentException">An unknown codec name was supplied.</exception>
    public static uint[] ParseCodecTags(string? codecString)
    {
        if (string.IsNullOrWhiteSpace(codecString))
            return [CodecTags.Zlib];

        var tags = new List<uint>();
        foreach (var name in codecString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            tags.Add(name.ToLowerInvariant() switch
            {
                "zlib" => CodecTags.Zlib,
                "zstd" => CodecTags.Zstd,
                "lzma" => CodecTags.Lzma,
                "huff" => CodecTags.Huff,
                "flac" => CodecTags.Flac,
                "cdzl" => CodecTags.Cdzl,
                "cdlz" => CodecTags.Cdlz,
                "cdzs" => CodecTags.Cdzs,
                "cdfl" => CodecTags.Cdfl,
                "none" => CodecTags.None,
                _ => throw new ArgumentException($"Unknown codec [{name}]")
            });
        }

        return tags.ToArray();
    }
}