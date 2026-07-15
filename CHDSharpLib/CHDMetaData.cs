using System.Security.Cryptography;
using System.Text;
using CHDSharp.Models;
using CHDSharp.Utils;
using Serilog;

namespace CHDSharp;

/// <summary>Reads and validates CHD metadata blocks, computing SHA1 hashes over the concatenation of raw data SHA1 and sorted per-entry hashes.</summary>
internal static class ChdMetaData
{
    /// <summary>Flag indicating the metadata entry has an associated checksum.</summary>
    internal static readonly uint ChdMdflagsChecksum = 0x01;

    /// <summary>Reads all metadata entries from the CHD file header chain and validates the combined SHA1 against the stored header value.</summary>
    /// <param name="file">The stream positioned at the CHD file data.</param>
    /// <param name="chd">The parsed header containing the metadata offset and expected SHA1.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; <see cref="ChdError.Chderrinvalidmetadata"/> if the SHA1 does not match.</returns>
    internal static ChdError ReadMetaData(Stream file, ChdHeader chd)
    {
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var metaHashes = new List<byte[]>();

        while (chd.Metaoffset != 0)
        {
            file.Seek((long)chd.Metaoffset, SeekOrigin.Begin);
            var metaTag = br.ReadUInt32Be();
            var metaLength = br.ReadUInt32Be();
            var metaNext = br.ReadUInt64Be();
            var metaFlags = metaLength >> 24;
            metaLength &= 0x00ffffff;

            var metaData = new byte[metaLength];
            file.ReadExactly(metaData, 0, metaData.Length);

            Log.Debug("{MetaTag}  Length: {MetaLength}",
                $"{(char)((metaTag >> 24) & 0xFF)}{(char)((metaTag >> 16) & 0xFF)}{(char)((metaTag >> 8) & 0xFF)}{(char)((metaTag >> 0) & 0xFF)}",
                metaLength);
            if (Util.IsAscii(metaData))
                Log.Debug("Data: {MetaData}", Encoding.ASCII.GetString(metaData));
            else
                Log.Debug("Data: Binary Data Length {Length}", metaData.Length);

            // take the 4 byte metaTag, and the metaData
            // SHA1 the metaData to 20 byte SHA1
            // metadata_hash return these 24 bytes in a byte[24]
            if ((metaFlags & ChdMdflagsChecksum) != 0)
                metaHashes.Add(metadata_hash(metaTag, metaData));

            // set location of next meta data entry in the CHD (set to 0 if finished.)
            chd.Metaoffset = metaNext;
        }

        if (chd.Sha1 == null)
            return ChdError.Chderrnone;

        // binary sort the metaHashes
        metaHashes.Sort(Util.ByteArrCompare);

        // build the final SHA1
        // starting with the 20 byte rawsha1 from the main CHD data
        // then add the 24 byte for each meta data entry
        using var sha1Total = SHA1.Create();
        sha1Total.TransformBlock(chd.Rawsha1, 0, chd.Rawsha1.Length, null, 0);

        foreach (var t in metaHashes)
            sha1Total.TransformBlock(t, 0, t.Length, null, 0);

        var tmp = Array.Empty<byte>();
        sha1Total.TransformFinalBlock(tmp, 0, 0);

        // compare the calculated metaData + rawData SHA1 with sha1 from the CHD header
        if (!Util.IsAllZeroArray(chd.Sha1) && !Util.ByteArrEquals(chd.Sha1, sha1Total.Hash!))
            return ChdError.Chderrinvalidmetadata;

        return ChdError.Chderrnone;
    }

    private static byte[] metadata_hash(uint metaTag, byte[] metaData)
    {
        // make 24 byte metadata hash
        // 0-3  :  metaTag
        // 4-23 :  sha1 of the metaData

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
}
