using CHDSharp.Models;

namespace CHDSharp.Tests;

[Collection("TestData")]
public class BoundsValidationTests
{
    private static readonly byte[] Magic = "MComprHD"u8.ToArray();

    private static MemoryStream MakeV3Stream(uint totalblocks, uint blocksize, uint totalbytes, Action<MemoryStream> writeMapEntries)
    {
        var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        ms.Write(EndianHelpers.Be(120), 0, 4); // V3 header length
        ms.Write(EndianHelpers.Be(3), 0, 4); // version 3
        ms.Write(EndianHelpers.Be(0), 0, 4); // flags
        ms.Write(EndianHelpers.Be(1), 0, 4); // compression = 1 (zlib in V3 format)
        ms.Write(EndianHelpers.Be(totalblocks), 0, 4);
        ms.Write(EndianHelpers.Be64(totalbytes), 0, 8);
        ms.Write(EndianHelpers.Be64(0), 0, 8); // metaoffset
        ms.Write(new byte[16], 0, 16); // md5
        ms.Write(new byte[16], 0, 16); // parentmd5
        ms.Write(EndianHelpers.Be(blocksize), 0, 4);
        ms.Write(new byte[20], 0, 20); // rawsha1
        ms.Write(new byte[20], 0, 20); // parentsha1
        writeMapEntries(ms);
        ms.Position = 0;
        return ms;
    }

    private static void WriteMapEntryV3(Stream ms, ulong offset, uint crc, byte lenByte0, byte lenByte1, byte lenByte2, byte flags)
    {
        ms.Write(EndianHelpers.Be64(offset));
        ms.Write(EndianHelpers.Be(crc));
        ms.WriteByte(lenByte0);
        ms.WriteByte(lenByte1);
        ms.WriteByte(lenByte2);
        ms.WriteByte(flags);
    }

    [Fact]
    public void V1_zero_blocksize_returns_invalid_data()
    {
        var ms = new MemoryStream();
        ms.Write(EndianHelpers.Be(0), 0, 4); // flags
        ms.Write(EndianHelpers.Be(0), 0, 4); // compression
        ms.Write(EndianHelpers.Be(0), 0, 4); // blocksize = 0
        ms.Write(EndianHelpers.Be(1), 0, 4); // totalblocks
        ms.Write(EndianHelpers.Be(1), 0, 4); // cylinders
        ms.Write(EndianHelpers.Be(1), 0, 4); // heads
        ms.Write(EndianHelpers.Be(1), 0, 4); // sectors
        ms.Write(new byte[16], 0, 16); // md5
        ms.Write(new byte[16], 0, 16); // parentmd5
        ms.Position = 0;

        var err = ChdHeaders.ReadHeaderV1(ms, out _);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }

    [Fact]
    public void V2_zero_hunk_sectors_returns_invalid_data()
    {
        var ms = new MemoryStream();
        ms.Write(EndianHelpers.Be(0), 0, 4); // flags
        ms.Write(EndianHelpers.Be(0), 0, 4); // compression
        ms.Write(EndianHelpers.Be(0), 0, 4); // hunkSectors = 0
        ms.Write(EndianHelpers.Be(1), 0, 4); // totalblocks
        ms.Write(EndianHelpers.Be(1), 0, 4); // cylinders
        ms.Write(EndianHelpers.Be(1), 0, 4); // heads
        ms.Write(EndianHelpers.Be(1), 0, 4); // sectors
        ms.Write(new byte[16], 0, 16); // md5
        ms.Write(new byte[16], 0, 16); // parentmd5
        ms.Write(EndianHelpers.Be(512), 0, 4); // seclen
        ms.Position = 0;

        var err = ChdHeaders.ReadHeaderV2(ms, out _);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }

    [Fact]
    public void V2_zero_seclen_returns_invalid_data()
    {
        var ms = new MemoryStream();
        ms.Write(EndianHelpers.Be(0), 0, 4); // flags
        ms.Write(EndianHelpers.Be(0), 0, 4); // compression
        ms.Write(EndianHelpers.Be(1), 0, 4); // hunkSectors
        ms.Write(EndianHelpers.Be(1), 0, 4); // totalblocks
        ms.Write(EndianHelpers.Be(1), 0, 4); // cylinders
        ms.Write(EndianHelpers.Be(1), 0, 4); // heads
        ms.Write(EndianHelpers.Be(1), 0, 4); // sectors
        ms.Write(new byte[16], 0, 16); // md5
        ms.Write(new byte[16], 0, 16); // parentmd5
        ms.Write(EndianHelpers.Be(0), 0, 4); // seclen = 0
        ms.Position = 0;

        var err = ChdHeaders.ReadHeaderV2(ms, out _);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }

    [Fact]
    public void V5_rejects_unknown_codec_value()
    {
        var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        ms.Write(EndianHelpers.Be(124), 0, 4);
        ms.Write(EndianHelpers.Be(5), 0, 4);
        ms.Position = 16; // ReadHeaderV5 expects stream after the preamble (magic + length + version)
        ms.Write(EndianHelpers.Be(0xDEADBEEF), 0, 4); // invalid codec
        ms.Write(EndianHelpers.Be((uint)ChdCodec.None), 0, 4);
        ms.Write(EndianHelpers.Be((uint)ChdCodec.None), 0, 4);
        ms.Write(EndianHelpers.Be((uint)ChdCodec.None), 0, 4);
        ms.Write(EndianHelpers.Be64(1000), 0, 8); // totalbytes
        ms.Write(EndianHelpers.Be64(0), 0, 8); // mapoffset
        ms.Write(EndianHelpers.Be64(0), 0, 8); // metaoffset
        ms.Write(EndianHelpers.Be(1000), 0, 4); // blocksize
        ms.Write(EndianHelpers.Be(2448), 0, 4); // unitbytes
        ms.Write(new byte[60], 0, 60); // sha1 * 3
        ms.Position = 16;

        var err = ChdHeaders.ReadHeaderV5(ms, out _);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }

    [Fact]
    public void Flac_single_byte_input_returns_invalid_data()
    {
        var buffIn = new[] { (byte)'L' };
        var buffOut = new byte[4096];
        using var codec = new ChdCodecState();

        var err = ChdReaders.Flac(buffIn, buffIn.Length, buffOut, buffOut.Length, codec);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }

    [Fact]
    public void Flac_empty_input_returns_invalid_data()
    {
        var buffIn = Array.Empty<byte>();
        var buffOut = new byte[4096];
        using var codec = new ChdCodecState();

        Assert.Throws<IndexOutOfRangeException>(() =>
            ChdReaders.Flac(buffIn, 0, buffOut, buffOut.Length, codec));
    }

    [Fact]
    public void GetReaderFromCodec_unknown_codec_throws_not_supported()
    {
        var invalidCodec = (ChdCodec)Enum.ToObject(typeof(ChdCodec), 0xDEADBEEF);
        var chd = new ChdHeader
        {
            Compression = [invalidCodec],
            Totalbytes = 1000,
            Blocksize = 1000,
            Totalblocks = 1,
            Map = [new MapEntry()],
            UncompressedMap = false,
            Md5 = new byte[16],
            Rawsha1 = new byte[20],
            Sha1 = new byte[20],
            Parentmd5 = new byte[16],
            Parentsha1 = new byte[20]
        };

        Assert.Throws<NotSupportedException>(() => ChdBlockRead.FindBlockReaders(chd));
    }

    [Fact]
    public void LinkSelfBlocks_offset_beyond_map_rejected_via_open()
    {
        var ms = MakeV3Stream(
            2,
            512,
            1024,
            stream =>
            {
                // Entry 0: valid compressed hunk at offset 256
                WriteMapEntryV3(stream,
                    256,
                    0,
                    0, 2, 0, // length = 512
                    (byte)MapEntryFlag.Mapentrytypecompressed);
                // Entry 1: self-reference with offset 999 (way beyond map length of 2)
                WriteMapEntryV3(stream,
                    999,
                    0,
                    0, 0, 0, // length = 0
                    (byte)MapEntryFlag.Mapentrytypeselfhunk);
            });

        // Append enough padding so stream doesn't trim
        ms.Seek(0, SeekOrigin.End);
        ms.WriteByte(0);
        ms.Position = 0;

        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
        Assert.Null(chd);
    }

    [Fact]
    public void Self_reference_with_valid_offset_succeeds()
    {
        var ms = MakeV3Stream(
            2,
            512,
            1024,
            stream =>
            {
                // Entry 0: valid compressed hunk
                WriteMapEntryV3(stream,
                    256,
                    0,
                    0, 2, 0,
                    (byte)MapEntryFlag.Mapentrytypecompressed);
                // Entry 1: self-reference to entry 0
                WriteMapEntryV3(stream,
                    0,
                    0,
                    0, 0, 0,
                    (byte)MapEntryFlag.Mapentrytypeselfhunk);
            });

        ms.Seek(0, SeekOrigin.End);
        ms.WriteByte(0);
        ms.Position = 0;

        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);
        chd.Dispose();
    }
}
