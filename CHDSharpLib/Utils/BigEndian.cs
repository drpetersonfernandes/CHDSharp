namespace CHDSharp.Utils;


/// <summary>Provides extension methods for reading and writing big-endian byte order values from <see cref="BinaryReader"/> and <see cref="byte"/> arrays.</summary>
public static class BigEndian
{
    /// <summary>Reverses the byte order of the array in-place and returns the same array.</summary>
    /// <param name="b">The byte array to reverse.</param>
    /// <returns>A reference to <paramref name="b"/> after reversal.</returns>
    public static byte[] Reverse(this byte[] b)
    {
        Array.Reverse(b);
        return b;
    }

    /// <summary>Reads a big-endian <see cref="UInt16"/> from the stream.</summary>
    /// <param name="binRdr">The <see cref="BinaryReader"/> to read from.</param>
    /// <returns>The unsigned 16-bit value read in big-endian order.</returns>
    public static ushort ReadUInt16BE(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt16(binRdr.ReadBytesRequired(sizeof(ushort)).Reverse(), 0);
    }

    /// <summary>Reads a big-endian <see cref="Int16"/> from the stream.</summary>
    /// <param name="binRdr">The <see cref="BinaryReader"/> to read from.</param>
    /// <returns>The signed 16-bit value read in big-endian order.</returns>
    public static short ReadInt16BE(this BinaryReader binRdr)
    {
        return BitConverter.ToInt16(binRdr.ReadBytesRequired(sizeof(short)).Reverse(), 0);
    }


    /// <summary>Reads a big-endian <see cref="UInt32"/> from the stream.</summary>
    /// <param name="binRdr">The <see cref="BinaryReader"/> to read from.</param>
    /// <returns>The unsigned 32-bit value read in big-endian order.</returns>
    public static uint ReadUInt32BE(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt32(binRdr.ReadBytesRequired(sizeof(uint)).Reverse(), 0);
    }
    /// <summary>Reads a big-endian 48-bit unsigned integer from the stream into a <see cref="UInt64"/>.</summary>
    /// <param name="binRdr">The <see cref="BinaryReader"/> to read from.</param>
    /// <returns>The 48-bit value read in big-endian order, stored in a <see cref="UInt64"/>.</returns>
    public static ulong ReadUInt48BE(this BinaryReader binRdr)
    {
        return (ulong)((binRdr.ReadByte() << 40) | (binRdr.ReadByte() << 32) | (binRdr.ReadByte() << 24) | (binRdr.ReadByte() << 16) | (binRdr.ReadByte() << 8) | (binRdr.ReadByte() << 0));
    }

    /// <summary>Reads a big-endian <see cref="UInt64"/> from the stream.</summary>
    /// <param name="binRdr">The <see cref="BinaryReader"/> to read from.</param>
    /// <returns>The unsigned 64-bit value read in big-endian order.</returns>
    public static ulong ReadUInt64BE(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt64(binRdr.ReadBytesRequired(sizeof(ulong)).Reverse(), 0);
    }

    /// <summary>Reads a big-endian <see cref="Int32"/> from the stream.</summary>
    /// <param name="binRdr">The <see cref="BinaryReader"/> to read from.</param>
    /// <returns>The signed 32-bit value read in big-endian order.</returns>
    public static int ReadInt32BE(this BinaryReader binRdr)
    {
        return BitConverter.ToInt32(binRdr.ReadBytesRequired(sizeof(int)).Reverse(), 0);
    }

    /// <summary>Reads the exact number of bytes requested from the stream, throwing if fewer bytes are available.</summary>
    /// <param name="binRdr">The <see cref="BinaryReader"/> to read from.</param>
    /// <param name="byteCount">The number of bytes to read.</param>
    /// <returns>A byte array containing exactly <paramref name="byteCount"/> bytes.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the stream contains fewer than <paramref name="byteCount"/> bytes.</exception>
    public static byte[] ReadBytesRequired(this BinaryReader binRdr, int byteCount)
    {
        var result = binRdr.ReadBytes(byteCount);

        if (result.Length != byteCount)
            throw new EndOfStreamException(string.Format("{0} bytes required from stream, but only {1} returned.", byteCount, result.Length));

        return result;
    }





    /// <summary>Reads a big-endian 16-bit unsigned integer from a byte array at the specified offset.</summary>
    /// <param name="arr">The byte array to read from.</param>
    /// <param name="offset">The zero-based offset at which to begin reading.</param>
    /// <returns>The unsigned 16-bit value read in big-endian order.</returns>
    public static ushort ReadUInt16BE(this byte[] arr, int offset)
    {
        return (ushort)((arr[offset + 0] << 8) | arr[offset + 1]);
    }
    /// <summary>Reads a big-endian 24-bit unsigned integer from a byte array at the specified offset into a <see cref="uint"/>.</summary>
    /// <param name="arr">The byte array to read from.</param>
    /// <param name="offset">The zero-based offset at which to begin reading.</param>
    /// <returns>The 24-bit value read in big-endian order, stored in a <see cref="uint"/>.</returns>
    public static uint ReadUInt24BE(this byte[] arr, int offset)
    {
        return ((uint)arr[offset + 0] << 16) | ((uint)arr[offset + 1] << 8) | (uint)arr[offset + 2];
    }
    /// <summary>Reads a big-endian 32-bit unsigned integer from a byte array at the specified offset.</summary>
    /// <param name="arr">The byte array to read from.</param>
    /// <param name="offset">The zero-based offset at which to begin reading.</param>
    /// <returns>The unsigned 32-bit value read in big-endian order.</returns>
    public static uint ReadUInt32BE(this byte[] arr, int offset)
    {
        return ((uint)arr[offset + 0] << 24) | ((uint)arr[offset + 1] << 16) | ((uint)arr[offset + 2] << 8) | (uint)arr[offset + 3];
    }
    /// <summary>Reads a big-endian 48-bit unsigned integer from a byte array at the specified offset into a <see cref="ulong"/>.</summary>
    /// <param name="arr">The byte array to read from.</param>
    /// <param name="offset">The zero-based offset at which to begin reading.</param>
    /// <returns>The 48-bit value read in big-endian order, stored in a <see cref="ulong"/>.</returns>
    public static ulong ReadUInt48BE(this byte[] arr, int offset)
    {
        return ((ulong)arr[offset + 0] << 40) | ((ulong)arr[offset + 1] << 32) |
               ((ulong)arr[offset + 2] << 24) | ((ulong)arr[offset + 3] << 16) | ((ulong)arr[offset + 4] << 8) | (ulong)arr[offset + 5];
    }

    /// <summary>Writes a 16-bit unsigned integer in big-endian order to a byte array at the specified offset.</summary>
    /// <param name="arr">The byte array to write to.</param>
    /// <param name="offset">The zero-based offset at which to begin writing.</param>
    /// <param name="value">The value to write.</param>
    public static void PutUInt16BE(this byte[] arr, int offset, uint value)
    {
        arr[offset++] = (byte)((value >> 8) & 0xFF);
        arr[offset++] = (byte)((value >> 0) & 0xFF);
    }
    /// <summary>Writes a 24-bit unsigned integer in big-endian order to a byte array at the specified offset.</summary>
    /// <param name="arr">The byte array to write to.</param>
    /// <param name="offset">The zero-based offset at which to begin writing.</param>
    /// <param name="value">The value to write (only the lower 24 bits are used).</param>
    public static void PutUInt24BE(this byte[] arr, int offset, uint value)
    {
        arr[offset++] = (byte)((value >> 16) & 0xFF);
        arr[offset++] = (byte)((value >> 8) & 0xFF);
        arr[offset++] = (byte)((value >> 0) & 0xFF);
    }
    /// <summary>Writes a 48-bit unsigned integer in big-endian order to a byte array at the specified offset.</summary>
    /// <param name="arr">The byte array to write to.</param>
    /// <param name="offset">The zero-based offset at which to begin writing.</param>
    /// <param name="value">The value to write (only the lower 48 bits are used).</param>
    public static void PutUInt48BE(this byte[] arr, int offset, ulong value)
    {
        arr[offset++] = (byte)((value >> 40) & 0xFF);
        arr[offset++] = (byte)((value >> 32) & 0xFF);
        arr[offset++] = (byte)((value >> 24) & 0xFF);
        arr[offset++] = (byte)((value >> 16) & 0xFF);
        arr[offset++] = (byte)((value >> 8) & 0xFF);
        arr[offset++] = (byte)((value >> 0) & 0xFF);
    }

}
