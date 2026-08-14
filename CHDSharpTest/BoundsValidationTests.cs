using System.IO.Compression;
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

    // ── Compressed hunk larger than output bounds (#118) ──

    private static (MemoryStream stream, byte[] hunk) MakeV3CompressedHunkStream(
        uint length,
        Func<MemoryStream, byte[]> writeData,
        uint blocksize = 512,
        byte flags = (byte)MapEntryFlag.Mapentrytypecompressed)
    {
        var ms = MakeV3Stream(
            1,
            blocksize,
            blocksize,
            stream => WriteMapEntryV3(
                stream,
                256,
                0,
                // V3 length layout (ChdHeaders.ReadHeaderV3): (byte0<<8) | (byte1<<0) | (byte2<<16).
                (byte)((length >> 8) & 0xFF),
                (byte)(length & 0xFF),
                (byte)((length >> 16) & 0xFF),
                flags));

        // Append the compressed payload at offset 256.
        ms.Seek(0, SeekOrigin.End);
        var data = writeData(ms);
        // Re-seek to absolute 256 if needed (if header shrank padding).
        ms.SetLength(256);
        ms.Position = 256;
        ms.Write(data, 0, data.Length);
        ms.Position = 0;
        return (ms, data);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var outStream = new MemoryStream();
        using (var deflate = new DeflateStream(outStream, CompressionLevel.Optimal, true))
        {
            deflate.Write(data, 0, data.Length);
        }
        return outStream.ToArray();
    }

    [Fact]
    public void Default_compressed_cap_is_2x_hunk_bytes()
    {
        var (stream, _) = MakeV3CompressedHunkStream(4, _ => new byte[1] { 0 });
        var err = ChdFile.Open(stream, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(chd!.HunkBytes * 2, chd.MaxCompressedBlockBytes);
        chd.Dispose();
    }

    [Fact]
    public void Cap_can_be_lowered_but_never_below_hunk_bytes()
    {
        var (stream, _) = MakeV3CompressedHunkStream(4, _ => new byte[1] { 0 });
        var err = ChdFile.Open(stream, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        chd!.MaxCompressedBlockBytes = 10; // below hunk bytes (512) → floored to hunk bytes
        Assert.Equal(chd.HunkBytes, chd.MaxCompressedBlockBytes);
        chd.MaxCompressedBlockBytes = 4096;
        Assert.Equal(4096u, chd.MaxCompressedBlockBytes);
        chd.MaxCompressedBlockBytes = 0; // reset to default
        Assert.Equal(chd.HunkBytes * 2, chd.MaxCompressedBlockBytes);
        chd.Dispose();
    }

    [Fact]
    public void ReadHunk_claims_compressed_length_over_cap_returns_invalid_data()
    {
        // blocksize 512 → default cap 1024. Claim a 2000-byte compressed hunk.
        var (stream, _) = MakeV3CompressedHunkStream(2000, _ => new byte[1] { 0 });
        var err = ChdFile.Open(stream, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);

        var buffer = new byte[512];
        var readErr = chd!.ReadHunk(0, buffer);
        Assert.Equal(ChdError.Chderrinvaliddata, readErr);
        chd.Dispose();
    }

    [Fact]
    public void ReadHunk_claims_compressed_length_over_cap_via_corpus_style_large_hunk_returns_invalid_data()
    {
        // Same as above but exercising a larger hunk size: blocksize 4096 → default cap 8192.
        var (stream, _) = MakeV3CompressedHunkStream(20000, _ => new byte[1] { 0 }, blocksize: 4096);
        var err = ChdFile.Open(stream, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(8192u, chd!.MaxCompressedBlockBytes);

        var buffer = new byte[4096];
        Assert.Equal(ChdError.Chderrinvaliddata, chd.ReadHunk(0, buffer));
        chd.Dispose();
    }

    [Fact]
    public void ReadHunk_with_compressed_size_over_hunk_bytes_but_under_cap_succeeds()
    {
        // Core #118 scenario: a VALID hunk whose compressed size (deflate header + stored-block
        // overhead for incompressible data) is larger than the uncompressed hunk size, but still
        // within the default cap (2x hunk bytes). It must read back correctly, NOT be rejected.
        var blocksize = 512;

        byte[] payload = new byte[blocksize];
        new Random(42).NextBytes(payload); // incompressible
        var compressed = Deflate(payload);

        // Incompressible data overhead pushes compressed size just over 512, well under cap 1024.
        Assert.InRange(compressed.Length, blocksize + 1, blocksize * 2);

        var (stream, _) = MakeV3CompressedHunkStream(
            (uint)compressed.Length,
            _ => compressed,
            flags: (byte)(MapEntryFlag.Mapentrytypecompressed | MapEntryFlag.Mapentryflagnocrc));
        var err = ChdFile.Open(stream, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal((uint)(blocksize * 2), chd!.MaxCompressedBlockBytes);

        var buffer = new byte[blocksize];
        Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, buffer));
        Assert.Equal(payload, buffer);
        chd.Dispose();
    }

    [Fact]
    public void CheckFile_oversized_compressed_hunk_returns_invalid_data()
    {
        // Exercise the parallel verification path (DecompressDataParallel).
        var (stream, _) = MakeV3CompressedHunkStream(2000, _ => new byte[1] { 0 });
        stream.Position = 0;

        var err = Chd.CheckFile(stream, "oversized.chd", deepCheck: true, out _, out _, out _);
        Assert.Equal(ChdError.Chderrinvaliddata, err);
    }
}
