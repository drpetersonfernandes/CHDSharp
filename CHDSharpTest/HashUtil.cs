using System;
using System.Security.Cryptography;

namespace CHDSharp.Tests;

internal static class HashUtil
{
    public static string ToHex(byte[] data)
        => data == null ? null : Convert.ToHexString(data).ToLowerInvariant();

    public static bool IsAllZero(byte[] data)
    {
        if (data == null) return true;
        foreach (byte b in data)
            if (b != 0) return false;
        return true;
    }
}
