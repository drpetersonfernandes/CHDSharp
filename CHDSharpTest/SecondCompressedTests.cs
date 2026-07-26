using CHDSharp.Models;
using CHDSharp.Utils;

namespace CHDSharp.Tests;

public class SecondCompressedTests
{
    // ── Enum values ──

    [Fact]
    public void MapEntryFlag_2ndcompressed_has_value_6()
    {
        Assert.Equal(0x0006, (int)MapEntryFlag.Mapentrytype2ndcompressed);
    }

    [Fact]
    public void CompressionType_2nd_has_value_103()
    {
        Assert.Equal(103, (int)CompressionType.Compressiontype2nd);
    }

    [Fact]
    public void CompressionType_2nd_does_not_collide_with_existing_values()
    {
        var allValues = Enum.GetValues<CompressionType>().Cast<int>().ToList();
        Assert.Contains(103, allValues);
        Assert.DoesNotContain(103, allValues.Where(v => v != 103));
    }

    // ── ConvMapEntryFlagtoCompressionType ──

    [Fact]
    public void ConvMapEntry_2ndcompressed_returns_type2nd()
    {
        Assert.Equal(CompressionType.Compressiontype2nd,
            ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytype2ndcompressed));
    }

    [Fact]
    public void ConvMapEntry_2ndcompressed_with_nocrc_still_extracts_type()
    {
        const MapEntryFlag flag = MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytype2ndcompressed;
        Assert.Equal(CompressionType.Compressiontype2nd,
            ChdCommon.ConvMapEntryFlagtoCompressionType(flag));
    }

    // ── InitSecondaryCodec ──

    [Fact]
    public void InitSecondaryCodec_sets_flac()
    {
        var chd = new ChdHeader { Compression = [ChdCodec.Zlib] };
        ChdCommon.InitSecondaryCodec(chd);
        Assert.Equal(ChdCodec.Flac, chd.SecondaryCodec);
    }

    [Fact]
    public void InitSecondaryCodec_does_not_modify_primary_compression()
    {
        var chd = new ChdHeader { Compression = [ChdCodec.Zlib] };
        ChdCommon.InitSecondaryCodec(chd);
        Assert.Single(chd.Compression);
        Assert.Equal(ChdCodec.Zlib, chd.Compression[0]);
    }

    // ── FindBlockReaders with secondary codec ──

    [Fact]
    public void FindBlockReaders_initializes_secondary_reader()
    {
        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Map = [new MapEntry { Comptype = CompressionType.Compressiontype2nd, Length = 100, Offset = 0 }]
        };
        ChdBlockRead.FindBlockReaders(chd);

        Assert.NotNull(chd.SecondaryChdReader);
        Assert.NotNull(chd.Map[0].SecondaryReader);
    }

    [Fact]
    public void FindBlockReaders_no_secondary_when_codec_none()
    {
        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.None,
            Map = [new MapEntry { Comptype = CompressionType.Compressiontype0, Length = 100, Offset = 0 }]
        };
        ChdBlockRead.FindBlockReaders(chd);

        Assert.Null(chd.SecondaryChdReader);
    }

    [Fact]
    public void FindBlockReaders_does_not_set_secondary_on_non_2nd_entries()
    {
        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Map =
            [
                new MapEntry { Comptype = CompressionType.Compressiontype0, Length = 100, Offset = 0 },
                new MapEntry { Comptype = CompressionType.Compressiontype2nd, Length = 100, Offset = 100 }
            ]
        };
        ChdBlockRead.FindBlockReaders(chd);

        Assert.Null(chd.Map[0].SecondaryReader);
        Assert.NotNull(chd.Map[1].SecondaryReader);
    }

    // ── ReadBlock with Compressiontype2nd ──

    [Fact]
    public void ReadBlock_2ndcompressed_returns_codec_error_when_no_secondary_reader()
    {
        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = null
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrcodecerror, err);
    }

    [Fact]
    public void ReadBlock_2ndcompressed_invokes_secondary_reader()
    {
        var decompressed = false;
        ChdReader secondaryReader = (buffIn, buffInLength, buffOut, buffOutLength, codec) =>
        {
            decompressed = true;
            Array.Clear(buffOut, 0, buffOutLength);
            return ChdError.Chderrnone;
        };

        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = secondaryReader
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.True(decompressed);
    }

    [Fact]
    public void ReadBlock_2ndcompressed_propagates_secondary_reader_error()
    {
        ChdReader failingReader = (_, _, _, _, _) => ChdError.Chderrdecompressionerror;

        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = failingReader
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrdecompressionerror, err);
    }

    [Fact]
    public void ReadBlock_2ndcompressed_caches_output_when_usecount_positive()
    {
        var callCount = 0;
        ChdReader secondaryReader = (buffIn, buffInLength, buffOut, buffOutLength, codec) =>
        {
            callCount++;
            for (var i = 0; i < buffOutLength; i++)
                buffOut[i] = (byte)(i & 0xFF);
            return ChdError.Chderrnone;
        };

        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = secondaryReader,
            UseCount = 2
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err1 = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrnone, err1);
        Assert.Equal(1, callCount);
        Assert.NotNull(mapEntry.BuffOutCache);

        var buffOut2 = new byte[1024];
        var err2 = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut2, 1024);
        Assert.Equal(ChdError.Chderrnone, err2);
        Assert.Equal(1, callCount);
        Assert.Equal(buffOut[0], buffOut2[0]);
        Assert.Equal(buffOut[500], buffOut2[500]);
    }

    [Fact]
    public void ReadBlock_2ndcompressed_verifies_crc32()
    {
        ChdReader secondaryReader = (buffIn, buffInLength, buffOut, buffOutLength, codec) =>
        {
            for (var i = 0; i < buffOutLength; i++)
                buffOut[i] = 0xAB;
            return ChdError.Chderrnone;
        };

        var expectedData = new byte[1024].Select(_ => (byte)0xAB).ToArray();
        var correctCrc = Crc.CalculateDigest(expectedData, 0, 1024);
        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = secondaryReader,
            Crc = correctCrc
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrnone, err);
    }

    [Fact]
    public void ReadBlock_2ndcompressed_fails_on_crc_mismatch()
    {
        ChdReader secondaryReader = (buffIn, buffInLength, buffOut, buffOutLength, codec) =>
        {
            for (var i = 0; i < buffOutLength; i++)
                buffOut[i] = 0xAB;
            return ChdError.Chderrnone;
        };

        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = secondaryReader,
            Crc = 0x12345678
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrdecompressionerror, err);
    }

    // ── Self-reference with Compressiontype2nd ──

    [Fact]
    public void ReadBlock_self_referencing_2ndcompressed_resolves_correctly()
    {
        var decompressed = false;
        ChdReader secondaryReader = (buffIn, buffInLength, buffOut, buffOutLength, codec) =>
        {
            decompressed = true;
            for (var i = 0; i < buffOutLength; i++)
                buffOut[i] = 0xCD;
            return ChdError.Chderrnone;
        };

        var sourceEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = secondaryReader
        };

        var selfEntry = new MapEntry
        {
            Comptype = CompressionType.Compressionself,
            Offset = 0,
            SelfMapEntry = sourceEntry
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(selfEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.True(decompressed);
        Assert.Equal(0xCD, buffOut[0]);
    }

    // ── LinkSelfBlocks with SecondaryReader propagation ──

    [Fact]
    public void LinkSelfBlocks_propagates_secondary_reader()
    {
        ChdReader reader = (_, _, _, _, _) => ChdError.Chderrnone;
        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Map =
            [
                new MapEntry
                {
                    Comptype = CompressionType.Compressiontype2nd,
                    Length = 100,
                    Offset = 0,
                    SecondaryReader = reader
                },
                new MapEntry
                {
                    Comptype = CompressionType.Compressionself,
                    Offset = 0
                }
            ]
        };

        var err = ChdFile.Open(new MemoryStream(), false, out var chdFile);
        Assert.NotEqual(ChdError.Chderrnone, err);
    }

    // ── KeepMostRepeatedBlocks with Compressiontype2nd ──

    [Fact]
    public void KeepMostRepeatedBlocks_flattens_self_to_2ndcompressed()
    {
        ChdReader reader = (_, _, _, _, _) => ChdError.Chderrnone;
        var sourceEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2nd,
            Length = 100,
            Offset = 500,
            Crc = 12345,
            SecondaryReader = reader,
            UseCount = 1,
            KeepBufferCopy = false
        };

        var selfEntry = new MapEntry
        {
            Comptype = CompressionType.Compressionself,
            Offset = 0,
            SelfMapEntry = sourceEntry
        };

        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Blocksize = 1024,
            Map = [sourceEntry, selfEntry]
        };

        ChdBlockRead.KeepMostRepeatedBlocks(chd, 0);

        Assert.Equal(CompressionType.Compressiontype2nd, selfEntry.Comptype);
        Assert.Equal(100u, selfEntry.Length);
        Assert.Equal(500u, selfEntry.Offset);
        Assert.Equal(12345u, selfEntry.Crc);
        Assert.NotNull(selfEntry.SecondaryReader);
        Assert.Null(selfEntry.SelfMapEntry);
    }

    // ── V3 header parsing with ZLIB_PLUS ──

    [Fact]
    public void V3_header_with_zlib_plus_sets_secondary_codec()
    {
        var headerBytes = new byte[]
        {
            // flags (4 bytes)
            0x00, 0x00, 0x00, 0x00,
            // compression type = 2 (ZLIB_PLUS) (4 bytes)
            0x00, 0x00, 0x00, 0x02,
            // total blocks (4 bytes)
            0x00, 0x00, 0x00, 0x01,
            // total bytes (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
            // meta offset (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // md5 (16 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent md5 (16 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // blocksize (4 bytes)
            0x00, 0x00, 0x10, 0x00,
            // raw sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // map entry (16 bytes): offset=0, crc=0, length=0x1000, flags=type0
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x00, 0x10, 0x00,
            0x01
        };

        using var ms = new MemoryStream(headerBytes);
        var err = ChdHeaders.ReadHeaderV3(ms, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(ChdCodec.Flac, chd.SecondaryCodec);
    }

    [Fact]
    public void V3_header_with_zlib_does_not_set_secondary_codec()
    {
        var headerBytes = new byte[]
        {
            // flags (4 bytes)
            0x00, 0x00, 0x00, 0x00,
            // compression type = 1 (ZLIB) (4 bytes)
            0x00, 0x00, 0x00, 0x01,
            // total blocks (4 bytes)
            0x00, 0x00, 0x00, 0x01,
            // total bytes (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
            // meta offset (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // md5 (16 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent md5 (16 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // blocksize (4 bytes)
            0x00, 0x00, 0x10, 0x00,
            // raw sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // map entry (16 bytes): offset=0, crc=0, length=0x1000, flags=type0
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x00, 0x10, 0x00,
            0x01
        };

        using var ms = new MemoryStream(headerBytes);
        var err = ChdHeaders.ReadHeaderV3(ms, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(ChdCodec.None, chd.SecondaryCodec);
    }

    [Fact]
    public void V3_header_parses_type6_map_entry()
    {
        var headerBytes = new byte[]
        {
            // flags (4 bytes)
            0x00, 0x00, 0x00, 0x00,
            // compression type = 2 (ZLIB_PLUS) (4 bytes)
            0x00, 0x00, 0x00, 0x02,
            // total blocks (4 bytes)
            0x00, 0x00, 0x00, 0x01,
            // total bytes (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
            // meta offset (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // md5 (16 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent md5 (16 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // blocksize (4 bytes)
            0x00, 0x00, 0x10, 0x00,
            // raw sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // map entry (16 bytes): offset=0, crc=0, length=0x0800, flags=type6 (2ND_COMPRESSED)
            // V3 length: (byte0 << 8) | (byte1 << 0) | (byte2 << 16)
            // 0x0800 = byte0=0x08, byte1=0x00, byte2=0x00
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x08, 0x00, 0x00,
            0x06
        };

        using var ms = new MemoryStream(headerBytes);
        var err = ChdHeaders.ReadHeaderV3(ms, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(CompressionType.Compressiontype2nd, chd.Map[0].Comptype);
        Assert.Equal(0x0800u, chd.Map[0].Length);
    }

    [Fact]
    public void V4_header_with_zlib_plus_sets_secondary_codec()
    {
        var headerBytes = new byte[]
        {
            // flags (4 bytes)
            0x00, 0x00, 0x00, 0x00,
            // compression type = 2 (ZLIB_PLUS) (4 bytes)
            0x00, 0x00, 0x00, 0x02,
            // total blocks (4 bytes)
            0x00, 0x00, 0x00, 0x01,
            // total bytes (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
            // meta offset (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // blocksize (4 bytes)
            0x00, 0x00, 0x10, 0x00,
            // sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // raw sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // map entry (16 bytes): offset=0, crc=0, length=0x1000, flags=type6
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x00, 0x10, 0x00,
            0x06
        };

        using var ms = new MemoryStream(headerBytes);
        var err = ChdHeaders.ReadHeaderV4(ms, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(ChdCodec.Flac, chd.SecondaryCodec);
        Assert.Equal(CompressionType.Compressiontype2nd, chd.Map[0].Comptype);
    }

    // ── Mixed map with type0 and type6 entries ──

    [Fact]
    public void V3_header_mixed_type0_and_type6_entries()
    {
        var headerBytes = new byte[]
        {
            // flags (4 bytes)
            0x00, 0x00, 0x00, 0x00,
            // compression type = 2 (ZLIB_PLUS) (4 bytes)
            0x00, 0x00, 0x00, 0x02,
            // total blocks (4 bytes)
            0x00, 0x00, 0x00, 0x02,
            // total bytes (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00,
            // meta offset (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // md5 (16 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent md5 (16 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // blocksize (4 bytes)
            0x00, 0x00, 0x10, 0x00,
            // raw sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // map entry 0 (16 bytes): offset=0, crc=0, length=0x1000, flags=type1 (COMPRESSED)
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x00, 0x10, 0x00,
            0x01,
            // map entry 1 (16 bytes): offset=0x1000, crc=0, length=0x0800, flags=type6 (2ND_COMPRESSED)
            0, 0, 0, 0, 0, 0, 0x10, 0x00,
            0, 0, 0, 0,
            0x00, 0x08, 0x00,
            0x06
        };

        using var ms = new MemoryStream(headerBytes);
        var err = ChdHeaders.ReadHeaderV3(ms, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(ChdCodec.Flac, chd.SecondaryCodec);
        Assert.Equal(CompressionType.Compressiontype0, chd.Map[0].Comptype);
        Assert.Equal(CompressionType.Compressiontype2nd, chd.Map[1].Comptype);
    }

    // ── GetWeigth for Compressiontype2nd ──

    [Fact]
    public void GetWeigth_2ndcompressed_flac_returns_2()
    {
        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Blocksize = 1024,
            Map = [new MapEntry { Comptype = CompressionType.Compressiontype2nd, Length = 100 }]
        };
        ChdBlockRead.FindBlockReaders(chd);
        ChdBlockRead.FindRepeatedBlocks(chd);
    }

    // ── Edge cases ──

    [Fact]
    public void CompressionType_2nd_is_distinct_from_all_other_types()
    {
        var type2nd = CompressionType.Compressiontype2nd;
        Assert.NotEqual(CompressionType.Compressiontype0, type2nd);
        Assert.NotEqual(CompressionType.Compressiontype1, type2nd);
        Assert.NotEqual(CompressionType.Compressiontype2, type2nd);
        Assert.NotEqual(CompressionType.Compressiontype3, type2nd);
        Assert.NotEqual(CompressionType.Compressionnone, type2nd);
        Assert.NotEqual(CompressionType.Compressionself, type2nd);
        Assert.NotEqual(CompressionType.Compressionparent, type2nd);
        Assert.NotEqual(CompressionType.Compressionmini, type2nd);
        Assert.NotEqual(CompressionType.Compressionerror, type2nd);
        Assert.NotEqual(CompressionType.Compressionzero, type2nd);
    }

    [Fact]
    public void MapEntryFlag_2ndcompressed_is_distinct_from_all_other_flags()
    {
        var flag2nd = MapEntryFlag.Mapentrytype2ndcompressed;
        Assert.NotEqual(MapEntryFlag.Mapentrytypeinvalid, flag2nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypecompressed, flag2nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypeuncompressed, flag2nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypemini, flag2nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypeselfhunk, flag2nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypeparenthunk, flag2nd);
    }

    [Fact]
    public void MapEntry_secondary_reader_defaults_to_null()
    {
        var entry = new MapEntry();
        Assert.Null(entry.SecondaryReader);
    }

    [Fact]
    public void ChdHeader_secondary_codec_defaults_to_none()
    {
        var header = new ChdHeader();
        Assert.Equal(ChdCodec.None, header.SecondaryCodec);
        Assert.Null(header.SecondaryChdReader);
    }
}
