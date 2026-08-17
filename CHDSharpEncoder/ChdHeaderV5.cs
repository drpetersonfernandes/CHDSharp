namespace CHDSharpEncoder;

/// <summary>Represents the header structure for CHD version 5 files.</summary>
public class ChdHeaderV5
{
    /// <summary>The CHD header tag as a string.</summary>
    public const string TagString = "MComprHD";

    /// <summary>The CHD header tag as a byte array.</summary>
    public static readonly byte[] Tag = "MComprHD"u8.ToArray();

    /// <summary>The serialized header length in bytes.</summary>
    public const uint Length = 124;

    /// <summary>The CHD format version (5).</summary>
    public const uint Version = 5;

    /// <summary>Gets or sets the four compressor codec tags.</summary>
    public uint[] Compressors { get; set; } = new uint[4];

    /// <summary>Gets or sets the total logical (uncompressed) size in bytes.</summary>
    public ulong LogicalBytes { get; set; }

    /// <summary>Gets or sets the byte offset of the hunk map within the file.</summary>
    public ulong MapOffset { get; set; }

    /// <summary>Gets or sets the byte offset of metadata within the file.</summary>
    public ulong MetaOffset { get; set; }

    /// <summary>Gets or sets the size of each hunk in bytes.</summary>
    public uint HunkBytes { get; set; }

    /// <summary>Gets or sets the unit size in bytes.</summary>
    public uint UnitBytes { get; set; }

    /// <summary>Gets or sets the SHA-1 hash of the raw (uncompressed) data.</summary>
    public byte[] RawSha1 { get; set; } = new byte[20];

    /// <summary>Gets or sets the SHA-1 hash of the final CHD data.</summary>
    public byte[] Sha1 { get; set; } = new byte[20];

    /// <summary>Gets or sets the SHA-1 hash of the parent CHD, if applicable.</summary>
    public byte[] ParentSha1 { get; set; } = new byte[20];

    /// <summary>Gets a value indicating whether the image uses compression.</summary>
    public bool IsCompressed => Compressors[0] != CodecTags.None;

    /// <summary>Serializes the header into a 124-byte array in big-endian format.</summary>
    /// <returns>A byte array containing the serialized header.</returns>
    public byte[] Serialize()
    {
        var w = new BigEndianWriter(124);

        w.WriteBytes(Tag);
        w.WriteU32(Length);
        w.WriteU32(Version);
        w.WriteU32(Compressors[0]);
        w.WriteU32(Compressors[1]);
        w.WriteU32(Compressors[2]);
        w.WriteU32(Compressors[3]);
        w.WriteU64(LogicalBytes);
        w.WriteU64(MapOffset);
        w.WriteU64(MetaOffset);
        w.WriteU32(HunkBytes);
        w.WriteU32(UnitBytes);
        w.WriteBytes(RawSha1);
        w.WriteBytes(Sha1);
        w.WriteBytes(ParentSha1);

        var result = w.ToArray();
        if (result.Length != Length)
            throw new InvalidOperationException($"Serialized header is {result.Length} bytes, expected {Length}");

        return result;
    }

    /// <summary>Writes the serialized header to a stream.</summary>
    /// <param name="stream">The output stream to write to.</param>
    public void WriteToStream(Stream stream)
    {
        var data = Serialize();
        stream.Write(data, 0, data.Length);
    }

    /// <summary>Deserializes a CHD v5 header from a byte array.</summary>
    /// <param name="data">The raw header bytes (at least 124 bytes).</param>
    /// <returns>A <see cref="ChdHeaderV5"/> populated from the data.</returns>
    public static ChdHeaderV5 Deserialize(byte[] data)
    {
        if (data.Length < Length)
            throw new ArgumentException($"Header data is {data.Length} bytes, need at least {Length}");

        return new ChdHeaderV5
        {
            Compressors = new[]
            {
                ReadU32Be(data, 16),
                ReadU32Be(data, 20),
                ReadU32Be(data, 24),
                ReadU32Be(data, 28)
            },
            LogicalBytes = ReadU64Be(data, 32),
            MapOffset = ReadU64Be(data, 40),
            MetaOffset = ReadU64Be(data, 48),
            HunkBytes = ReadU32Be(data, 56),
            UnitBytes = ReadU32Be(data, 60),
            RawSha1 = data.AsSpan(64, 20).ToArray(),
            Sha1 = data.AsSpan(84, 20).ToArray(),
            ParentSha1 = data.AsSpan(104, 20).ToArray()
        };
    }

    /// <summary>Creates a header for a raw (uncompressed) CHD image.</summary>
    /// <param name="compressors0">The primary compressor codec tag.</param>
    /// <param name="logicalBytes">The total logical size in bytes.</param>
    /// <param name="hunkBytes">The hunk size in bytes.</param>
    /// <param name="unitBytes">The unit size in bytes.</param>
    /// <returns>A new <see cref="ChdHeaderV5"/> configured for a raw image.</returns>
    public static ChdHeaderV5 CreateRaw(uint compressors0, ulong logicalBytes, uint hunkBytes, uint unitBytes)
    {
        return CreateRaw(new[] { compressors0, 0u, 0u, 0u }, logicalBytes, hunkBytes, unitBytes);
    }

    /// <summary>Creates a header for a compressed CHD image with up to 4 codecs.</summary>
    /// <param name="compressors">The compressor codec tags (up to 4; empty slots use <see cref="CodecTags.None"/>).</param>
    /// <param name="logicalBytes">The total logical size in bytes.</param>
    /// <param name="hunkBytes">The hunk size in bytes.</param>
    /// <param name="unitBytes">The unit size in bytes.</param>
    /// <returns>A new <see cref="ChdHeaderV5"/> configured for the image.</returns>
    public static ChdHeaderV5 CreateRaw(uint[] compressors, ulong logicalBytes, uint hunkBytes, uint unitBytes)
    {
        ArgumentNullException.ThrowIfNull(compressors);

        var codecArray = new uint[4];
        for (int i = 0; i < 4; i++)
        {
            codecArray[i] = i < compressors.Length ? compressors[i] : CodecTags.None;
        }

        return new ChdHeaderV5
        {
            Compressors = codecArray,
            LogicalBytes = logicalBytes,
            MapOffset = codecArray[0] != CodecTags.None ? 0uL : Length,
            MetaOffset = 0,
            HunkBytes = hunkBytes,
            UnitBytes = unitBytes
        };
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) |
               ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) |
               data[offset + 3];
    }

    private static ulong ReadU64Be(byte[] data, int offset)
    {
        return ((ulong)ReadU32Be(data, offset) << 32) |
               ReadU32Be(data, offset + 4);
    }
}
