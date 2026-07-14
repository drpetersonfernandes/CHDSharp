namespace CHDSharpTester.Services;

public static class HashUtil
{
    public static string ToHex(byte[]? a)
    {
        if (a == null) return "(none)";
        return Convert.ToHexString(a).ToLowerInvariant();
    }

    public static bool IsAllZero(byte[] a)
    {
        foreach (var b in a) if (b != 0) return false;
        return true;
    }
}
