namespace CHDSharp.Tests;

internal static class EndianHelpers
{
    internal static byte[] Be(uint v)
    {
        return [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    }

    internal static byte[] Be64(ulong v)
    {
        return
        [
            (byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32),
            (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v
        ];
    }
}
