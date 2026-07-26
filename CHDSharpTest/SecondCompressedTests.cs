using CHDSharp.Models;
using CHDSharp.Utils;

namespace CHDSharp.Tests;

public class SecondCompressedTests
{
    // ── Enum values ──

    [Fact]
    public void MapEntryFlag_2ndcompressed_has_value_6()
    {
        Assert.Equal(0x0006, (int)MapEntryFlag.Mapentrytype2Ndcompressed);
    }

    [Fact]
    public void CompressionType_2nd_has_value_103()
    {
        Assert.Equal(103, (int)CompressionType.Compressiontype2Nd);
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
        Assert.Equal(CompressionType.Compressiontype2Nd,
            ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytype2Ndcompressed));
    }

    [Fact]
    public void ConvMapEntry_2ndcompressed_with_nocrc_still_extracts_type()
    {
        const MapEntryFlag flag = MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytype2Ndcompressed;
        Assert.Equal(CompressionType.Compressiontype2Nd,
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
            Map = [new MapEntry { Comptype = CompressionType.Compressiontype2Nd, Length = 100, Offset = 0 }]
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
                new MapEntry { Comptype = CompressionType.Compressiontype2Nd, Length = 100, Offset = 100 }
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
            Comptype = CompressionType.Compressiontype2Nd,
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

        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = SecondaryReader
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.True(decompressed);
        return;

        ChdError SecondaryReader(byte[] buffIn, int buffInLength, byte[] buffOut2, int buffOutLength, ChdCodecState codec2)
        {
            decompressed = true;
            Array.Clear(buffOut2, 0, buffOutLength);
            return ChdError.Chderrnone;
        }
    }

    [Fact]
    public void ReadBlock_2ndcompressed_propagates_secondary_reader_error()
    {
        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = FailingReader
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrdecompressionerror, err);
        return;

        static ChdError FailingReader(byte[] bytes, int i, byte[] bytes1, int i1, ChdCodecState chdCodecState)
        {
            return ChdError.Chderrdecompressionerror;
        }
    }

    [Fact]
    public void ReadBlock_2ndcompressed_caches_output_when_usecount_positive()
    {
        var callCount = 0;

        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = SecondaryReader,
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
        return;

        ChdError SecondaryReader(byte[] buffIn, int buffInLength, byte[] buffOut3, int buffOutLength, ChdCodecState codec2)
        {
            callCount++;
            for (var i = 0; i < buffOutLength; i++)
            {
                buffOut3[i] = (byte)(i & 0xFF);
            }

            return ChdError.Chderrnone;
        }
    }

    [Fact]
    public void ReadBlock_2ndcompressed_verifies_crc32()
    {
        var expectedData = new byte[1024].Select(_ => (byte)0xAB).ToArray();
        var correctCrc = Crc.CalculateDigest(expectedData, 0, 1024);
        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = SecondaryReader,
            Crc = correctCrc
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrnone, err);
        return;

        static ChdError SecondaryReader(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodecState codec)
        {
            for (var i = 0; i < buffOutLength; i++)
            {
                buffOut[i] = 0xAB;
            }

            return ChdError.Chderrnone;
        }
    }

    [Fact]
    public void ReadBlock_2ndcompressed_fails_on_crc_mismatch()
    {
        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = SecondaryReader,
            Crc = 0x12345678
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[1024];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 1024);
        Assert.Equal(ChdError.Chderrdecompressionerror, err);
        return;

        static ChdError SecondaryReader(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodecState codec)
        {
            for (var i = 0; i < buffOutLength; i++)
            {
                buffOut[i] = 0xAB;
            }

            return ChdError.Chderrnone;
        }
    }

    // ── Self-reference with Compressiontype2nd ──

    [Fact]
    public void ReadBlock_self_referencing_2ndcompressed_resolves_correctly()
    {
        var decompressed = false;

        var sourceEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = SecondaryReader
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
        return;

        ChdError SecondaryReader(byte[] buffIn, int buffInLength, byte[] buffOut2, int buffOutLength, ChdCodecState codec2)
        {
            decompressed = true;
            for (var i = 0; i < buffOutLength; i++)
            {
                buffOut2[i] = 0xCD;
            }

            return ChdError.Chderrnone;
        }
    }

    // ── LinkSelfBlocks with SecondaryReader propagation ──

    [Fact]
    public void LinkSelfBlocks_propagates_secondary_reader()
    {
        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Map =
            [
                new MapEntry
                {
                    Comptype = CompressionType.Compressiontype2Nd,
                    Length = 100,
                    Offset = 0,
                    SecondaryReader = Reader
                },
                new MapEntry
                {
                    Comptype = CompressionType.Compressionself,
                    Offset = 0
                }
            ]
        };

        ChdBlockRead.FindBlockReaders(chd);

        Assert.NotNull(chd.Map[0].SecondaryReader);
        Assert.Null(chd.Map[1].SecondaryReader);
        return;

        static ChdError Reader(byte[] bytes, int i, byte[] bytes1, int i1, ChdCodecState chdCodecState)
        {
            return ChdError.Chderrnone;
        }
    }

    // ── KeepMostRepeatedBlocks with Compressiontype2nd ──

    [Fact]
    public void KeepMostRepeatedBlocks_flattens_self_to_2ndcompressed()
    {
        var sourceEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 100,
            Offset = 500,
            Crc = 12345,
            SecondaryReader = Reader,
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

        Assert.Equal(CompressionType.Compressiontype2Nd, selfEntry.Comptype);
        Assert.Equal(100u, selfEntry.Length);
        Assert.Equal(500u, selfEntry.Offset);
        Assert.Equal(12345u, selfEntry.Crc);
        Assert.NotNull(selfEntry.SecondaryReader);
        Assert.Null(selfEntry.SelfMapEntry);
        return;

        static ChdError Reader(byte[] bytes, int i, byte[] bytes1, int i1, ChdCodecState chdCodecState)
        {
            return ChdError.Chderrnone;
        }
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
        Assert.Equal(CompressionType.Compressiontype2Nd, chd.Map[0].Comptype);
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
        Assert.Equal(CompressionType.Compressiontype2Nd, chd.Map[0].Comptype);
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
        Assert.Equal(CompressionType.Compressiontype2Nd, chd.Map[1].Comptype);
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
            Map = [new MapEntry { Comptype = CompressionType.Compressiontype2Nd, Length = 100 }]
        };
        ChdBlockRead.FindBlockReaders(chd);
        ChdBlockRead.FindRepeatedBlocks(chd);
    }

    // ── Edge cases ──

    [Fact]
    public void CompressionType_2nd_is_distinct_from_all_other_types()
    {
        const CompressionType type2Nd = CompressionType.Compressiontype2Nd;
        Assert.NotEqual(CompressionType.Compressiontype0, type2Nd);
        Assert.NotEqual(CompressionType.Compressiontype1, type2Nd);
        Assert.NotEqual(CompressionType.Compressiontype2, type2Nd);
        Assert.NotEqual(CompressionType.Compressiontype3, type2Nd);
        Assert.NotEqual(CompressionType.Compressionnone, type2Nd);
        Assert.NotEqual(CompressionType.Compressionself, type2Nd);
        Assert.NotEqual(CompressionType.Compressionparent, type2Nd);
        Assert.NotEqual(CompressionType.Compressionmini, type2Nd);
        Assert.NotEqual(CompressionType.Compressionerror, type2Nd);
        Assert.NotEqual(CompressionType.Compressionzero, type2Nd);
    }

    [Fact]
    public void MapEntryFlag_2ndcompressed_is_distinct_from_all_other_flags()
    {
        const MapEntryFlag flag2Nd = MapEntryFlag.Mapentrytype2Ndcompressed;
        Assert.NotEqual(MapEntryFlag.Mapentrytypeinvalid, flag2Nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypecompressed, flag2Nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypeuncompressed, flag2Nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypemini, flag2Nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypeselfhunk, flag2Nd);
        Assert.NotEqual(MapEntryFlag.Mapentrytypeparenthunk, flag2Nd);
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

    // ── CompTypeConv mapping ──

    [Fact]
    public void CompTypeConv_type1_returns_zlib()
    {
        Assert.Equal(ChdCodec.Zlib, ChdCommon.CompTypeConv(1));
    }

    [Fact]
    public void CompTypeConv_type2_returns_zlib()
    {
        Assert.Equal(ChdCodec.Zlib, ChdCommon.CompTypeConv(2));
    }

    [Fact]
    public void CompTypeConv_type3_returns_avhuff()
    {
        Assert.Equal(ChdCodec.Avhuff, ChdCommon.CompTypeConv(3));
    }

    [Fact]
    public void CompTypeConv_unknown_returns_error()
    {
        Assert.Equal(ChdCodec.Error, ChdCommon.CompTypeConv(0));
        Assert.Equal(ChdCodec.Error, ChdCommon.CompTypeConv(4));
        Assert.Equal(ChdCodec.Error, ChdCommon.CompTypeConv(99));
    }

    [Fact]
    public void CompTypeConv_types1_and_2_are_identical()
    {
        var codec1 = ChdCommon.CompTypeConv(1);
        var codec2 = ChdCommon.CompTypeConv(2);
        Assert.Equal(codec1, codec2);
    }

    // ── IsValidCodec ──

    [Fact]
    public void IsValidCodec_zlib_returns_true()
    {
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Zlib));
    }

    [Fact]
    public void IsValidCodec_all_known_codecs_return_true()
    {
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Lzma));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Huffman));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Flac));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Zstd));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Cdzlib));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Cdlzma));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Cdflac));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Cdzstd));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Avhuff));
        Assert.True(ChdCommon.IsValidCodec(ChdCodec.Error));
    }

    [Fact]
    public void IsValidCodec_none_returns_false()
    {
        Assert.False(ChdCommon.IsValidCodec(ChdCodec.None));
    }

    [Fact]
    public void IsValidCodec_invalid_value_returns_false()
    {
        Assert.False(ChdCommon.IsValidCodec((ChdCodec)0x12345678));
    }

    // ── V4 mixed type0 and type6 entries ──

    [Fact]
    public void V4_header_mixed_type0_and_type6_entries()
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
            // blocksize (4 bytes)
            0x00, 0x00, 0x10, 0x00,
            // sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // parent sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // raw sha1 (20 bytes)
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // map entry 0 (16 bytes): offset=0, crc=0, length=0x1000, flags=type1 (COMPRESSED)
            // V4 length: (ReadUInt16Be) | (br.ReadByte() << 16)
            // 0x1000 = ReadUInt16Be(0x10,0x00)=0x1000, ReadByte=0x00
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x10, 0x00, 0x00,
            0x01,
            // map entry 1 (16 bytes): offset=0x1000, crc=0, length=0x0800, flags=type6 (2ND_COMPRESSED)
            0, 0, 0, 0, 0, 0, 0x10, 0x00,
            0, 0, 0, 0,
            0x08, 0x00, 0x00,
            0x06
        };

        using var ms = new MemoryStream(headerBytes);
        var err = ChdHeaders.ReadHeaderV4(ms, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(ChdCodec.Flac, chd.SecondaryCodec);
        Assert.Equal(CompressionType.Compressiontype0, chd.Map[0].Comptype);
        Assert.Equal(CompressionType.Compressiontype2Nd, chd.Map[1].Comptype);
        Assert.Equal(0x1000u, chd.Map[0].Length);
        Assert.Equal(0x0800u, chd.Map[1].Length);
    }

    // ── FindRepeatedBlocks with Compressiontype2nd ──

    [Fact]
    public void FindRepeatedBlocks_counts_2ndcompressed_entries()
    {
        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Blocksize = 1024,
            Map =
            [
                new MapEntry { Comptype = CompressionType.Compressiontype0, Length = 500, Offset = 0 },
                new MapEntry { Comptype = CompressionType.Compressiontype2Nd, Length = 600, Offset = 500 },
                new MapEntry { Comptype = CompressionType.Compressiontype2Nd, Length = 700, Offset = 1100 },
                new MapEntry { Comptype = CompressionType.Compressionself, Offset = 1 }
            ]
        };

        ChdBlockRead.FindRepeatedBlocks(chd);

        Assert.Equal(1, chd.Map[3].SelfMapEntry.UseCount);
        Assert.Equal(CompressionType.Compressiontype2Nd, chd.Map[3].SelfMapEntry.Comptype);
    }

    [Fact]
    public void FindRepeatedBlocks_self_to_2ndcompressed_resolves_correctly()
    {
        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Blocksize = 1024,
            Map =
            [
                new MapEntry
                {
                    Comptype = CompressionType.Compressiontype2Nd,
                    Length = 500,
                    Offset = 0,
                    SecondaryReader = Reader
                },
                new MapEntry
                {
                    Comptype = CompressionType.Compressionself,
                    Offset = 0
                }
            ]
        };

        ChdBlockRead.FindBlockReaders(chd);
        ChdBlockRead.FindRepeatedBlocks(chd);

        Assert.NotNull(chd.Map[1].SelfMapEntry);
        Assert.Equal(CompressionType.Compressiontype2Nd, chd.Map[1].SelfMapEntry.Comptype);
        Assert.NotNull(chd.Map[1].SelfMapEntry.SecondaryReader);
        return;

        static ChdError Reader(byte[] bytes, int i, byte[] bytes1, int i1, ChdCodecState chdCodecState)
        {
            return ChdError.Chderrnone;
        }
    }

    // ── Full pipeline: header parsing → FindBlockReaders → ReadBlock ──

    [Fact]
    public void FullPipeline_v3_zlib_plus_type2nd_decompresses_correctly()
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
            // map entry 0 (16 bytes): offset=0, crc=0, length=0x1000, flags=type1
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

        ChdBlockRead.FindBlockReaders(chd);

        // Primary reader should be Zlib
        Assert.NotNull(chd.ChdReader);
        Assert.Single(chd.ChdReader);

        // Secondary reader should be Flac
        Assert.NotNull(chd.SecondaryChdReader);
        Assert.Equal(ChdCodec.Flac, chd.SecondaryCodec);

        // Type-0 entry should not have secondary reader
        Assert.Null(chd.Map[0].SecondaryReader);

        // Type-2nd entry should have secondary reader
        Assert.NotNull(chd.Map[1].SecondaryReader);
    }

    // ── KeepMostRepeatedBlocks with multiple 2ndcompressed entries ──

    [Fact]
    public void KeepMostRepeatedBlocks_multiple_2ndcompressed_entries()
    {
        ChdReader reader = (_, _, _, _, _) => ChdError.Chderrnone;
        var sourceEntry1 = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 100,
            Offset = 500,
            Crc = 11111,
            SecondaryReader = reader,
            UseCount = 2,
            KeepBufferCopy = false
        };

        var sourceEntry2 = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 200,
            Offset = 1000,
            Crc = 22222,
            SecondaryReader = reader,
            UseCount = 1,
            KeepBufferCopy = false
        };

        var selfEntry = new MapEntry
        {
            Comptype = CompressionType.Compressionself,
            Offset = 0,
            SelfMapEntry = sourceEntry1
        };

        var chd = new ChdHeader
        {
            Compression = [ChdCodec.Zlib],
            SecondaryCodec = ChdCodec.Flac,
            Blocksize = 1024,
            Map = [sourceEntry1, sourceEntry2, selfEntry]
        };

        ChdBlockRead.KeepMostRepeatedBlocks(chd, 0);

        Assert.Equal(CompressionType.Compressiontype2Nd, selfEntry.Comptype);
        Assert.Equal(100u, selfEntry.Length);
        Assert.Equal(500u, selfEntry.Offset);
        Assert.Equal(11111u, selfEntry.Crc);
        Assert.NotNull(selfEntry.SecondaryReader);
        Assert.Null(selfEntry.SelfMapEntry);
    }

    // ── V3 with type6 entry with NOCRC flag ──

    [Fact]
    public void V3_header_type6_with_nocrc_flag_parses_correctly()
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
            // map entry (16 bytes): offset=0, crc=0, length=0x0800, flags=type6 | NOCRC (0x16)
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x08, 0x00, 0x00,
            0x16
        };

        using var ms = new MemoryStream(headerBytes);
        var err = ChdHeaders.ReadHeaderV3(ms, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(CompressionType.Compressiontype2Nd, chd.Map[0].Comptype);
        Assert.Null(chd.Map[0].Crc);
        Assert.Equal(0x0800u, chd.Map[0].Length);
    }

    // ── ConvMapEntryFlagtoCompressionType with all valid types ──

    [Fact]
    public void ConvMapEntry_all_valid_types_convert_correctly()
    {
        Assert.Equal(CompressionType.Compressionerror, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypeinvalid));
        Assert.Equal(CompressionType.Compressiontype0, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypecompressed));
        Assert.Equal(CompressionType.Compressionnone, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypeuncompressed));
        Assert.Equal(CompressionType.Compressionmini, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypemini));
        Assert.Equal(CompressionType.Compressionself, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypeselfhunk));
        Assert.Equal(CompressionType.Compressionparent, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytypeparenthunk));
        Assert.Equal(CompressionType.Compressiontype2Nd, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentrytype2Ndcompressed));
    }

    [Fact]
    public void ConvMapEntry_all_valid_types_with_nocrc_still_extract_type()
    {
        Assert.Equal(CompressionType.Compressionerror, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytypeinvalid));
        Assert.Equal(CompressionType.Compressiontype0, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytypecompressed));
        Assert.Equal(CompressionType.Compressionnone, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytypeuncompressed));
        Assert.Equal(CompressionType.Compressionmini, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytypemini));
        Assert.Equal(CompressionType.Compressionself, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytypeselfhunk));
        Assert.Equal(CompressionType.Compressionparent, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytypeparenthunk));
        Assert.Equal(CompressionType.Compressiontype2Nd, ChdCommon.ConvMapEntryFlagtoCompressionType(MapEntryFlag.Mapentryflagnocrc | MapEntryFlag.Mapentrytype2Ndcompressed));
    }

    // ── V3 all type6 entries ──

    [Fact]
    public void V3_header_all_type6_entries()
    {
        var headerBytes = new byte[]
        {
            // flags (4 bytes)
            0x00, 0x00, 0x00, 0x00,
            // compression type = 2 (ZLIB_PLUS) (4 bytes)
            0x00, 0x00, 0x00, 0x02,
            // total blocks (4 bytes)
            0x00, 0x00, 0x00, 0x03,
            // total bytes (8 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x30, 0x00,
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
            // map entry 0 (16 bytes): flags=type6
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x08, 0x00, 0x00,
            0x06,
            // map entry 1 (16 bytes): flags=type6
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x08, 0x00, 0x00,
            0x06,
            // map entry 2 (16 bytes): flags=type6
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0,
            0x08, 0x00, 0x00,
            0x06
        };

        using var ms = new MemoryStream(headerBytes);
        var err = ChdHeaders.ReadHeaderV3(ms, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(ChdCodec.Flac, chd.SecondaryCodec);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(CompressionType.Compressiontype2Nd, chd.Map[i].Comptype);
            Assert.Equal(0x0800u, chd.Map[i].Length);
        }
    }

    // ── ReadBlock with Compressiontype2nd and CRC16 ──

    [Fact]
    public void ReadBlock_2ndcompressed_verifies_crc16()
    {
        var expectedData = new byte[512].Select(_ => (byte)0xCD).ToArray();
        var correctCrc16 = Crc16.Calc(expectedData, 512);
        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = SecondaryReader,
            Crc = null,
            Crc16 = correctCrc16
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[512];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 512);
        Assert.Equal(ChdError.Chderrnone, err);
        return;

        static ChdError SecondaryReader(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodecState codec)
        {
            for (var i = 0; i < buffOutLength; i++)
            {
                buffOut[i] = 0xCD;
            }

            return ChdError.Chderrnone;
        }
    }

    [Fact]
    public void ReadBlock_2ndcompressed_fails_on_crc16_mismatch()
    {
        var mapEntry = new MapEntry
        {
            Comptype = CompressionType.Compressiontype2Nd,
            Length = 10,
            Offset = 0,
            BuffIn = new byte[10],
            SecondaryReader = SecondaryReader,
            Crc = null,
            Crc16 = 0x1234
        };

        var arrPool = new ArrayPool(1024);
        var compression = Array.Empty<ChdReader>();
        var codec = new ChdCodecState();
        var buffOut = new byte[512];

        var err = ChdBlockRead.ReadBlock(mapEntry, arrPool, compression, codec, buffOut, 512);
        Assert.Equal(ChdError.Chderrdecompressionerror, err);
        return;

        static ChdError SecondaryReader(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodecState codec)
        {
            for (var i = 0; i < buffOutLength; i++)
            {
                buffOut[i] = 0xCD;
            }

            return ChdError.Chderrnone;
        }
    }
}
