namespace CHDSharp.Utils;

/// <summary>A CRC-32 calculator using the ISO 3309 / ITU-T V.42 polynomial, with an 8-table slicing implementation for performance.</summary>
public class CRC
{
    /// <summary>Precomputed CRC-32 lookup tables (8 tables, 256 entries each) for fast slicing.</summary>
    public static readonly uint[] CRC32Lookup;
    /// <summary>The running CRC-32 state value.</summary>
    private uint _crc;
    /// <summary>The total number of bytes processed since the last reset.</summary>
    private long _totalBytesRead;

    static CRC()
    {
        const uint polynomial = 0xEDB88320;
        const int crcNumTables = 8;

        unchecked
        {
            CRC32Lookup = new uint[256 * crcNumTables];
            int i;
            for (i = 0; i < 256; i++)
            {
                var r = (uint)i;
                for (var j = 0; j < 8; j++)
                {
                    r = (r >> 1) ^ (polynomial & ~((r & 1) - 1));
                }

                CRC32Lookup[i] = r;
            }

            for (; i < 256 * crcNumTables; i++)
            {
                var r = CRC32Lookup[i - 256];
                CRC32Lookup[i] = CRC32Lookup[r & 0xFF] ^ (r >> 8);
            }
        }
    }


    /// <summary>Initializes a new instance of the <see cref="CRC"/> class and resets the internal state.</summary>
    public CRC()
    {
        Reset();
    }

    /// <summary>Resets the CRC-32 state to its initial value and clears the byte count.</summary>
    public void Reset()
    {
        _totalBytesRead = 0;
        _crc = 0xffffffffu;
    }


    /// <summary>Updates the CRC-32 state with a single byte value.</summary>
    /// <param name="inCh">The byte value to process.</param>
    internal void UpdateCRC(int inCh)
    {
        _crc = (_crc >> 8) ^ CRC32Lookup[(byte)_crc ^ ((byte)inCh)];
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
            crc = (crc >> 8) ^ CRC32Lookup[(byte)crc ^ block[offset++]];
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

                crc = CRC32Lookup[(byte)crc + 0x700]
                      ^ CRC32Lookup[(byte)(crc >>= 8) + 0x600]
                      ^ CRC32Lookup[(byte)(crc >>= 8) + 0x500]
                      ^ CRC32Lookup[ /*(byte)*/(crc >> 8) + 0x400]
                      ^ CRC32Lookup[(byte)high + 0x300]
                      ^ CRC32Lookup[(byte)(high >>= 8) + 0x200]
                      ^ CRC32Lookup[(byte)(high >>= 8) + 0x100]
                      ^ CRC32Lookup[ /*(byte)*/(high >> 8) + 0x000];
            }
        }

        while (count-- != 0)
        {
            crc = (crc >> 8) ^ CRC32Lookup[(byte)crc ^ block[offset++]];
        }

        _crc = crc;

    }

    /// <summary>Gets the CRC-32 result as a big-endian byte array.</summary>
    public byte[] Crc32ResultB
    {
        get
        {
            var result = BitConverter.GetBytes(~_crc);
            Array.Reverse(result);
            return result;
        }
    }
    /// <summary>Gets the CRC-32 result as a signed 32-bit integer.</summary>
    public int Crc32Result => unchecked((int)(~_crc));

    /// <summary>Gets the CRC-32 result as an unsigned 32-bit integer.</summary>
    public uint Crc32ResultU => ~_crc;

    /// <summary>Gets the total number of bytes processed since the last <see cref="Reset"/>.</summary>
    public long TotalBytesRead => _totalBytesRead;

    /// <summary>Calculates the CRC-32 digest of a data range without mutating instance state.</summary>
    /// <param name="data">The byte array containing the data.</param>
    /// <param name="offset">The zero-based offset in <paramref name="data"/> at which to start.</param>
    /// <param name="size">The number of bytes to process.</param>
    /// <returns>The CRC-32 digest as an unsigned 32-bit integer.</returns>
    public static uint CalculateDigest(byte[] data, uint offset, uint size)
    {
        var crc = new CRC();
        // crc.Init();
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