namespace CHDSharp.Utils;

/// <summary>A CRC-32 calculator using the ISO 3309 / ITU-T V.42 polynomial, with an 8-table slicing implementation for performance.</summary>
internal class Crc
{
    /// <summary>Precomputed CRC-32 lookup tables (8 tables, 256 entries each) for fast slicing.</summary>
    public static readonly uint[] Crc32Lookup;
    private uint _crc;
    private long _totalBytesRead;

    static Crc()
    {
        const uint polynomial = 0xEDB88320;
        const int crcNumTables = 8;

        unchecked
        {
            Crc32Lookup = new uint[256 * crcNumTables];
            int i;
            for (i = 0; i < 256; i++)
            {
                var r = (uint)i;
                for (var j = 0; j < 8; j++)
                {
                    r = (r >> 1) ^ (polynomial & ~((r & 1) - 1));
                }

                Crc32Lookup[i] = r;
            }

            for (; i < 256 * crcNumTables; i++)
            {
                var r = Crc32Lookup[i - 256];
                Crc32Lookup[i] = Crc32Lookup[r & 0xFF] ^ (r >> 8);
            }
        }
    }


    /// <summary>Initializes a new instance of the <see cref="Crc"/> class and resets the internal state.</summary>
    public Crc()
    {
        Reset();
    }

    /// <summary>Resets the CRC-32 state to its initial value and clears the byte count.</summary>
    public void Reset()
    {
        _totalBytesRead = 0;
        _crc = 0xffffffffu;
    }


    /// <summary>Processes a block of data through the CRC-32 algorithm using 8-byte slicing where possible.</summary>
    /// <param name="block">The byte array containing the data to process.</param>
    /// <param name="offset">The zero-based offset in <paramref name="block"/> at which to begin processing.</param>
    /// <param name="count">The number of bytes to process.</param>
    public void SlurpBlock(byte[] block, int offset, int count)
    {
        _totalBytesRead += count;
        var crc = _crc;

        for (; (offset & 7) != 0 && count != 0; count--)
        {
            crc = (crc >> 8) ^ Crc32Lookup[(byte)crc ^ block[offset++]];
        }

        if (count >= 8)
        {
            var end = (count - 8) & ~7;
            count -= end;
            end += offset;

            while (offset != end)
            {
                crc ^= (uint)(block[offset] + (block[offset + 1] << 8) + (block[offset + 2] << 16) + (block[offset + 3] << 24));
                var high = (uint)(block[offset + 4] + (block[offset + 5] << 8) + (block[offset + 6] << 16) + (block[offset + 7] << 24));
                offset += 8;

                crc = Crc32Lookup[(byte)crc + 0x700]
                      ^ Crc32Lookup[(byte)(crc >>= 8) + 0x600]
                      ^ Crc32Lookup[(byte)(crc >>= 8) + 0x500]
                      ^ Crc32Lookup[ /*(byte)*/(crc >> 8) + 0x400]
                      ^ Crc32Lookup[(byte)high + 0x300]
                      ^ Crc32Lookup[(byte)(high >>= 8) + 0x200]
                      ^ Crc32Lookup[(byte)(high >>= 8) + 0x100]
                      ^ Crc32Lookup[ /*(byte)*/(high >> 8) + 0x000];
            }
        }

        while (count-- != 0)
        {
            crc = (crc >> 8) ^ Crc32Lookup[(byte)crc ^ block[offset++]];
        }

        _crc = crc;
    }

    /// <summary>Gets the CRC-32 result as an unsigned 32-bit integer.</summary>
    public uint Crc32ResultU => ~_crc;

    /// <summary>Calculates the CRC-32 digest of a data range without mutating instance state.</summary>
    /// <param name="data">The byte array containing the data.</param>
    /// <param name="offset">The zero-based offset in <paramref name="data"/> at which to start.</param>
    /// <param name="size">The number of bytes to process.</param>
    /// <returns>The CRC-32 digest as an unsigned 32-bit integer.</returns>
    public static uint CalculateDigest(byte[] data, uint offset, uint size)
    {
        var crc = new Crc();
        crc.SlurpBlock(data, (int)offset, (int)size);
        return crc.Crc32ResultU;
    }

    /// <summary>Verifies that a CRC-32 digest matches the computed digest of a data range.</summary>
    /// <param name="digest">The expected CRC-32 digest.</param>
    /// <param name="data">The byte array containing the data.</param>
    /// <param name="offset">The zero-based offset in <paramref name="data"/> at which to start.</param>
    /// <param name="size">The number of bytes to process.</param>
    /// <returns><c>true</c> if the computed digest matches <paramref name="digest"/>; otherwise <c>false</c>.</returns>
    public static bool VerifyDigest(uint digest, byte[] data, uint offset, uint size)
    {
        return (CalculateDigest(data, offset, size) == digest);
    }
}