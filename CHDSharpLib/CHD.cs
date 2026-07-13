using CHDSharpLib.Utils;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CHDSharpLib;

internal class CHDHeader
{
    public chd_codec[] compression;
    public CHDReader[] chdReader;

    public ulong totalbytes;
    public uint blocksize;
    public uint totalblocks;

    // V5: size of a "unit" used for parent block address translation.
    // For V1-V4 parent entries the offset is a direct hunk index, so unitbytes
    // is only meaningful for V5 (set to blocksize as a harmless default otherwise).
    public uint unitbytes;

    // True when the V5 map is the uncompressed variant. In that map an offset
    // word of 0 means "read this hunk from the parent" (or zero-fill if none).
    public bool uncompressedMap;

    public MapEntry[] map;

    public byte[] md5; // just compressed data
    public byte[] rawsha1; // just compressed data
    public byte[] sha1; // includes the meta data

    public byte[] parentmd5;
    public byte[] parentsha1;

    public ulong metaoffset;
}

internal class MapEntry
{
    public compression_type comptype;
    public uint length; // length of compressed data
    public ulong offset; // offset of compressed data in file. Also index of source block for COMPRESSION_SELF 
    public uint? crc = null; // V3 & V4
    public ushort? crc16 = null; // V5

    public MapEntry selfMapEntry; // link to self MapEntry data used in COMPRESSION_SELF (replaces offset index)

    //Used to optimmize block reading so that any block in only decompressed once.
    public int UseCount;

    public byte[] buffIn = null;
    public byte[] buffOutCache = null;
    public byte[] buffOut = null;

    // Used in Parallel decompress to keep the blocks in order when hashing.
    public bool Processed = false;


    // Used to calculate which blocks should have buffered copies kept.
    public int UsageWeight;
    public bool KeepBufferCopy = false;
}


public static class CHD
{
    public static int taskCount = 8;

    public static chd_error CheckFile(Stream s, string filename, bool deepCheck, out uint? chdVersion, out byte[] chdSHA1, out byte[] chdMD5)
    {
        chdSHA1 = null;
        chdMD5 = null;
        chdVersion = null;

        if (!CheckHeader(s, out uint length, out uint version))
            return chd_error.CHDERR_INVALID_FILE;

        Log.Information("CHD Version {Version}", version);
        chd_error valid = chd_error.CHDERR_INVALID_DATA;
        CHDHeader chd = null;
        try
        {
            switch (version)
            {
                case 1:
                    valid = CHDHeaders.ReadHeaderV1(s, out chd);
                    break;
                case 2:
                    valid = CHDHeaders.ReadHeaderV2(s, out chd);
                    break;
                case 3:
                    valid = CHDHeaders.ReadHeaderV3(s, out chd);
                    break;
                case 4:
                    valid = CHDHeaders.ReadHeaderV4(s, out chd);
                    break;
                case 5:
                    valid = CHDHeaders.ReadHeaderV5(s, out chd);
                    break;
                default:
                    {
                        Log.Warning("Unknown version {Version}", version);
                        return chd_error.CHDERR_UNSUPPORTED_VERSION;
                    }
            }
        }
        catch
        {
            valid = chd_error.CHDERR_INVALID_DATA;
        }

        if (valid != chd_error.CHDERR_NONE)
        {
            Log.Warning("Child CHD found, cannot be processed");
            return valid;
        }

        chdSHA1 = chd.sha1 ?? chd.rawsha1;
        chdMD5 = chd.md5;
        chdVersion = version;

        if (!Util.IsAllZeroArray(chd.parentmd5) || !Util.IsAllZeroArray(chd.parentsha1))
        {
            Log.Warning("Child CHD found, cannot be processed");
            return chd_error.CHDERR_REQUIRES_PARENT;
        }

        if (!deepCheck)
            return chd_error.CHDERR_NONE;

        if (((ulong)chd.totalblocks * (ulong)chd.blocksize) != chd.totalbytes)
        {
            Log.Debug("{BlocksXSize} != {TotalBytes}", (ulong)chd.totalblocks * (ulong)chd.blocksize, chd.totalbytes);
        }


        string strComp = "";
        for (int i = 0; i < chd.compression.Length; i++)
        {
            strComp += $", {chd.compression[i].ToString().Substring(10)}";
        }
        Log.Information("{Filename}, V:{Version} {Compression}", Path.GetFileName(filename), version, strComp);

        CHDBlockRead.FindBlockReaders(chd);
        CHDBlockRead.FindRepeatedBlocks(chd);
        int blocksToKeep = (1024 * 1024 * 512) / (int)chd.blocksize;
        CHDBlockRead.KeepMostRepeatedBlocks(chd, blocksToKeep);

        valid = taskCount == 0 ? DecompressData(s, chd) : DecompressDataParallel(s, chd);

        if (valid != chd_error.CHDERR_NONE)
        {
            Log.Error("Data Decompress Failed: {Error}", valid);
            return valid;
        }

        valid = CHDMetaData.ReadMetaData(s, chd);

        if (valid != chd_error.CHDERR_NONE)
        {
            Log.Error("Meta Data Failed: {Error}", valid);
            return valid;
        }


        Log.Information("Valid");
        return chd_error.CHDERR_NONE;
    }

    /// <summary>
    /// Fully verifies a (possibly child/differential) CHD, resolving parent
    /// references against the CHD at <paramref name="parentFilename"/> (pass null
    /// for a standalone CHD). Reads the whole image via the random-access
    /// <see cref="CHDFile"/> API, validates the raw-data hash (SHA1/MD5) and, for
    /// V4/V5, the metadata SHA1. This is a sequential (single-threaded) verify -
    /// use <see cref="CheckFile"/> for the fast parallel path on standalone CHDs.
    /// </summary>
    public static chd_error CheckFileWithParent(string filename, string parentFilename,
        out uint? chdVersion, out byte[] chdSHA1, out byte[] chdMD5)
    {
        chdVersion = null;
        chdSHA1 = null;
        chdMD5 = null;

        chd_error err = CHDFile.Open(filename, parentFilename, out CHDFile chd);
        if (err != chd_error.CHDERR_NONE)
            return err;

        using (chd)
        {
            chdVersion = chd.Version;
            chdSHA1 = chd.SHA1;
            chdMD5 = chd.MD5;

            byte[] expectedSha1 = chd.RawSHA1;
            byte[] expectedMd5 = chd.MD5;
            bool haveSha1 = expectedSha1 != null && !Util.IsAllZeroArray(expectedSha1);
            bool haveMd5 = expectedMd5 != null && !Util.IsAllZeroArray(expectedMd5);

            using MD5 md5Check = haveMd5 ? MD5.Create() : null;
            using SHA1 sha1Check = haveSha1 ? SHA1.Create() : null;

            byte[] buffer = new byte[chd.HunkBytes];
            ulong sizetoGo = chd.TotalBytes;
            ulong offset = 0;
            while (sizetoGo > 0)
            {
                int chunk = (int)Math.Min((ulong)buffer.Length, sizetoGo);
                err = chd.Read(offset, buffer, 0, chunk);
                if (err != chd_error.CHDERR_NONE)
                    return err;
                md5Check?.TransformBlock(buffer, 0, chunk, null, 0);
                sha1Check?.TransformBlock(buffer, 0, chunk, null, 0);
                offset += (ulong)chunk;
                sizetoGo -= (ulong)chunk;
            }

            byte[] tmp = new byte[0];
            md5Check?.TransformFinalBlock(tmp, 0, 0);
            sha1Check?.TransformFinalBlock(tmp, 0, 0);

            if (haveMd5 && !Util.ByteArrEquals(expectedMd5, md5Check.Hash))
                return chd_error.CHDERR_DECOMPRESSION_ERROR;
            if (haveSha1 && !Util.ByteArrEquals(expectedSha1, sha1Check.Hash))
                return chd_error.CHDERR_DECOMPRESSION_ERROR;

            return chd_error.CHDERR_NONE;
        }
    }

    private static readonly uint[] HeaderLengths = new uint[] { 0, 76, 80, 120, 108, 124 };
    private static readonly byte[] id = { (byte)'M', (byte)'C', (byte)'o', (byte)'m', (byte)'p', (byte)'r', (byte)'H', (byte)'D' };

    public static bool CheckHeader(Stream file, out uint length, out uint version)
    {
        for (int i = 0; i < id.Length; i++)
        {
            byte b = (byte)file.ReadByte();
            if (b != id[i])
            {
                length = 0;
                version = 0;
                return false;
            }
        }

        using (BinaryReader br = new BinaryReader(file, Encoding.UTF8, true))
        {
            length = br.ReadUInt32BE();
            version = br.ReadUInt32BE();
            return HeaderLengths[version] == length;
        }
    }


    internal static chd_error DecompressData(Stream file, CHDHeader chd)
    {
        // stores the FLAC decompression classes for this instance.
        CHDCodec codec = new CHDCodec();

        using BinaryReader br = new BinaryReader(file, Encoding.UTF8, true);

        using MD5 md5Check = chd.md5 != null ? MD5.Create() : null;
        using SHA1 sha1Check = chd.rawsha1 != null ? SHA1.Create() : null;

        ArrayPool arrPool = new ArrayPool(chd.blocksize);

        byte[] buffer = new byte[chd.blocksize];

        try
        {
            int block = 0;
            ulong sizetoGo = chd.totalbytes;
            while (sizetoGo > 0)
            {
                /* progress */
                if ((block % 1000) == 0)
                    Log.Debug("Verifying, {Percent:N1}% complete", 100 - sizetoGo * 100 / chd.totalbytes);

                MapEntry mapEntry = chd.map[block];
                if (mapEntry.length > 0)
                {
                    mapEntry.buffIn = arrPool.Rent();
                    file.Seek((long)mapEntry.offset, System.IO.SeekOrigin.Begin);
                    file.ReadExactly(mapEntry.buffIn, 0, (int)mapEntry.length);
                }

                /* read the block into the cache */
                chd_error err = CHDBlockRead.ReadBlock(mapEntry, arrPool, chd.chdReader, codec, buffer, (int)chd.blocksize);
                if (err != chd_error.CHDERR_NONE)
                    return err;

                if (mapEntry.length > 0)
                {
                    arrPool.Return(mapEntry.buffIn);
                    mapEntry.buffIn = null;
                }

                int sizenext = sizetoGo > (ulong)chd.blocksize ? (int)chd.blocksize : (int)sizetoGo;

                md5Check?.TransformBlock(buffer, 0, sizenext, null, 0);
                sha1Check?.TransformBlock(buffer, 0, sizenext, null, 0);

                /* prepare for the next block */
                block++;
                sizetoGo -= (ulong)sizenext;

            }
            Log.Debug("Verifying, 100.0% complete");
        }
        catch (Exception e)
        {
            Log.Error(e, "Data Decompress Failed");
            return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }

        byte[] tmp = new byte[0];
        md5Check?.TransformFinalBlock(tmp, 0, 0);
        sha1Check?.TransformFinalBlock(tmp, 0, 0);

        // here it is now using the rawsha1 value from the header to validate the raw binary data.
        if (chd.md5 != null && !Util.IsAllZeroArray(chd.md5) && !Util.ByteArrEquals(chd.md5, md5Check.Hash))
        {
            return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }
        if (chd.rawsha1 != null && !Util.IsAllZeroArray(chd.rawsha1) && !Util.ByteArrEquals(chd.rawsha1, sha1Check.Hash))
        {
            return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }

        return chd_error.CHDERR_NONE;
    }



    internal static chd_error DecompressDataParallel(Stream file, CHDHeader chd)
    {
        using BinaryReader br = new BinaryReader(file, Encoding.UTF8, true);

        using MD5 md5Check = chd.md5 != null ? MD5.Create() : null;
        using SHA1 sha1Check = chd.rawsha1 != null ? SHA1.Create() : null;

        using BlockingCollection<int> blocksToDecompress = new BlockingCollection<int>(taskCount * 100);
        using BlockingCollection<int> blocksToHash = new BlockingCollection<int>(taskCount * 100);
        chd_error errMaster = chd_error.CHDERR_NONE;

        List<Task> allTasks = new List<Task>();

        var ts = new CancellationTokenSource();
        CancellationToken ct = ts.Token;

        ArrayPool arrPoolIn = new ArrayPool(chd.blocksize);
        ArrayPool arrPoolOut = new ArrayPool(chd.blocksize);
        ArrayPool arrPoolCache = new ArrayPool(chd.blocksize);

        int blocksToKeep = (1024 * 1024 * 512) / (int)chd.blocksize;
        SemaphoreSlim aheadLock = new SemaphoreSlim(blocksToKeep, blocksToKeep);

        Task producerThread = Task.Factory.StartNew(() =>
        {
            try
            {
                uint blockPercent = chd.totalblocks / 100;
                if (blockPercent == 0)
                    blockPercent = 1;

                for (int block = 0; block < chd.totalblocks; block++)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    /* progress */
                    if ((block % blockPercent) == 0)
                    {
                        //arrPoolIn.ReadStats(out int issuedArraysTotalIn, out int returnedArraysTotalIn);
                        //arrPoolOut.ReadStats(out int issuedArraysTotalOut, out int returnedArraysTotalOut);
                        //arrPoolCache.ReadStats(out int issuedArraysTotalCache, out int returnedArraysTotalCache);
                        //progress?.Invoke($"Verifying: {(long)block * 100 / chd.totalblocks:N0}%     Load buffer: {blocksToDecompress.Count}   Hash buffer: {blocksToHash.Count}  {issuedArraysTotalIn},{returnedArraysTotalIn} | {issuedArraysTotalOut},{returnedArraysTotalOut} | {issuedArraysTotalCache},{returnedArraysTotalCache}\r");

                        //progress?.Invoke($"Verifying: {(long)block * 100 / chd.totalblocks:N0}%     Load buffer: {blocksToDecompress.Count}    Hash buffer: {blocksToHash.Count}");;

                        Log.Debug("Verifying: {Percent:N0}%", (long)block * 100 / chd.totalblocks);
                    }
                    MapEntry mapEntry = chd.map[block];

                    if (mapEntry.length > 0)
                    {
                        if (file.Position != (long)mapEntry.offset)
                            file.Seek((long)mapEntry.offset, System.IO.SeekOrigin.Begin);

                        mapEntry.buffIn = arrPoolIn.Rent();
                        file.ReadExactly(mapEntry.buffIn, 0, (int)mapEntry.length);
                    }

                    blocksToDecompress.Add(block, ct);
                }
                // this must be done to tell all the decompression threads to stop working and return.
                for (int i = 0; i < taskCount; i++)
                    blocksToDecompress.Add(-1);

            }
            catch
            {
                if (ct.IsCancellationRequested)
                    return;

                if (errMaster == chd_error.CHDERR_NONE)
                    errMaster = chd_error.CHDERR_INVALID_FILE;
                ts.Cancel();
            }
        });
        allTasks.Add(producerThread);




        for (int i = 0; i < taskCount; i++)
        {
            Task decompressionThread = Task.Factory.StartNew(() =>
            {
                try
                {
                    CHDCodec codec = new CHDCodec();
                    while (true)
                    {
                        aheadLock.Wait(ct);
                        int block = blocksToDecompress.Take(ct);
                        if (block == -1)
                            return;
                        MapEntry mapEntry = chd.map[block];
                        mapEntry.buffOut = arrPoolOut.Rent();
                        chd_error err = CHDBlockRead.ReadBlock(mapEntry, arrPoolCache, chd.chdReader, codec, mapEntry.buffOut, (int)chd.blocksize);
                        if (err != chd_error.CHDERR_NONE)
                        {
                            ts.Cancel();
                            errMaster = err;
                            return;
                        }
                        blocksToHash.Add(block, ct);

                        if (mapEntry.length > 0)
                        {
                            arrPoolIn.Return(mapEntry.buffIn);
                            mapEntry.buffIn = null;
                        }
                    }
                }
                catch
                {
                    if (ct.IsCancellationRequested)
                        return;
                    if (errMaster == chd_error.CHDERR_NONE)
                        errMaster = chd_error.CHDERR_DECOMPRESSION_ERROR;
                    ts.Cancel();
                }
            });

            allTasks.Add(decompressionThread);

        }

        ulong sizetoGo = chd.totalbytes;
        int proc = 0;
        Task hashingThread = Task.Factory.StartNew(() =>
        {
            try
            {
                while (true)
                {
                    int item = blocksToHash.Take(ct);

                    chd.map[item].Processed = true;
                    while (chd.map[proc].Processed == true)
                    {
                        int sizenext = sizetoGo > (ulong)chd.blocksize ? (int)chd.blocksize : (int)sizetoGo;

                        MapEntry mapEntry = chd.map[proc];

                        md5Check?.TransformBlock(mapEntry.buffOut, 0, sizenext, null, 0);
                        sha1Check?.TransformBlock(mapEntry.buffOut, 0, sizenext, null, 0);

                        arrPoolOut.Return(mapEntry.buffOut);
                        mapEntry.buffOut = null;
                        aheadLock.Release();

                        /* prepare for the next block */
                        sizetoGo -= (ulong)sizenext;

                        proc++;
                        if (proc == chd.totalblocks)
                            return;
                    }
                }
            }
            catch
            {
                if (ct.IsCancellationRequested)
                    return;

                if (errMaster == chd_error.CHDERR_NONE)
                    errMaster = chd_error.CHDERR_DECOMPRESSION_ERROR;
                ts.Cancel();
            }

        });
        allTasks.Add(hashingThread);

        Task.WaitAll(allTasks.ToArray());


        Log.Debug("Verifying, 100% complete");

        arrPoolIn.ReadStats(out int issuedArraysTotal, out int returnedArraysTotal);
        Log.Debug("In: Issued Arrays Total {Issued}, returned Arrays Total {Returned}, block size {BlockSize}", issuedArraysTotal, returnedArraysTotal, chd.blocksize);
        arrPoolOut.ReadStats(out issuedArraysTotal, out returnedArraysTotal);
        Log.Debug("Out: Issued Arrays Total {Issued}, returned Arrays Total {Returned}, block size {BlockSize}", issuedArraysTotal, returnedArraysTotal, chd.blocksize);
        arrPoolCache.ReadStats(out issuedArraysTotal, out returnedArraysTotal);
        Log.Debug("Cache: Issued Arrays Total {Issued}, returned Arrays Total {Returned}, block size {BlockSize}", issuedArraysTotal, returnedArraysTotal, chd.blocksize);

        if (errMaster != chd_error.CHDERR_NONE)

            if (errMaster != chd_error.CHDERR_NONE)
                return errMaster;

        byte[] tmp = new byte[0];
        md5Check?.TransformFinalBlock(tmp, 0, 0);
        sha1Check?.TransformFinalBlock(tmp, 0, 0);

        // here it is now using the rawsha1 value from the header to validate the raw binary data.
        if (chd.md5 != null && !Util.IsAllZeroArray(chd.md5) && !Util.ByteArrEquals(chd.md5, md5Check.Hash))
        {
            return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }
        if (chd.rawsha1 != null && !Util.IsAllZeroArray(chd.rawsha1) && !Util.ByteArrEquals(chd.rawsha1, sha1Check.Hash))
        {
            return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }

        return chd_error.CHDERR_NONE;
    }

}