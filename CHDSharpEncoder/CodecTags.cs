namespace CHDSharpEncoder;

public static class CodecTags
{
    public const uint ZLIB = 0x7A6C6962; // 'zlib' in big-endian
    public const uint NONE = 0x00000000;

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
