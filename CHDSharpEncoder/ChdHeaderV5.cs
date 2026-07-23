namespace CHDSharpEncoder;

public class ChdHeaderV5
{
    public const string TAG_STRING = "MComprHD";
    public static readonly byte[] TAG = { (byte)'M', (byte)'C', (byte)'o', (byte)'m', (byte)'p', (byte)'r', (byte)'H', (byte)'D' };
    public const uint LENGTH = 124;
    public const uint VERSION = 5;

    public uint[] Compressors { get; set; } = new uint[4];
    public ulong LogicalBytes { get; set; }
    public ulong MapOffset { get; set; }
    public ulong MetaOffset { get; set; }
    public uint HunkBytes { get; set; }
    public uint UnitBytes { get; set; }
    public byte[] RawSha1 { get; set; } = new byte[20];
    public byte[] Sha1 { get; set; } = new byte[20];
    public byte[] ParentSha1 { get; set; } = new byte[20];

    public bool IsCompressed => Compressors[0] != CodecTags.NONE;

    public byte[] Serialize()
    {
        var w = new BigEndianWriter(124);

        w.WriteBytes(TAG);
        w.WriteU32(LENGTH);
        w.WriteU32(VERSION);
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

        byte[] result = w.ToArray();
        if (result.Length != LENGTH)
            throw new InvalidOperationException($"Serialized header is {result.Length} bytes, expected {LENGTH}");

        return result;
    }

    public void WriteToStream(Stream stream)
    {
        byte[] data = Serialize();
        stream.Write(data, 0, data.Length);
    }

    public static ChdHeaderV5 Deserialize(byte[] data)
    {
        if (data.Length < LENGTH)
            throw new ArgumentException($"Header data is {data.Length} bytes, need at least {LENGTH}");

        return new ChdHeaderV5
        {
            Compressors = new[]
            {
                ReadU32BE(data, 16),
                ReadU32BE(data, 20),
                ReadU32BE(data, 24),
                ReadU32BE(data, 28),
            },
            LogicalBytes = ReadU64BE(data, 32),
            MapOffset = ReadU64BE(data, 40),
            MetaOffset = ReadU64BE(data, 48),
            HunkBytes = ReadU32BE(data, 56),
            UnitBytes = ReadU32BE(data, 60),
            RawSha1 = data.AsSpan(64, 20).ToArray(),
            Sha1 = data.AsSpan(84, 20).ToArray(),
            ParentSha1 = data.AsSpan(104, 20).ToArray(),
        };
    }

    public static ChdHeaderV5 CreateRaw(uint compressors0, ulong logicalBytes, uint hunkBytes, uint unitBytes)
    {
        return new ChdHeaderV5
        {
            Compressors = new[] { compressors0, 0u, 0u, 0u },
            LogicalBytes = logicalBytes,
            MapOffset = IsCompressedCheck(compressors0) ? 0uL : LENGTH,
            MetaOffset = 0,
            HunkBytes = hunkBytes,
            UnitBytes = unitBytes,
        };
    }

    private static bool IsCompressedCheck(uint compressor0) => compressor0 != CodecTags.NONE;

    private static uint ReadU32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) |
               ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) |
               data[offset + 3];
    }

    private static ulong ReadU64BE(byte[] data, int offset)
    {
        return ((ulong)ReadU32BE(data, offset) << 32) |
               ReadU32BE(data, offset + 4);
    }
}
