using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class HunkProcessorTests
{
    [Fact]
    public void ZeroHunk_compressesBelowHunkSize()
    {
        byte[] hunk = new byte[4096];
        var processor = new HunkProcessor(4096);
        var (entry, data) = processor.ProcessHunk(hunk, 124);

        Assert.Equal(MapEntry.COMPRESSION_TYPE_0, entry.Compression);
        Assert.True(data.Length < 4096);
    }

    [Fact]
    public void RandomHunk_mayBeUncompressed()
    {
        byte[] hunk = new byte[4096];
        new Random(42).NextBytes(hunk);
        var processor = new HunkProcessor(4096);
        var (entry, data) = processor.ProcessHunk(hunk, 124);

        if (entry.Compression == MapEntry.COMPRESSION_NONE)
        {
            Assert.Equal(4096u, entry.CompLength);
            Assert.Equal(4096, data.Length);
        }
    }

    [Fact]
    public void Crc16MatchesExpected()
    {
        byte[] hunk = new byte[4096];
        hunk[0] = 0x42;
        var processor = new HunkProcessor(4096);
        var (entry, _) = processor.ProcessHunk(hunk, 124);

        ushort expected = Crc16.Compute(hunk);
        Assert.Equal(expected, entry.Crc16);
    }

    [Fact]
    public void PatternHunk_compresses()
    {
        byte[] hunk = new byte[4096];
        for (int i = 0; i < hunk.Length; i++)
            hunk[i] = (byte)(i & 0xFF);

        var processor = new HunkProcessor(4096);
        var (entry, data) = processor.ProcessHunk(hunk, 124);

        Assert.Equal(MapEntry.COMPRESSION_TYPE_0, entry.Compression);
        Assert.True(data.Length < 4096);
    }

    [Fact]
    public void CompressedData_roundtrips()
    {
        byte[] original = new byte[4096];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)((i * 7 + 3) & 0xFF);

        var processor = new HunkProcessor(4096);
        var (entry, data) = processor.ProcessHunk(original, 124);

        if (entry.Compression == MapEntry.COMPRESSION_TYPE_0)
        {
            byte[] decompressed = RawDeflate.Decompress(data, 4096);
            Assert.Equal(original, decompressed);
        }
    }

    [Fact]
    public void FileOffset_storedInEntry()
    {
        byte[] hunk = new byte[4096];
        var processor = new HunkProcessor(4096);
        var (entry, _) = processor.ProcessHunk(hunk, 999888);

        Assert.Equal(999888uL, entry.Offset);
    }

    [Fact]
    public void HunkSizeMismatch_throws()
    {
        byte[] hunk = new byte[2048]; // half size
        var processor = new HunkProcessor(4096);

        Assert.Throws<ArgumentException>(() => processor.ProcessHunk(hunk, 124));
    }

    [Fact]
    public void CdFrameSizeHunk_works()
    {
        byte[] hunk = new byte[18816]; // 8 CD frames
        for (int i = 0; i < hunk.Length; i++)
            hunk[i] = (byte)((i * 13 + 7) & 0xFF);

        var processor = new HunkProcessor(18816);
        var (entry, data) = processor.ProcessHunk(hunk, 124);

        Assert.True(entry.Compression == MapEntry.COMPRESSION_TYPE_0 ||
                    entry.Compression == MapEntry.COMPRESSION_NONE);

        if (entry.Compression == MapEntry.COMPRESSION_TYPE_0)
        {
            byte[] decompressed = RawDeflate.Decompress(data, 18816);
            Assert.Equal(hunk, decompressed);
        }
    }

    [Fact]
    public void MapEntry_WriteRawMapEntry_roundtrips()
    {
        var entry = new MapEntry
        {
            Compression = MapEntry.COMPRESSION_TYPE_0,
            CompLength = 12345,
            Offset = 0xABCDEF012345,
            Crc16 = 0x9876,
        };

        byte[] rawMap = new byte[12];
        MapEntry.WriteRawMapEntry(rawMap, 0, entry);

        Assert.Equal(MapEntry.COMPRESSION_TYPE_0, rawMap[0]);
        Assert.Equal(0x00, rawMap[1]);
        Assert.Equal(0x30, rawMap[2]);
        Assert.Equal(0x39, rawMap[3]);
        Assert.Equal((byte)0xAB, rawMap[4]);
        Assert.Equal((byte)0xCD, rawMap[5]);
        Assert.Equal((byte)0xEF, rawMap[6]);
        Assert.Equal((byte)0x01, rawMap[7]);
        Assert.Equal((byte)0x23, rawMap[8]);
        Assert.Equal((byte)0x45, rawMap[9]);
        Assert.Equal(0x98, rawMap[10]);
        Assert.Equal(0x76, rawMap[11]);
    }

    [Fact]
    public void ConstantValues_matchMameDefines()
    {
        Assert.Equal(0, MapEntry.COMPRESSION_TYPE_0);
        Assert.Equal(4, MapEntry.COMPRESSION_NONE);
        Assert.Equal(5, MapEntry.COMPRESSION_SELF);
        Assert.Equal(6, MapEntry.COMPRESSION_PARENT);
    }
}
