using CHDSharp.Utils;
using Serilog;

namespace CHDSharp;

internal static class ChdBlockRead
{
    // search for all COMPRESSION_SELF block, and increase the counter of the block it is referencing.
    // the first time the referenced block is decompressed a copy of its data is kept.
    // this copy is then used (instead of re-decompressing.) until the use count returns to zero
    // at which time the backup copy if removed.

    internal static void FindRepeatedBlocks(ChdHeader chd)
    {
        var totalFound = 0;
        var compressionCount = new int[5];
        var compressionSelfCount = new int[5];
        var compressionUniqueCount = new int[5];

        Parallel.ForEach(chd.Map, me =>
        {
            if (me.Comptype != compression_type.COMPRESSION_SELF)
            {
                if ((int)me.Comptype < 5)
                    Interlocked.Increment(ref compressionCount[(int)me.Comptype]);
                return;
            }

            me.SelfMapEntry = chd.Map[me.Offset];
            switch (me.SelfMapEntry.Comptype)
            {
                case compression_type.COMPRESSION_TYPE_0:
                case compression_type.COMPRESSION_TYPE_1:
                case compression_type.COMPRESSION_TYPE_2:
                case compression_type.COMPRESSION_TYPE_3:
                case compression_type.COMPRESSION_NONE:
                    break;
                default:
                    Log.Error("Unexpected compression type {CompType}", me.SelfMapEntry.Comptype);
                    break;
            }

            lock (me.SelfMapEntry)
            {
                Interlocked.Increment(ref me.SelfMapEntry.UseCount);
                if (me.SelfMapEntry.UseCount == 1)
                    Interlocked.Increment(ref compressionUniqueCount[(int)me.SelfMapEntry.Comptype]);
            }

            Interlocked.Increment(ref compressionSelfCount[(int)me.SelfMapEntry.Comptype]);
            Interlocked.Increment(ref totalFound);
        });

        Log.Debug("Total Blocks {TotalBlocks}, Repeat Blocks {RepeatBlocks}, Output Block Size {BlockSize}", chd.Map.Length, totalFound, chd.Blocksize);
        for (var i = 0; i < 5; i++)
        {
            if ((compressionCount[i] == 0) & (compressionSelfCount[i] == 0))
                continue;

            var comp = "";
            if (i < chd.Compression.Length)
            {
                comp = chd.Compression[i].ToString().Substring(10);
            }
            else if (i == 4)
            {
                comp = "NONE";
            }

            Log.Debug("Compression {Index} : {Compression} : Block Count {Count}, Repeat Source Block Count {UniqueCount}, Repeat Total Block Count {SelfCount}", i, comp, compressionCount[i], compressionUniqueCount[i], compressionSelfCount[i]);
        }
    }

    internal static void KeepMostRepeatedBlocks(ChdHeader chd, int blocksToKeep)
    {
        var mapentries = new List<MapEntry>();
        foreach (var me in chd.Map)
        {
            if (me.UseCount > 0)
            {
                me.UsageWeight = GetWeigth(chd, me) * me.UseCount;
                mapentries.Add(me);
            }
        }

        Log.Debug("{Count} repeated used blocks", mapentries.Count);
        if (mapentries.Count < blocksToKeep)
            return;

        mapentries.Sort(static (a, b) => b.UsageWeight.CompareTo(a.UsageWeight));

        var c = 0;
        foreach (var me in mapentries)
        {
            if (c < blocksToKeep)
            {
                c++;
                me.KeepBufferCopy = true;
                continue;
            }

            me.KeepBufferCopy = false;
            me.UseCount = 0;
        }

        Parallel.ForEach(chd.Map, static me =>
        {
            if (me.Comptype != compression_type.COMPRESSION_SELF)
                return;
            // this should never be true
            if (me.SelfMapEntry == null)
                return;

            if (me.SelfMapEntry.KeepBufferCopy)
                return;

            me.Comptype = me.SelfMapEntry.Comptype;
            me.Length = me.SelfMapEntry.Length;
            me.Offset = me.SelfMapEntry.Offset;
            me.Crc = me.SelfMapEntry.Crc;
            me.Crc16 = me.SelfMapEntry.Crc16;
            me.SelfMapEntry = null!;
        });
    }

    internal static int GetWeigth(ChdHeader chd, MapEntry me)
    {
        if (me.Comptype == compression_type.COMPRESSION_NONE)
            return 1;

        switch (chd.Compression[(int)me.Comptype])
        {
            case chd_codec.CHD_CODEC_LZMA: return 23;
            case chd_codec.CHD_CODEC_ZLIB: return 1;
            case chd_codec.CHD_CODEC_FLAC: return (me.Length == 41) ? 1 : 2;
            case chd_codec.CHD_CODEC_HUFFMAN: return 64;

            case chd_codec.CHD_CODEC_AVHUFF: return 1;

            case chd_codec.CHD_CODEC_CD_FLAC: return (me.Length == 15) ? 1 : 2;
            case chd_codec.CHD_CODEC_CD_LZMA: return 18;
            case chd_codec.CHD_CODEC_CD_ZLIB: return 3;
            default: return 1;
        }
    }

    internal static void FindBlockReaders(ChdHeader chd)
    {
        chd.ChdReader = new CHDReader[chd.Compression.Length];
        for (var i = 0; i < chd.Compression.Length; i++)
        {
            chd.ChdReader[i] = GetReaderFromCodec(chd.Compression[i]);
        }
    }

    private static CHDReader GetReaderFromCodec(chd_codec chdCodec)
    {
        switch (chdCodec)
        {
            case chd_codec.CHD_CODEC_ZLIB: return CHDReaders.zlib;
            case chd_codec.CHD_CODEC_LZMA: return CHDReaders.lzma;
            case chd_codec.CHD_CODEC_HUFFMAN: return CHDReaders.huffman;
            case chd_codec.CHD_CODEC_FLAC: return CHDReaders.flac;
            case chd_codec.CHD_CODEC_ZSTD: return CHDReaders.zstd;
            case chd_codec.CHD_CODEC_CD_ZLIB: return CHDReaders.cdzlib;
            case chd_codec.CHD_CODEC_CD_LZMA: return CHDReaders.cdlzma;
            case chd_codec.CHD_CODEC_CD_FLAC: return CHDReaders.cdflac;
            case chd_codec.CHD_CODEC_CD_ZSTD: return CHDReaders.cdzstd;
            case chd_codec.CHD_CODEC_AVHUFF: return CHDReaders.avHuff;
            default: return null!;
        }
    }

    internal static chd_error ReadBlock(MapEntry mapEntry, ArrayPool arrPool, CHDReader[] compression, CHDCodec codec, byte[] buffOut, int buffOutLength)
    {
        var checkCrc = true;

        switch (mapEntry.Comptype)
        {
            case compression_type.COMPRESSION_TYPE_0:
            case compression_type.COMPRESSION_TYPE_1:
            case compression_type.COMPRESSION_TYPE_2:
            case compression_type.COMPRESSION_TYPE_3:
            {
                lock (mapEntry)
                {
                    if (mapEntry.BuffOutCache == null)
                    {
                        var ret = chd_error.CHDERR_UNSUPPORTED_FORMAT;
                        ret = compression[(int)mapEntry.Comptype].Invoke(mapEntry.BuffIn, (int)mapEntry.Length, buffOut, buffOutLength, codec);

                        if (ret != chd_error.CHDERR_NONE)
                            return ret;

                        // if this block is re-used keep a copy of it.
                        if (mapEntry.UseCount > 0)
                        {
                            mapEntry.BuffOutCache = arrPool.Rent();
                            Array.Copy(buffOut, 0, mapEntry.BuffOutCache, 0, buffOutLength);
                        }

                        break;
                    }

                    Array.Copy(mapEntry.BuffOutCache, 0, buffOut, 0, (int)buffOutLength);

                    Interlocked.Decrement(ref mapEntry.UseCount);
                    if (mapEntry.UseCount == 0)
                    {
                        arrPool.Return(mapEntry.BuffOutCache);
                        mapEntry.BuffOutCache = null!;
                    }

                    checkCrc = false;
                }

                break;
            }
            case compression_type.COMPRESSION_NONE:
            {
                lock (mapEntry)
                {
                    if (mapEntry.BuffOutCache == null)
                    {
                        Array.Copy(mapEntry.BuffIn, 0, buffOut, 0, buffOutLength);

                        if (mapEntry.UseCount > 0)
                        {
                            mapEntry.BuffOutCache = arrPool.Rent();
                            Array.Copy(buffOut, 0, mapEntry.BuffOutCache, 0, buffOutLength);
                        }
                        break;
                    }


                    Array.Copy(mapEntry.BuffOutCache, 0, buffOut, 0, (int)buffOutLength);
                    Interlocked.Decrement(ref mapEntry.UseCount);
                    if (mapEntry.UseCount == 0)
                    {
                        arrPool.Return(mapEntry.BuffOutCache);
                        mapEntry.BuffOutCache = null!;
                    }

                    checkCrc = false;
                }
                break;
            }

            case compression_type.COMPRESSION_MINI:
            {
                var tmp = BitConverter.GetBytes(mapEntry.Offset);
                for (var i = 0; i < 8; i++)
                {
                    buffOut[i] = tmp[7 - i];
                }

                for (var i = 8; i < buffOutLength; i++)
                {
                    buffOut[i] = buffOut[i - 8];
                }

                break;
            }

            case compression_type.COMPRESSION_SELF:
            {
                var retcs = ReadBlock(mapEntry.SelfMapEntry, arrPool, compression, codec, buffOut, buffOutLength);
                if (retcs != chd_error.CHDERR_NONE)
                    return retcs;
                // check CRC in the read_block_into_cache call
                checkCrc = false;
                break;
            }
            default:
                return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }

        if (checkCrc)
        {
            if ((mapEntry.Crc != null && !CRC.VerifyDigest((uint)mapEntry.Crc, buffOut, 0, (uint)buffOutLength)) || (mapEntry.Crc16 != null && CRC16.calc(buffOut, (int)buffOutLength) != mapEntry.Crc16))
                return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }

        return chd_error.CHDERR_NONE;
    }
}