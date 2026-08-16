using System.Text;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class MetadataWriterTests
{
    private static CdTrack MakeTrack(int number, int trackType, int frames, int pregap = 0,
        int pgType = 0, int pgDataSize = 0, int postgap = 0)
    {
        return new CdTrack
        {
            Number = number,
            TrackType = trackType,
            SubType = CdSubType.None,
            DataSize = CdConstants.MaxSectorData,
            Frames = frames,
            Pregap = pregap,
            PgType = pgType,
            PgDataSize = pgDataSize,
            PgSub = CdSubType.None,
            Postgap = postgap,
        };
    }

    [Fact]
    public void MetadataString_FormatCorrect()
    {
        // pgType defaults to 0 (Mode1) when no pregap was derived; chdman writes PGTYPE:MODE1
        var track = MakeTrack(1, CdTrackType.Mode1Raw, 13500);
        Assert.Equal(
            "TRACK:1 TYPE:MODE1_RAW SUBTYPE:NONE FRAMES:13500 PREGAP:0 PGTYPE:MODE1 PGSUB:NONE POSTGAP:0",
            MetadataWriter.BuildChd2String(track));
    }

    [Fact]
    public void MetadataString_AudioWithPregap_HasValidFlag()
    {
        var track = MakeTrack(2, CdTrackType.Audio, 13500, pregap: 150, pgType: CdTrackType.Audio, pgDataSize: 2352);
        Assert.Equal(
            "TRACK:2 TYPE:AUDIO SUBTYPE:NONE FRAMES:13500 PREGAP:150 PGTYPE:VAUDIO PGSUB:NONE POSTGAP:0",
            MetadataWriter.BuildChd2String(track));
    }

    [Fact]
    public void MetadataString_EmptyPregapType()
    {
        var track = MakeTrack(3, CdTrackType.Audio, 100, pregap: 0);
        Assert.Equal(
            "TRACK:3 TYPE:AUDIO SUBTYPE:NONE FRAMES:100 PREGAP:0 PGTYPE:MODE1 PGSUB:NONE POSTGAP:0",
            MetadataWriter.BuildChd2String(track));
    }

    [Theory]
    [InlineData(CdTrackType.Mode1, "MODE1")]
    [InlineData(CdTrackType.Mode1Raw, "MODE1_RAW")]
    [InlineData(CdTrackType.Mode2, "MODE2")]
    [InlineData(CdTrackType.Mode2Form1, "MODE2_FORM1")]
    [InlineData(CdTrackType.Mode2Form2, "MODE2_FORM2")]
    [InlineData(CdTrackType.Mode2FormMix, "MODE2_FORM_MIX")]
    [InlineData(CdTrackType.Mode2Raw, "MODE2_RAW")]
    [InlineData(CdTrackType.Audio, "AUDIO")]
    public void TypeString_Mappings(int type, string expected)
    {
        Assert.Equal(expected, MetadataWriter.GetTypeString(type));
    }

    [Theory]
    [InlineData(CdSubType.Normal, "RW")]
    [InlineData(CdSubType.Raw, "RW_RAW")]
    [InlineData(CdSubType.None, "NONE")]
    public void SubtypeString_MappingsCorrect(int subtype, string expected)
    {
        Assert.Equal(expected, MetadataWriter.GetSubtypeString(subtype));
    }

    [Fact]
    public void MetadataEntry_SerializeLayout()
    {
        var entry = new MetadataEntry
        {
            Tag = 0x43485432,
            Flags = 0x01,
            Payload = Encoding.ASCII.GetBytes("TRACK:1\0"),
            NextOffset = 0x1234,
        };

        byte[] data = entry.Serialize();

        Assert.Equal(16 + 8, data.Length);
        Assert.Equal(0x43, data[0]);
        Assert.Equal(0x48, data[1]);
        Assert.Equal(0x54, data[2]);
        Assert.Equal(0x32, data[3]);
        Assert.Equal(0x01, data[4]);           // flags
        Assert.Equal(0, data[5]);              // length high byte (fits in 24 bits)
        Assert.Equal(0, data[6]);
        Assert.Equal(8, data[7]);              // length = payload length including null
        Assert.Equal(0x00001234UL, ReadU64BE(data, 8));
        Assert.Equal("TRACK:1\0", Encoding.ASCII.GetString(data, 16, 8));
    }

    [Fact]
    public void WriteMetadata_ProducesValidEntries()
    {
        var toc = new CdToc();
        toc.Tracks.Add(MakeTrack(1, CdTrackType.Mode1Raw, 1000));
        toc.Tracks.Add(MakeTrack(2, CdTrackType.Audio, 2000, pregap: 150, pgType: CdTrackType.Audio, pgDataSize: 2352));

        using var ms = new MemoryStream();
        long metaOffset = MetadataWriter.WriteCdMetadata(ms, toc);

        Assert.Equal(0L, metaOffset);
        Assert.True(ms.Length > 0);

        byte[] data = ms.ToArray();
        Assert.Equal(MetadataWriter.CdRomTrackMetadata2Tag, ReadU32BE(data, (int)metaOffset));

        // entry 0: 'CHT2', length = string + null terminator, next points at entry 1
        long entry0Len = 16 + ReadU24BE(data, (int)metaOffset + 5);
        Assert.Equal((ulong)entry0Len, ReadU64BE(data, (int)metaOffset + 8));
        Assert.Equal(0x01, data[(int)metaOffset + 4]);

        // entry 1: next = 0 (end of list)
        long entry1Offset = metaOffset + entry0Len;
        Assert.Equal(0UL, ReadU64BE(data, (int)entry1Offset + 8));
        Assert.Equal(entry1Offset + 16 + ReadU24BE(data, (int)entry1Offset + 5), ms.Length);
    }

    [Fact]
    public void WriteMetadata_EmptyToc_ReturnsPositionAndWritesNothing()
    {
        using var ms = new MemoryStream();
        ms.SetLength(100);
        ms.Position = 42;

        long metaOffset = MetadataWriter.WriteCdMetadata(ms, new CdToc());

        Assert.Equal(42L, metaOffset);
        Assert.Equal(100L, ms.Length);
    }

    [Fact]
    public void WriteMetadata_SingleTrack_NextIsZero()
    {
        var toc = new CdToc();
        toc.Tracks.Add(MakeTrack(1, CdTrackType.Audio, 500));

        using var ms = new MemoryStream();
        long metaOffset = MetadataWriter.WriteCdMetadata(ms, toc);

        byte[] data = ms.ToArray();
        Assert.Equal(0UL, ReadU64BE(data, (int)metaOffset + 8));
        Assert.Equal(metaOffset + 16 + ReadU24BE(data, (int)metaOffset + 5), ms.Length);
    }

    [Fact]
    public void WriteMetadata_LinkedListIntegrity()
    {
        var toc = new CdToc();
        for (int i = 1; i <= 5; i++)
            toc.Tracks.Add(MakeTrack(i, CdTrackType.Audio, 100 + i));

        using var ms = new MemoryStream();
        long firstOffset = MetadataWriter.WriteCdMetadata(ms, toc);
        byte[] data = ms.ToArray();

        long offset = firstOffset;
        int entries = 0;
        while (true)
        {
            Assert.True((int)offset + 16 <= data.Length, "entry header out of range");
            uint tag = ReadU32BE(data, (int)offset);
            Assert.Equal(MetadataWriter.CdRomTrackMetadata2Tag, tag);
            Assert.Equal(MetadataWriter.ChdMdflagsChecksum, data[(int)offset + 4]);

            ulong next = ReadU64BE(data, (int)offset + 8);
            long entryLen = 16 + ReadU24BE(data, (int)offset + 5);
            entries++;

            if (next == 0)
            {
                Assert.Equal(5, entries);
                Assert.Equal(offset + entryLen, ms.Length);
                break;
            }

            // next must point exactly past this entry
            Assert.Equal((ulong)(offset + entryLen), next);
            offset = (long)next;
        }
    }

    private static uint ReadU32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static uint ReadU24BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 16) | ((uint)data[offset + 1] << 8) | data[offset + 2];
    }

    private static ulong ReadU64BE(byte[] data, int offset)
    {
        return ((ulong)ReadU32BE(data, offset) << 32) | ReadU32BE(data, offset + 4);
    }
}