using System.Text;
using CHDSharp.Models;
using CHDSharp.Models.Utils;

namespace CHDSharp.Tests;

public class BoundsValidationTests
{
    private static readonly byte[] Magic = "MComprHD"u8.ToArray();

    private static byte[] Be(uint v)
    {
        return [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    }

    private static byte[] Be64(ulong v)
    {
        return [
            (byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32),
            (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v
        ];
    }

    private static MemoryStream MakeV3Stream(uint totalblocks, uint blocksize, uint totalbytes, Action<MemoryStream> writeMapEntries)
    {
        var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        ms.Write(Be(120), 0, 4); // V3 header length
        ms.Write(Be(3), 0, 4); // version 3
        ms.Write(Be(0), 0, 4); // flags
        ms.Write(Be(1), 0, 4); // compression = 1 (zlib in V3 format)
        ms.Write(Be(totalblocks), 0, 4);
        ms.Write(Be64(totalbytes), 0, 8);
        ms.Write(Be64(0), 0, 8); // metaoffset
        ms.Write(new byte[16], 0, 16); // md5
        ms.Write(new byte[16], 0, 16); // parentmd5
        ms.Write(Be(blocksize), 0, 4);
        ms.Write(new byte[20], 0, 20); // rawsha1
        ms.Write(new byte[20], 0, 20); // parentsha1
        writeMapEntries(ms);
        ms.Position = 0;
        return ms;
    }

    private static void WriteMapEntryV3(Stream ms, ulong offset, uint crc, byte lenByte0, byte lenByte1, byte lenByte2, byte flags)
    {
        ms.Write(Be64(offset));
        ms.Write(Be(crc));
        ms.WriteByte(lenByte0);
        ms.WriteByte(lenByte1);
        ms.WriteByte(lenByte2);
        ms.WriteByte(flags);
    }

    [Fact]
    public void V1_zero_blocksize_returns_invalid_data()
    {
        var ms = new MemoryStream();
        ms.Write(Be(0), 0, 4); // flags
        ms.Write(Be(0), 0, 4); // compression
        ms.Write(Be(0), 0, 4); // blocksize = 0
        ms.Write(Be(1), 0, 4); // totalblocks
        ms.Write(Be(1), 0, 4); // cylinders
        ms.Write(Be(1), 0, 4); // heads
        ms.Write(Be(1), 0, 4); // sectors
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
        ms.Write(Be(0), 0, 4); // flags
        ms.Write(Be(0), 0, 4); // compression
        ms.Write(Be(0), 0, 4); // hunkSectors = 0
        ms.Write(Be(1), 0, 4); // totalblocks
        ms.Write(Be(1), 0, 4); // cylinders
        ms.Write(Be(1), 0, 4); // heads
        ms.Write(Be(1), 0, 4); // sectors
        ms.Write(new byte[16], 0, 16); // md5
        ms.Write(new byte[16], 0, 16); // parentmd5
        ms.Write(Be(512), 0, 4); // seclen
        ms.Position = 0;

        var err = ChdHeaders.ReadHeaderV2(ms, out _);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }

    [Fact]
    public void V2_zero_seclen_returns_invalid_data()
    {
        var ms = new MemoryStream();
        ms.Write(Be(0), 0, 4); // flags
        ms.Write(Be(0), 0, 4); // compression
        ms.Write(Be(1), 0, 4); // hunkSectors
        ms.Write(Be(1), 0, 4); // totalblocks
        ms.Write(Be(1), 0, 4); // cylinders
        ms.Write(Be(1), 0, 4); // heads
        ms.Write(Be(1), 0, 4); // sectors
        ms.Write(new byte[16], 0, 16); // md5
        ms.Write(new byte[16], 0, 16); // parentmd5
        ms.Write(Be(0), 0, 4); // seclen = 0
        ms.Position = 0;

        var err = ChdHeaders.ReadHeaderV2(ms, out _);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }

    [Fact]
    public void V5_rejects_unknown_codec_value()
    {
        var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        ms.Write(Be(124), 0, 4);
        ms.Write(Be(5), 0, 4);
        ms.Write(Be(0xDEADBEEF), 0, 4); // invalid codec
        ms.Write(Be((uint)ChdCodec.None), 0, 4);
        ms.Write(Be((uint)ChdCodec.None), 0, 4);
        ms.Write(Be((uint)ChdCodec.None), 0, 4);
        ms.Write(Be64(1000), 0, 8); // totalbytes
        ms.Write(Be64(0), 0, 8); // mapoffset
        ms.Write(Be64(0), 0, 8); // metaoffset
        ms.Write(Be(1000), 0, 4); // blocksize
        ms.Write(Be(2448), 0, 4); // unitbytes
        ms.Write(new byte[60], 0, 60); // sha1 * 3
        ms.Position = 0;

        var err = ChdHeaders.ReadHeaderV5(ms, out _);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }

    [Fact]
    public void Flac_no_input_data_returns_invalid_data()
    {
        var buffIn = new byte[] { (byte)'L' };
        var buffOut = new byte[4096];
        var codec = new ChdCodecState();

        var err = ChdReaders.Flac(buffIn, buffIn.Length, buffOut, buffOut.Length, codec);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
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
            totalblocks: 2,
            blocksize: 512,
            totalbytes: 1024,
            writeMapEntries: stream =>
            {
                // Entry 0: valid compressed hunk at offset 256
                WriteMapEntryV3(stream,
                    offset: 256,
                    crc: 0,
                    lenByte0: 0, lenByte1: 2, lenByte2: 0, // length = 512
                    flags: (byte)MapEntryFlag.Mapentrytypecompressed);
                // Entry 1: self-reference with offset 999 (way beyond map length of 2)
                WriteMapEntryV3(stream,
                    offset: 999,
                    crc: 0,
                    lenByte0: 0, lenByte1: 0, lenByte2: 0, // length = 0
                    flags: (byte)MapEntryFlag.Mapentrytypeselfhunk);
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
            totalblocks: 2,
            blocksize: 512,
            totalbytes: 1024,
            writeMapEntries: stream =>
            {
                // Entry 0: valid compressed hunk
                WriteMapEntryV3(stream,
                    offset: 256,
                    crc: 0,
                    lenByte0: 0, lenByte1: 2, lenByte2: 0,
                    flags: (byte)MapEntryFlag.Mapentrytypecompressed);
                // Entry 1: self-reference to entry 0
                WriteMapEntryV3(stream,
                    offset: 0,
                    crc: 0,
                    lenByte0: 0, lenByte1: 0, lenByte2: 0,
                    flags: (byte)MapEntryFlag.Mapentrytypeselfhunk);
            });

        ms.Seek(0, SeekOrigin.End);
        ms.WriteByte(0);
        ms.Position = 0;

        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);
        chd?.Dispose();
    }
}
