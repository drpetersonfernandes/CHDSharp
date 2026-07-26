namespace CHDSharpEncoder;

/// <summary>Defines CHD v5 codec tag constants and conversion utilities.</summary>
public static class CodecTags
{
    /// <summary>Zlib (deflate) compression codec tag.</summary>
    public const uint ZLIB = 0x7A6C6962; // 'zlib' in big-endian
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
