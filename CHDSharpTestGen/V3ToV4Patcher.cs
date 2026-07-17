using System.Buffers.Binary;
using System.Security.Cryptography;

namespace CHDSharpTestGen;

/// <summary>Converts a standalone (no-parent) V3 CHD into a V4 CHD.
/// chdman 0.145's A/V compression crashes, so the V4 A/V corpus entry is synthesized from the
/// V3 file instead. V3 and V4 share identical map and metadata layouts; only the header differs
/// (120 -> 108 bytes), so all absolute file offsets shift by -12 and the V4 combined SHA1
/// (rawsha1 + checksummed metadata) is recomputed exactly as MAME 0.145 chd.c does.</summary>
internal static class V3ToV4Patcher
{
    private const int V3HeaderSize = 120;
    private const int V4HeaderSize = 108;
    private const int Delta = V3HeaderSize - V4HeaderSize; // 12
    private const int MetadataHeaderSize = 16;

    public static void Convert(string v3Path, string v4Path)
    {
        var src = File.ReadAllBytes(v3Path);

        if (System.Text.Encoding.ASCII.GetString(src, 0, 8) != "MComprHD" ||
            BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(12)) != 3)
            throw new InvalidDataException($"{v3Path} is not a V3 CHD");

        var flags = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(16));
        if ((flags & 1) != 0)
            throw new InvalidDataException("V3->V4 conversion only supports CHDs without a parent");

        var compression = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(20));
        var totalHunks = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(24));
        var logicalBytes = BinaryPrimitives.ReadUInt64BigEndian(src.AsSpan(28));
        var metaOffset = BinaryPrimitives.ReadUInt64BigEndian(src.AsSpan(36));
        var hunkBytes = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(76));
        var rawSha1 = src.AsSpan(80, 20).ToArray();

        var dst = new byte[src.Length - Delta];

        // V4 header
        System.Text.Encoding.ASCII.GetBytes("MComprHD", dst);
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(8), V4HeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(16), flags);
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(20), compression);
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(24), totalHunks);
        BinaryPrimitives.WriteUInt64BigEndian(dst.AsSpan(28), logicalBytes);
        BinaryPrimitives.WriteUInt64BigEndian(dst.AsSpan(36), metaOffset == 0 ? 0 : metaOffset - Delta);
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(44), hunkBytes);
        // sha1 @48 (combined, patched below), parentsha1 @68 (zeros), rawsha1 @88
        rawSha1.CopyTo(dst.AsSpan(88));

        // body (map + data + metadata) shifted by -12
        Array.Copy(src, V3HeaderSize, dst, V4HeaderSize, src.Length - V3HeaderSize);

        // map: 16-byte entries; adjust absolute offsets for COMPRESSED (1) / UNCOMPRESSED (2)
        // entries only (MINI stores literal data, SELF/PARENT store hunk indexes)
        for (uint h = 0; h < totalHunks; h++)
        {
            var pos = V4HeaderSize + (int)(h * 16);
            var type = dst[pos + 15] & 0x0F;
            if (type is 1 or 2)
            {
                var offset = BinaryPrimitives.ReadUInt64BigEndian(dst.AsSpan(pos));
                BinaryPrimitives.WriteUInt64BigEndian(dst.AsSpan(pos), offset - Delta);
            }
        }

        // metadata chain: fix each entry's next pointer
        var offsetIter = metaOffset == 0 ? 0 : metaOffset - Delta;
        while (offsetIter != 0)
        {
            var next = BinaryPrimitives.ReadUInt64BigEndian(dst.AsSpan((int)offsetIter + 8));
            if (next != 0)
                BinaryPrimitives.WriteUInt64BigEndian(dst.AsSpan((int)offsetIter + 8), next - Delta);
            offsetIter = next == 0 ? 0 : next - Delta;
        }

        // combined sha1 = SHA1(rawsha1 || sorted {tag,sha1} of checksummed metadata), per MAME chd.c
        ComputeOverallSha1(dst, rawSha1).CopyTo(dst.AsSpan(48));

        File.WriteAllBytes(v4Path, dst);
    }

    private static byte[] ComputeOverallSha1(byte[] file, byte[] rawSha1)
    {
        var hashes = new List<byte[]>();
        var offset = BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(36));
        while (offset != 0)
        {
            var pos = (int)offset;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos));
            var lengthField = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos + 4));
            var next = BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(pos + 8));

            var metaFlags = (byte)(lengthField >> 24);
            var metaLength = (int)(lengthField & 0x00FFFFFF);

            if ((metaFlags & 0x01) != 0) // CHD_MDFLAGS_CHECKSUM
            {
                var entry = new byte[24];
                BinaryPrimitives.WriteUInt32BigEndian(entry, tag);
                SHA1.HashData(file.AsSpan(pos + MetadataHeaderSize, metaLength)).CopyTo(entry.AsSpan(4));
                hashes.Add(entry);
            }

            offset = next;
        }

        hashes.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));

        using var sha1 = SHA1.Create();
        sha1.TransformBlock(rawSha1, 0, 20, null, 0);
        foreach (var entry in hashes)
            sha1.TransformBlock(entry, 0, 24, null, 0);
        sha1.TransformFinalBlock([], 0, 0);
        return sha1.Hash!;
    }
}
