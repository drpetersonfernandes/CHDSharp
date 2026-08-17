using CHDSharp.Models;
using CHDSharp.Utils;
using Microsoft.Extensions.Logging;

namespace CHDSharp;

/// <summary>Handles CHD block reading, deduplication of self-referencing blocks, and decompression caching.</summary>
internal static class ChdBlockRead
{
    private static readonly ILogger Log = ChdLogger.GetLogger(nameof(ChdBlockRead));

    private static readonly Action<ILogger, CompressionType, Exception?> LogUnexpectedCompType =
        LoggerMessage.Define<CompressionType>(LogLevel.Error, new EventId(1), "Unexpected compression type {CompType}");

    private static readonly Action<ILogger, int, int, uint, Exception?> LogBlockSummary =
        LoggerMessage.Define<int, int, uint>(LogLevel.Debug, new EventId(2), "Total Blocks {TotalBlocks}, Repeat Blocks {RepeatBlocks}, Output Block Size {BlockSize}");

    private static readonly Action<ILogger, int, string, int, int, int, Exception?> LogCompressionStats =
        LoggerMessage.Define<int, string, int, int, int>(LogLevel.Debug, new EventId(3), "Compression {Index} : {Compression} : Block Count {Count}, Repeat Source Block Count {UniqueCount}, Repeat Total Block Count {SelfCount}");

    private static readonly Action<ILogger, int, Exception?> LogRepeatedBlocksCount =
        LoggerMessage.Define<int>(LogLevel.Debug, new EventId(4), "{Count} repeated used blocks");

    /// <summary>Scans the map for <see cref="CompressionType.Compressionself"/> entries and builds usage counts for referenced source blocks.</summary>
    /// <param name="chd">The parsed CHD header containing the block map.</param>
    internal static void FindRepeatedBlocks(ChdHeader chd)
    {
        var totalFound = 0;
        var compressionCount = new int[6];
        var compressionSelfCount = new int[6];
        var compressionUniqueCount = new int[6];

        Parallel.ForEach(chd.Map, me =>
        {
            if (me.Comptype != CompressionType.Compressionself)
            {
                var idx = (int)me.Comptype;
                if (idx is >= 0 and < 6)
                    Interlocked.Increment(ref compressionCount[idx]);
                else if (me.Comptype == CompressionType.Compressiontype2Nd)
                    Interlocked.Increment(ref compressionCount[5]);
                return;
            }

            var self = chd.Map[me.Offset];
            me.SelfMapEntry = self;
            switch (self.Comptype)
            {
                case CompressionType.Compressiontype0:
                case CompressionType.Compressiontype1:
                case CompressionType.Compressiontype2:
                case CompressionType.Compressiontype3:
                case CompressionType.Compressionnone:
                case CompressionType.Compressiontype2Nd:
                    break;
                default:
                    LogUnexpectedCompType(Log, self.Comptype, null);
                    break;
            }

            lock (self)
            {
                Interlocked.Increment(ref self.UseCount);
                if (self.UseCount == 1)
                {
                    var uniqueIdx = (int)self.Comptype;
                    if (uniqueIdx is >= 0 and < 6)
                        Interlocked.Increment(ref compressionUniqueCount[uniqueIdx]);
                    else if (self.Comptype == CompressionType.Compressiontype2Nd)
                        Interlocked.Increment(ref compressionUniqueCount[5]);
                }
            }

            {
                var selfIdx = (int)self.Comptype;
                if (selfIdx is >= 0 and < 6)
                    Interlocked.Increment(ref compressionSelfCount[selfIdx]);
                else if (self.Comptype == CompressionType.Compressiontype2Nd)
                    Interlocked.Increment(ref compressionSelfCount[5]);
            }

            Interlocked.Increment(ref totalFound);
        });

        LogBlockSummary(Log, chd.Map.Length, totalFound, chd.Blocksize, null);
        for (var i = 0; i < 6; i++)
        {
            if ((compressionCount[i] == 0) & (compressionSelfCount[i] == 0))
                continue;

            var comp = "";
            if (i < chd.Compression.Length)
            {
                comp = chd.Compression[i].ToString();
            }
            else
                switch (i)
                {
                    case 4:
                        comp = "NONE";
                        break;
                    case 5:
                        comp = "2ND_COMPRESSED";
                        break;
                }

            LogCompressionStats(Log, i, comp, compressionCount[i], compressionUniqueCount[i], compressionSelfCount[i], null);
        }
    }

    /// <summary>Retains the most frequently used blocks for caching, promoting them to keep their decompressed buffers, and flattens remaining self-references.</summary>
    /// <param name="chd">The parsed CHD header containing the block map.</param>
    /// <param name="blocksToKeep">The maximum number of blocks to keep cached in memory.</param>
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

        LogRepeatedBlocksCount(Log, mapentries.Count, null);
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
            if (me.Comptype != CompressionType.Compressionself)
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
            me.SecondaryReader = me.SelfMapEntry.SecondaryReader;
            me.SelfMapEntry = null;
        });
    }

    /// <summary>Computes a relative weight for a map entry based on its compression type, used to prioritize blocks for caching.</summary>
    /// <param name="chd">The parsed CHD header.</param>
    /// <param name="me">The map entry to evaluate.</param>
    /// <returns>An integer weight where higher values indicate more expensive decompression.</returns>
    private static int GetWeigth(ChdHeader chd, MapEntry me)
    {
        switch (me.Comptype)
        {
            case CompressionType.Compressionnone:
                return 1;
            case CompressionType.Compressiontype2Nd:
                return chd.SecondaryCodec switch
                {
                    ChdCodec.Flac => 2,
                    ChdCodec.Lzma => 18,
                    ChdCodec.Zlib => 3,
                    _ => 1
                };
            default:
                switch (chd.Compression[(int)me.Comptype])
                {
                    case ChdCodec.Lzma: return 23;
                    case ChdCodec.Zlib: return 1;
                    case ChdCodec.Flac: return (me.Length == 41) ? 1 : 2;
                    case ChdCodec.Huffman: return 64;

                    case ChdCodec.Avhuff: return 1;

                    case ChdCodec.Cdflac: return (me.Length == 15) ? 1 : 2;
                    case ChdCodec.Cdlzma: return 18;
                    case ChdCodec.Cdzlib: return 3;
                    default: return 1;
                }
        }
    }

    /// <summary>Resolves compression codecs to their corresponding reader delegates and stores them in the header.</summary>
    /// <param name="chd">The parsed CHD header whose readers will be populated.</param>
    internal static void FindBlockReaders(ChdHeader chd)
    {
        chd.ChdReader = new ChdReader[chd.Compression.Length];
        for (var i = 0; i < chd.Compression.Length; i++)
        {
            chd.ChdReader[i] = GetReaderFromCodec(chd.Compression[i]);
        }

        if (chd.SecondaryCodec != ChdCodec.None)
        {
            chd.SecondaryChdReader = GetReaderFromCodec(chd.SecondaryCodec);
            foreach (var me in chd.Map)
            {
                if (me.Comptype == CompressionType.Compressiontype2Nd)
                {
                    me.SecondaryReader = chd.SecondaryChdReader;
                }
            }
        }
    }

    private static ChdReader GetReaderFromCodec(ChdCodec chdCodec)
    {
        switch (chdCodec)
        {
            case ChdCodec.Zlib: return ChdReaders.Zlib;
            case ChdCodec.Lzma: return ChdReaders.Lzma;
            case ChdCodec.Huffman: return ChdReaders.Huffman;
            case ChdCodec.Flac: return ChdReaders.Flac;
            case ChdCodec.Zstd: return ChdReaders.Zstd;
            case ChdCodec.Cdzlib: return ChdReaders.Cdzlib;
            case ChdCodec.Cdlzma: return ChdReaders.Cdlzma;
            case ChdCodec.Cdflac: return ChdReaders.Cdflac;
            case ChdCodec.Cdzstd: return ChdReaders.Cdzstd;
            case ChdCodec.Avhuff: return ChdReaders.AvHuff;
            case ChdCodec.None:
            case ChdCodec.Error: return ChdReaders.None;
            default: throw new NotSupportedException($"Unknown CHD codec: {chdCodec}");
        }
    }

    /// <summary>Decompresses a single map entry into the output buffer, handling compression, caching, self-references, and CRC validation.</summary>
    /// <param name="mapEntry">The map entry describing the compressed block.</param>
    /// <param name="arrPool">The array pool used for buffer rental and caching.</param>
    /// <param name="compression">The array of decompression delegates indexed by compression type.</param>
    /// <param name="codec">The codec-specific state and settings.</param>
    /// <param name="buffOut">The pre-allocated output buffer to receive decompressed data.</param>
    /// <param name="buffOutLength">The expected length of the decompressed data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    internal static ChdError ReadBlock(MapEntry mapEntry, ArrayPool arrPool, ChdReader[] compression, ChdCodecState codec, byte[] buffOut, int buffOutLength)
    {
        var checkCrc = true;

        switch (mapEntry.Comptype)
        {
            case CompressionType.Compressiontype0:
            case CompressionType.Compressiontype1:
            case CompressionType.Compressiontype2:
            case CompressionType.Compressiontype3:
            {
                lock (mapEntry)
                {
                    if (mapEntry.BuffOutCache == null)
                    {
                        var buffIn = mapEntry.BuffIn;
                        if (buffIn is null)
                            return ChdError.Chderrcodecerror;

                        var ret = compression[(int)mapEntry.Comptype].Invoke(buffIn, (int)mapEntry.Length, buffOut, buffOutLength, codec);

                        if (ret != ChdError.Chderrnone)
                            return ret;

                        // if this block is re-used keep a copy of it.
                        if (mapEntry.UseCount > 0)
                        {
                            mapEntry.BuffOutCache = arrPool.Rent();
                            Array.Copy(buffOut, 0, mapEntry.BuffOutCache, 0, buffOutLength);
                        }

                        break;
                    }

                    Array.Copy(mapEntry.BuffOutCache, 0, buffOut, 0, buffOutLength);

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
            case CompressionType.Compressionnone:
            {
                lock (mapEntry)
                {
                    if (mapEntry.BuffOutCache == null)
                    {
                        var buffIn = mapEntry.BuffIn;
                        if (buffIn is null)
                            return ChdError.Chderrcodecerror;

                        Array.Copy(buffIn, 0, buffOut, 0, buffOutLength);

                        if (mapEntry.UseCount > 0)
                        {
                            mapEntry.BuffOutCache = arrPool.Rent();
                            Array.Copy(buffOut, 0, mapEntry.BuffOutCache, 0, buffOutLength);
                        }

                        break;
                    }

                    Array.Copy(mapEntry.BuffOutCache, 0, buffOut, 0, buffOutLength);
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

            case CompressionType.Compressionmini:
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

            case CompressionType.Compressionzero:
            {
                Array.Clear(buffOut, 0, buffOutLength);
                checkCrc = false;
                break;
            }

            case CompressionType.Compressionself:
            {
                var self = mapEntry.SelfMapEntry;
                if (self is null)
                    return ChdError.Chderrinvaliddata;

                var retcs = ReadBlock(self, arrPool, compression, codec, buffOut, buffOutLength);
                if (retcs != ChdError.Chderrnone)
                    return retcs;
                // check CRC in the read_block_into_cache call
                checkCrc = false;
                break;
            }

            case CompressionType.Compressiontype2Nd:
            {
                if (mapEntry.SecondaryReader == null)
                    return ChdError.Chderrcodecerror;

                lock (mapEntry)
                {
                    if (mapEntry.BuffOutCache == null)
                    {
                        var buffIn = mapEntry.BuffIn;
                        if (buffIn is null)
                            return ChdError.Chderrcodecerror;

                        var ret = mapEntry.SecondaryReader.Invoke(buffIn, (int)mapEntry.Length, buffOut, buffOutLength, codec);

                        if (ret != ChdError.Chderrnone)
                            return ret;

                        if (mapEntry.UseCount > 0)
                        {
                            mapEntry.BuffOutCache = arrPool.Rent();
                            Array.Copy(buffOut, 0, mapEntry.BuffOutCache, 0, buffOutLength);
                        }

                        break;
                    }

                    Array.Copy(mapEntry.BuffOutCache, 0, buffOut, 0, buffOutLength);

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

            default:
                return ChdError.Chderrdecompressionerror;
        }

        if (checkCrc)
        {
            if ((mapEntry.Crc != null && !Crc.VerifyDigest((uint)mapEntry.Crc, buffOut, 0, (uint)buffOutLength)) || (mapEntry.Crc16 != null && Crc16.Calc(buffOut, buffOutLength) != mapEntry.Crc16))
            {
                return ChdError.Chderrdecompressionerror;
            }
        }

        return ChdError.Chderrnone;
    }
}