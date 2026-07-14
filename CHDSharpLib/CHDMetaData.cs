using System.Security.Cryptography;
using System.Text;
using CHDSharp.Utils;
using Serilog;

namespace CHDSharp;

internal static class CHDMetaData
{
    internal static uint CHD_MDFLAGS_CHECKSUM = 0x01;        // indicates data is checksummed

    internal static chd_error ReadMetaData(Stream file, ChdHeader chd)
    {
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var metaHashes = new List<byte[]>();

        while (chd.Metaoffset != 0)
        {
            file.Seek((long)chd.Metaoffset, SeekOrigin.Begin);
            var metaTag = br.ReadUInt32BE();
            var metaLength = br.ReadUInt32BE();
            var metaNext = br.ReadUInt64BE();
            var metaFlags = metaLength >> 24;
            metaLength &= 0x00ffffff;

            var metaData = new byte[metaLength];
            file.ReadExactly(metaData, 0, metaData.Length);

            Log.Debug("{MetaTag}  Length: {MetaLength}",
                $"{(char)((metaTag >> 24) & 0xFF)}{(char)((metaTag >> 16) & 0xFF)}{(char)((metaTag >> 8) & 0xFF)}{(char)((metaTag >> 0) & 0xFF)}",
                metaLength);
            if (Util.isAscii(metaData))
                Log.Debug("Data: {MetaData}", Encoding.ASCII.GetString(metaData));
            else
                Log.Debug("Data: Binary Data Length {Length}", metaData.Length);

            // take the 4 byte metaTag, and the metaData
            // SHA1 the metaData to 20 byte SHA1
            // metadata_hash return these 24 bytes in a byte[24]
            if ((metaFlags & CHD_MDFLAGS_CHECKSUM) != 0)
                metaHashes.Add(metadata_hash(metaTag, metaData));

            // set location of next meta data entry in the CHD (set to 0 if finished.)
            chd.Metaoffset = metaNext;
        }

        if (chd.Sha1 == null)
            return chd_error.CHDERR_NONE;

        // binary sort the metaHashes
        metaHashes.Sort(Util.ByteArrCompare);

        // build the final SHA1
        // starting with the 20 byte rawsha1 from the main CHD data
        // then add the 24 byte for each meta data entry
        using var sha1Total = SHA1.Create();
        sha1Total.TransformBlock(chd.Rawsha1, 0, chd.Rawsha1.Length, null, 0);

        for (var i = 0; i < metaHashes.Count; i++)
            sha1Total.TransformBlock(metaHashes[i], 0, metaHashes[i].Length, null, 0);

        var tmp = Array.Empty<byte>();
        sha1Total.TransformFinalBlock(tmp, 0, 0);

        // compare the calculated metaData + rawData SHA1 with sha1 from the CHD header
        if (!Util.IsAllZeroArray(chd.Sha1) && !Util.ByteArrEquals(chd.Sha1, sha1Total.Hash!))
            return chd_error.CHDERR_INVALID_METADATA;

        return chd_error.CHDERR_NONE;
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
        using var sha1 = SHA1.Create();
        var metaDataHash = sha1.ComputeHash(metaData);

        for (var i = 0; i < 20; i++)
        {
            metaHash[4 + i] = metaDataHash[i];
        }

        return metaHash;
    }
}
