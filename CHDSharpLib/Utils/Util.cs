namespace CHDSharp.Utils;

internal static class Util
{
    internal static bool IsAllZeroArray(byte[] b)
    {
        if (b == null) return true;

        for (var i = 0; i < b.Length; i++)
            if (b[i] != 0) return false;

        return true;
    }

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
