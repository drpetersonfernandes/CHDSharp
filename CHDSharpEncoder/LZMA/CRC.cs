namespace CHDSharpEncoder.LZMA;

/// <summary>CRC-32 table and calculator (poly 0xEDB88320), ported from the LZMA SDK (public domain).</summary>
internal static class Crc
{
    /// <summary>Precomputed CRC-32 lookup table used by the binary-tree match finder hashing.</summary>
    internal static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        const uint kPoly = 0xEDB88320;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var r = i;
            for (var j = 0; j < 8; j++)
            {
                if ((r & 1) != 0)
                {
                    r = (r >> 1) ^ kPoly;
                }
                else
                {
                    r >>= 1;
                }
            }

            table[i] = r;
        }

        return table;
    }
}