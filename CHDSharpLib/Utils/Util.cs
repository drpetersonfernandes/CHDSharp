namespace CHDSharp.Utils;

/// <summary>General-purpose utility methods for byte array comparisons, hashing, and ASCII detection used throughout the CHD reader.</summary>
internal static class Util
{
    /// <summary>Determines whether every byte in the array is zero (or the array is null).</summary>
    /// <param name="b">The byte array to check.</param>
    /// <returns><c>true</c> if the array is null or all bytes are zero; otherwise <c>false</c>.</returns>
    internal static bool IsAllZeroArray(byte[] b)
    {
        if (b == null) return true;

        for (var i = 0; i < b.Length; i++)
            if (b[i] != 0) return false;

        return true;
    }

    /// <summary>Compares two byte arrays for exact equality.</summary>
    /// <returns><c>true</c> if both arrays are non-null and contain identical bytes; otherwise <c>false</c>.</returns>
    internal static bool ByteArrEquals(byte[] b0, byte[] b1)
    {
        if ((b0 == null) || (b1 == null))
        {
            return false;
        }
        if (b0.Length != b1.Length)
        {
            return false;
        }

        for (var i = 0; i < b0.Length; i++)
        {
            if (b0[i] != b1[i])
            {
                return false;
            }
        }
        return true;
    }


    /// <summary>Lexicographically compares two byte arrays for use in sorting.</summary>
    /// <returns>A negative value if <paramref name="x"/> is less than <paramref name="y"/>, zero if equal, or positive if greater.</returns>
    internal static int ByteArrCompare(byte[] x, byte[] y)
    {
        for (var i = 0; i < x.Length; i++)
        {
            var v = x[i].CompareTo(y[i]);
            if (v != 0)
                return v;
        }
        return 0;
    }

    /// <summary>Checks whether the byte array contains only printable ASCII characters (including null bytes).</summary>
    internal static bool isAscii(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            if (b != 0 && b < 32)
                return false;
        }
        return true;
    }
}
