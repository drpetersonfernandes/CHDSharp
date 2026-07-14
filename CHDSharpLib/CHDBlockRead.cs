using CHDSharp.Models;
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
            if (me.Comptype != compressionType.COMPRESSIONSELF)
            {
                if ((int)me.Comptype < 5)
                    Interlocked.Increment(ref compressionCount[(int)me.Comptype]);
                return;
            }

            me.SelfMapEntry = chd.Map[me.Offset];
            switch (me.SelfMapEntry.Comptype)
            {
                case compressionType.COMPRESSIONTYPE0:
                case compressionType.COMPRESSIONTYPE1:
                case compressionType.COMPRESSIONTYPE2:
                case compressionType.COMPRESSIONTYPE3:
                case compressionType.COMPRESSIONNONE:
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
            if (me.Comptype != compressionType.COMPRESSIONSELF)
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
        if (me.Comptype == compressionType.COMPRESSIONNONE)
            return 1;

        switch (chd.Compression[(int)me.Comptype])
        {
            case chdCodec.CHDCODECLZMA: return 23;
            case chdCodec.CHDCODECZLIB: return 1;
            case chdCodec.CHDCODECFLAC: return (me.Length == 41) ? 1 : 2;
            case chdCodec.CHDCODECHUFFMAN: return 64;

            case chdCodec.CHDCODECAVHUFF: return 1;

            case chdCodec.CHDCODECCDFLAC: return (me.Length == 15) ? 1 : 2;
            case chdCodec.CHDCODECCDLZMA: return 18;
            case chdCodec.CHDCODECCDZLIB: return 3;
            default: return 1;
        }
    }

    internal static void FindBlockReaders(ChdHeader chd)
    {
        chd.ChdReader = new ChdReader[chd.Compression.Length];
        for (var i = 0; i < chd.Compression.Length; i++)
        {
            chd.ChdReader[i] = GetReaderFromCodec(chd.Compression[i]);
        }
    }

    private static ChdReader GetReaderFromCodec(chdCodec chdCodec)
    {
        switch (chdCodec)
        {
            case chdCodec.CHDCODECZLIB: return ChdReaders.Zlib;
            case chdCodec.CHDCODECLZMA: return ChdReaders.Lzma;
            case chdCodec.CHDCODECHUFFMAN: return ChdReaders.Huffman;
            case chdCodec.CHDCODECFLAC: return ChdReaders.Flac;
            case chdCodec.CHDCODECZSTD: return ChdReaders.Zstd;
            case chdCodec.CHDCODECCDZLIB: return ChdReaders.Cdzlib;
            case chdCodec.CHDCODECCDLZMA: return ChdReaders.Cdlzma;
            case chdCodec.CHDCODECCDFLAC: return ChdReaders.Cdflac;
            case chdCodec.CHDCODECCDZSTD: return ChdReaders.Cdzstd;
            case chdCodec.CHDCODECAVHUFF: return ChdReaders.AvHuff;
            default: return null!;
        }
    }

    internal static chdError ReadBlock(MapEntry mapEntry, ArrayPool arrPool, ChdReader[] compression, CHDCodec codec, byte[] buffOut, int buffOutLength)
    {
        var checkCrc = true;

        switch (mapEntry.Comptype)
        {
            case compressionType.COMPRESSIONTYPE0:
            case compressionType.COMPRESSIONTYPE1:
            case compressionType.COMPRESSIONTYPE2:
            case compressionType.COMPRESSIONTYPE3:
            {
                lock (mapEntry)
                {
                    if (mapEntry.BuffOutCache == null)
                    {
                        var ret = compression[(int)mapEntry.Comptype].Invoke(mapEntry.BuffIn, (int)mapEntry.Length, buffOut, buffOutLength, codec);

                        if (ret != chdError.CHDERRNONE)
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
            case compressionType.COMPRESSIONNONE:
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

            case compressionType.COMPRESSIONMINI:
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

            case compressionType.COMPRESSIONSELF:
            {
                var retcs = ReadBlock(mapEntry.SelfMapEntry, arrPool, compression, codec, buffOut, buffOutLength);
                if (retcs != chdError.CHDERRNONE)
                    return retcs;
                // check CRC in the read_block_into_cache call
                checkCrc = false;
                break;
            }
            default:
                return chdError.CHDERRDECOMPRESSIONERROR;
        }

        if (checkCrc)
        {
            if ((mapEntry.Crc != null && !CRC.VerifyDigest((uint)mapEntry.Crc, buffOut, 0, (uint)buffOutLength)) || (mapEntry.Crc16 != null && CRC16.calc(buffOut, (int)buffOutLength) != mapEntry.Crc16))
                return chdError.CHDERRDECOMPRESSIONERROR;
        }

        return chdError.CHDERRNONE;
    }
}