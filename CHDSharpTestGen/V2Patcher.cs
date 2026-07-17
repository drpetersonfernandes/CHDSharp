using System.Buffers.Binary;

namespace CHDSharpTestGen;

/// <summary>Converts a V1 CHD into a V2 CHD. No stock MAME tool ever wrote V2 by default
/// (V2 only added an explicit seclen field), so the V2 corpus entry is synthesized from a V1 file:
/// header grows 76 -> 80 bytes (seclen appended), version bumped, and all absolute file offsets
/// in the map are shifted by +4.</summary>
internal static class V2Patcher
{
    private const int V1HeaderSize = 76;
    private const int V2HeaderSize = 80;
    private const uint SectorSize = 512;

    public static void Convert(string v1Path, string v2Path)
    {
        var src = File.ReadAllBytes(v1Path);

        if (src.Length < V1HeaderSize ||
            System.Text.Encoding.ASCII.GetString(src, 0, 8) != "MComprHD" ||
            BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(12)) != 1)
            throw new InvalidDataException($"{v1Path} is not a V1 CHD");

        var totalHunks = BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(28));

        var dst = new byte[src.Length + 4];
        // header
        Array.Copy(src, 0, dst, 0, V1HeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(8), V2HeaderSize);   // length
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(12), 2);             // version
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(V1HeaderSize), SectorSize); // seclen

        // body (map + data) shifted by +4
        Array.Copy(src, V1HeaderSize, dst, V2HeaderSize, src.Length - V1HeaderSize);

        // fix absolute offsets inside the map: each entry is UINT64 BE, offset in low 44 bits
        for (uint h = 0; h < totalHunks; h++)
        {
            var pos = V2HeaderSize + (int)(h * 8);
            var entry = BinaryPrimitives.ReadUInt64BigEndian(dst.AsSpan(pos));
            var offset = entry & 0xFFFFFFFFFFFUL;
            var length = entry & ~0xFFFFFFFFFFFUL;
            BinaryPrimitives.WriteUInt64BigEndian(dst.AsSpan(pos), length | (offset + 4));
        }

        File.WriteAllBytes(v2Path, dst);
    }
}
