namespace CHDSharpTester.Services;

/// <summary>Provides utility methods for formatting and inspecting byte arrays used in hash comparisons.</summary>
public static class HashUtil
{
    /// <summary>Converts a byte array to a lowercase hexadecimal string.</summary>
    /// <param name="a">The byte array to convert, or null.</param>
    /// <returns>A lowercase hex string, or "(none)" if the array is null.</returns>
    public static string ToHex(byte[]? a)
    {
        if (a == null) return "(none)";

        return Convert.ToHexString(a).ToLowerInvariant();
    }

    /// <summary>Checks whether every byte in the array is zero.</summary>
    /// <param name="a">The byte array to inspect.</param>
    /// <returns><c>true</c> if all bytes are zero; otherwise <c>false</c>.</returns>
    public static bool IsAllZero(byte[] a)
    {
        foreach (var b in a) if (b != 0) return false;

        return true;
    }
}
