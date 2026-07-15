using System.Security.Cryptography;
using System.Text;
using CHDSharp.Models;
using CHDSharp.Utils;
using Microsoft.Extensions.Logging;

namespace CHDSharp;

internal static class ChdMetaData
{
    private static readonly ILogger Log = ChdLogger.GetLogger(nameof(ChdMetaData));

    private static readonly Action<ILogger, string, uint, Exception?> LogMetaTag =
        LoggerMessage.Define<string, uint>(LogLevel.Debug, new EventId(1), "{Tag}  Length: {Length}");

    private static readonly Action<ILogger, string, Exception?> LogMetaDataText =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2), "Data: {Data}");

    private static readonly Action<ILogger, int, Exception?> LogMetaDataBinary =
        LoggerMessage.Define<int>(LogLevel.Debug, new EventId(3), "Data: Binary Data Length {Length}");

    internal static readonly uint ChdMdflagsChecksum = 0x01;

    internal static ChdError ReadMetaData(Stream file, ChdHeader chd)
    {
        var metaHashes = new List<byte[]>();

        foreach (var entry in ReadMetaDataInternal(file, chd, collectHashes: true))
        {
            if (entry.Hash != null)
                metaHashes.Add(entry.Hash);
        }

        metaHashes.Sort(Util.ByteArrCompare);

        using var sha1Total = SHA1.Create();
        sha1Total.TransformBlock(chd.Rawsha1, 0, chd.Rawsha1.Length, null, 0);

        foreach (var t in metaHashes)
            sha1Total.TransformBlock(t, 0, t.Length, null, 0);

        var tmp = Array.Empty<byte>();
        sha1Total.TransformFinalBlock(tmp, 0, 0);

        if (!Util.IsAllZeroArray(chd.Sha1) && !Util.ByteArrEquals(chd.Sha1, sha1Total.Hash!))
            return ChdError.Chderrinvalidmetadata;

        return ChdError.Chderrnone;
    }

    public static ChdError ReadMetaDataEntries(Stream file, ChdHeader chd,
        out List<ChdMetadataEntry> entries)
    {
        entries = [];
        ReadMetaDataInternal(file, chd, collectHashes: false, out var internalEntries);
        foreach (var e in internalEntries)
        {
            entries.Add(new ChdMetadataEntry((e.Tag), e.Data));
        }
        return ChdError.Chderrnone;
    }

    private static List<InternalEntry> ReadMetaDataInternal(Stream file, ChdHeader chd,
        bool collectHashes, out List<InternalEntry> entries)
    {
        entries = [];
        using var br = new BinaryReader(file, Encoding.UTF8, true);
        var results = new List<InternalEntry>();

        var currentOffset = chd.Metaoffset;
        while (currentOffset != 0)
        {
            file.Seek((long)currentOffset, SeekOrigin.Begin);
            var metaTag = br.ReadUInt32Be();
            var metaLength = br.ReadUInt32Be();
            var metaNext = br.ReadUInt64Be();
            var metaFlags = metaLength >> 24;
            metaLength &= 0x00ffffff;

            var metaData = new byte[metaLength];
            file.ReadExactly(metaData, 0, metaData.Length);

            var tag = $"{(char)((metaTag >> 24) & 0xFF)}{(char)((metaTag >> 16) & 0xFF)}{(char)((metaTag >> 8) & 0xFF)}{(char)((metaTag >> 0) & 0xFF)}";

            LogMetaTag(Log, tag, metaLength, null);
            if (Util.IsAscii(metaData))
                LogMetaDataText(Log, Encoding.ASCII.GetString(metaData), null);
            else
                LogMetaDataBinary(Log, metaData.Length, null);

            byte[]? hash = null;
            if (collectHashes && (metaFlags & ChdMdflagsChecksum) != 0)
            {
                hash = metadata_hash(metaTag, metaData);
            }

            results.Add(new InternalEntry { Tag = tag, Data = metaData, Hash = hash });

            currentOffset = metaNext;
        }

        entries = results;
        return results;
    }

    private static List<InternalEntry> ReadMetaDataInternal(Stream file, ChdHeader chd, bool collectHashes)
    {
        return ReadMetaDataInternal(file, chd, collectHashes, out _);
    }

    private static byte[] metadata_hash(uint metaTag, byte[] metaData)
    {
        var metaHash = new byte[24];
        metaHash[0] = (byte)((metaTag >> 24) & 0xff);
        metaHash[1] = (byte)((metaTag >> 16) & 0xff);
        metaHash[2] = (byte)((metaTag >> 8) & 0xff);
        metaHash[3] = (byte)((metaTag >> 0) & 0xff);
        var metaDataHash = SHA1.HashData(metaData);

        for (var i = 0; i < 20; i++)
        {
            metaHash[4 + i] = metaDataHash[i];
        }

        return metaHash;
    }

    private sealed class InternalEntry
    {
        public required string Tag;
        public required byte[] Data;
        public byte[]? Hash;
    }
}
