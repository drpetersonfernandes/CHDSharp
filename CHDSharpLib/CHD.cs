using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CHDSharp.Models;
using CHDSharp.Utils;
using Serilog;

namespace CHDSharp;

/// <summary>Provides methods for validating and inspecting CHD files using parallel decompression.</summary>
public static class Chd
{
    /// <summary>Number of parallel decompression tasks used during verification.</summary>
    public const int TaskCount = 8;

    /// <summary>
    /// Validates a CHD file from a <see cref="Stream"/> using parallel decompression and hash verification.
    /// Returns the CHD version, SHA1, and MD5 hashes.
    /// </summary>
    /// <param name="s">The stream to read the CHD from.</param>
    /// <param name="filename">The filename associated with the stream, used for logging.</param>
    /// <param name="deepCheck">If <c>true</c>, performs full decompression and hash verification; otherwise only validates the header.</param>
    /// <param name="chdVersion">When this method returns, contains the CHD version number, or <c>null</c> if invalid.</param>
    /// <param name="chdSha1">When this method returns, contains the SHA1 hash from the header, or <c>null</c> if invalid.</param>
    /// <param name="chdMd5">When this method returns, contains the MD5 hash from the header, or <c>null</c> if invalid.</param>
    /// <returns><see cref="chd_error.CHDERR_NONE"/> on success; otherwise an error code.</returns>
    /// <remarks>This method does not handle differential (parent/child) CHDs. Use <see cref="CheckFileWithParent"/> for those.</remarks>
    public static chd_error CheckFile(Stream s, string filename, bool deepCheck, out uint? chdVersion, out byte[]? chdSha1, out byte[]? chdMd5)
    {
        chdSha1 = null;
        chdMd5 = null;
        chdVersion = null;

        if (!CheckHeader(s, out _, out var version))
            return chd_error.CHDERR_INVALID_FILE;

        Log.Information("CHD Version {Version}", version);
        chd_error valid;
        ChdHeader? chd = null;
        try
        {
            switch (version)
            {
                case 1:
                    valid = ChdHeaders.ReadHeaderV1(s, out chd);
                    break;
                case 2:
                    valid = ChdHeaders.ReadHeaderV2(s, out chd);
                    break;
                case 3:
                    valid = ChdHeaders.ReadHeaderV3(s, out chd);
                    break;
                case 4:
                    valid = ChdHeaders.ReadHeaderV4(s, out chd);
                    break;
                case 5:
                    valid = ChdHeaders.ReadHeaderV5(s, out chd);
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

        if (chd != null)
        {
            chdSha1 = chd.Sha1;
            chdMd5 = chd.Md5;
            chdVersion = version;

            if (!Util.IsAllZeroArray(chd.Parentmd5) || !Util.IsAllZeroArray(chd.Parentsha1))
            {
                Log.Warning("Child CHD found, cannot be processed");
                return chd_error.CHDERR_REQUIRES_PARENT;
            }

            if (!deepCheck)
                return chd_error.CHDERR_NONE;

            if (((ulong)chd.Totalblocks * (ulong)chd.Blocksize) != chd.Totalbytes)
            {
                Log.Debug("{BlocksXSize} != {TotalBytes}", (ulong)chd.Totalblocks * (ulong)chd.Blocksize, chd.Totalbytes);
            }

            var strComp = "";
            foreach (var t in chd.Compression)
            {
                strComp += $", {t.ToString().Substring(10)}";
            }

            Log.Information("{Filename}, V:{Version} {Compression}", Path.GetFileName(filename), version, strComp);

            ChdBlockRead.FindBlockReaders(chd);
            ChdBlockRead.FindRepeatedBlocks(chd);
            var blocksToKeep = (1024 * 1024 * 512) / (int)chd.Blocksize;
            ChdBlockRead.KeepMostRepeatedBlocks(chd, blocksToKeep);

            valid = DecompressDataParallel(s, chd);

            if (valid != chd_error.CHDERR_NONE)
            {
                Log.Error("Data Decompress Failed: {Error}", valid);
                return valid;
            }

            valid = ChdMetaData.ReadMetaData(s, chd);
        }

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
    /// <see cref="ChdFile"/> API, validates the raw-data hash (SHA1/MD5) and, for
    /// V4/V5, the metadata SHA1. This is a sequential (single-threaded) verify -
    /// use <see cref="CheckFile"/> for the fast parallel path on standalone CHDs.
    /// </summary>
    public static chd_error CheckFileWithParent(string filename, string parentFilename,
        out uint? chdVersion, out byte[]? chdSha1, out byte[]? chdMd5)
    {
        chdVersion = null;
        chdSha1 = null;
        chdMd5 = null;

        var err = ChdFile.Open(filename, parentFilename, out var chd);
        if (err != chd_error.CHDERR_NONE)
            return err;

        using (chd)
        {
            chdVersion = chd!.Version;
            chdSha1 = chd!.Sha1;
            chdMd5 = chd!.Md5;

            var expectedSha1 = chd.RawSha1;
            var expectedMd5 = chd.Md5;
            var haveSha1 = expectedSha1 != null && !Util.IsAllZeroArray(expectedSha1);
            var haveMd5 = expectedMd5 != null && !Util.IsAllZeroArray(expectedMd5);

            using var md5Check = haveMd5 ? MD5.Create() : null;
            using var sha1Check = haveSha1 ? SHA1.Create() : null;

            var buffer = new byte[chd.HunkBytes];
            var sizetoGo = chd.TotalBytes;
            ulong offset = 0;
            while (sizetoGo > 0)
            {
                var chunk = (int)Math.Min((ulong)buffer.Length, sizetoGo);
                err = chd.Read(offset, buffer, 0, chunk);
                if (err != chd_error.CHDERR_NONE)
                    return err;

                md5Check?.TransformBlock(buffer, 0, chunk, null, 0);
                sha1Check?.TransformBlock(buffer, 0, chunk, null, 0);
                offset += (ulong)chunk;
                sizetoGo -= (ulong)chunk;
            }

            var tmp = Array.Empty<byte>();
            md5Check?.TransformFinalBlock(tmp, 0, 0);
            sha1Check?.TransformFinalBlock(tmp, 0, 0);

            if (expectedMd5 != null && md5Check != null && sha1Check != null && md5Check.Hash != null && sha1Check.Hash != null && expectedSha1 != null && ((haveMd5 && !Util.ByteArrEquals(expectedMd5, md5Check.Hash)) || (haveSha1 && !Util.ByteArrEquals(expectedSha1, sha1Check.Hash))))
                return chd_error.CHDERR_DECOMPRESSION_ERROR;

            return chd_error.CHDERR_NONE;
        }
    }

    private static readonly uint[] HeaderLengths = [0, 76, 80, 120, 108, 124];
    private static readonly byte[] Id = "MComprHD"u8.ToArray();

    /// <summary>Reads and validates the CHD file header signature and version.</summary>
    /// <param name="file">The stream positioned at the start of the CHD file.</param>
    /// <param name="length">When this method returns, contains the header length in bytes.</param>
    /// <param name="version">When this method returns, contains the CHD version number.</param>
    /// <returns><c>true</c> if the header signature is valid and the version is recognized; otherwise <c>false</c>.</returns>
    public static bool CheckHeader(Stream file, out uint length, out uint version)
    {
        foreach (var t in Id)
        {
            var b = (byte)file.ReadByte();
            if (b != t)
            {
                length = 0;
                version = 0;
                return false;
            }
        }

        using var br = new BinaryReader(file, Encoding.UTF8, true);
        length = br.ReadUInt32BE();
        version = br.ReadUInt32BE();
        return HeaderLengths[version] == length;
    }


    internal static chd_error DecompressDataParallel(Stream file, ChdHeader chd)
    {
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var md5Check = chd.Md5 != null ? MD5.Create() : null;
        var sha1Check = chd.Rawsha1 != null ? SHA1.Create() : null;
        var blocksToDecompress = new BlockingCollection<int>(TaskCount * 100);
        var blocksToHash = new BlockingCollection<int>(TaskCount * 100);
        try
        {
            var errMaster = chd_error.CHDERR_NONE;

            var allTasks = new List<Task>();

            var ts = new CancellationTokenSource();
            var ct = ts.Token;

            var arrPoolIn = new ArrayPool(chd.Blocksize);
            var arrPoolOut = new ArrayPool(chd.Blocksize);
            var arrPoolCache = new ArrayPool(chd.Blocksize);

            var blocksToKeep = (1024 * 1024 * 512) / (int)chd.Blocksize;
            var aheadLock = new SemaphoreSlim(blocksToKeep, blocksToKeep);

            var producerThread = Task.Factory.StartNew(() =>
            {
                try
                {
                    var blockPercent = chd.Totalblocks / 100;
                    if (blockPercent == 0)
                    {
                        blockPercent = 1;
                    }

                    for (var block = 0; block < chd.Totalblocks; block++)
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

                            Log.Debug("Verifying: {Percent:N0}%", (long)block * 100 / chd.Totalblocks);
                        }

                        var mapEntry = chd.Map[block];

                        if (mapEntry.Length > 0)
                        {
                            if (file.Position != (long)mapEntry.Offset)
                                file.Seek((long)mapEntry.Offset, SeekOrigin.Begin);

                            mapEntry.BuffIn = arrPoolIn.Rent();
                            file.ReadExactly(mapEntry.BuffIn, 0, (int)mapEntry.Length);
                        }

                        blocksToDecompress.Add(block, ct);
                    }

                    // this must be done to tell all the decompression threads to stop working and return.
                    for (var i = 0; i < TaskCount; i++)
                        blocksToDecompress.Add(-1);
                }
                catch
                {
                    if (ct.IsCancellationRequested)
                        return;

                    if (errMaster == chd_error.CHDERR_NONE)
                    {
                        errMaster = chd_error.CHDERR_INVALID_FILE;
                    }

                    ts.Cancel();
                }
            });
            allTasks.Add(producerThread);

            for (var i = 0; i < TaskCount; i++)
            {
                var decompressionThread = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        var codec = new CHDCodec();
                        while (true)
                        {
                            aheadLock.Wait(ct);
                            var block = blocksToDecompress.Take(ct);
                            if (block == -1)
                                return;

                            var mapEntry = chd.Map[block];
                            mapEntry.BuffOut = arrPoolOut.Rent();
                            var err = ChdBlockRead.ReadBlock(mapEntry, arrPoolCache, chd.ChdReader, codec, mapEntry.BuffOut, (int)chd.Blocksize);
                            if (err != chd_error.CHDERR_NONE)
                            {
                                ts.Cancel();
                                errMaster = err;
                                return;
                            }

                            blocksToHash.Add(block, ct);

                            if (mapEntry.Length > 0)
                            {
                                arrPoolIn.Return(mapEntry.BuffIn);
                                mapEntry.BuffIn = null!;
                            }
                        }
                    }
                    catch
                    {
                        if (ct.IsCancellationRequested)
                            return;

                        if (errMaster == chd_error.CHDERR_NONE)
                        {
                            errMaster = chd_error.CHDERR_DECOMPRESSION_ERROR;
                        }

                        ts.Cancel();
                    }
                });

                allTasks.Add(decompressionThread);
            }

            var sizetoGo = chd.Totalbytes;
            var proc = 0;
            var hashingThread = Task.Factory.StartNew(() =>
            {
                try
                {
                    while (true)
                    {
                        var item = blocksToHash.Take(ct);

                        chd.Map[item].Processed = true;
                        while (chd.Map[proc].Processed)
                        {
                            var sizenext = sizetoGo > (ulong)chd.Blocksize ? (int)chd.Blocksize : (int)sizetoGo;

                            var mapEntry = chd.Map[proc];

                            md5Check?.TransformBlock(mapEntry.BuffOut, 0, sizenext, null, 0);
                            sha1Check?.TransformBlock(mapEntry.BuffOut, 0, sizenext, null, 0);

                            arrPoolOut.Return(mapEntry.BuffOut);
                            mapEntry.BuffOut = null!;
                            aheadLock.Release();

                            /* prepare for the next block */
                            sizetoGo -= (ulong)sizenext;

                            proc++;
                            if (proc == chd.Totalblocks)
                                return;
                        }
                    }
                }
                catch
                {
                    if (ct.IsCancellationRequested)
                        return;

                    if (errMaster == chd_error.CHDERR_NONE)
                    {
                        errMaster = chd_error.CHDERR_DECOMPRESSION_ERROR;
                    }

                    ts.Cancel();
                }
            });
            allTasks.Add(hashingThread);

            Task.WaitAll(allTasks.ToArray());


            Log.Debug("Verifying, 100% complete");

            arrPoolIn.ReadStats(out var issuedArraysTotal, out var returnedArraysTotal);
            Log.Debug("In: Issued Arrays Total {Issued}, returned Arrays Total {Returned}, block size {BlockSize}", issuedArraysTotal, returnedArraysTotal, chd.Blocksize);
            arrPoolOut.ReadStats(out issuedArraysTotal, out returnedArraysTotal);
            Log.Debug("Out: Issued Arrays Total {Issued}, returned Arrays Total {Returned}, block size {BlockSize}", issuedArraysTotal, returnedArraysTotal, chd.Blocksize);
            arrPoolCache.ReadStats(out issuedArraysTotal, out returnedArraysTotal);
            Log.Debug("Cache: Issued Arrays Total {Issued}, returned Arrays Total {Returned}, block size {BlockSize}", issuedArraysTotal, returnedArraysTotal, chd.Blocksize);

            if (errMaster != chd_error.CHDERR_NONE)
                return errMaster;

            var tmp = Array.Empty<byte>();
            md5Check?.TransformFinalBlock(tmp, 0, 0);
            sha1Check?.TransformFinalBlock(tmp, 0, 0);

            // here it is now using the rawsha1 value from the header to validate the raw binary data.
            if (chd.Md5 != null && !Util.IsAllZeroArray(chd.Md5) && md5Check is { Hash: not null } && !Util.ByteArrEquals(chd.Md5, md5Check.Hash))
            {
                return chd_error.CHDERR_DECOMPRESSION_ERROR;
            }

            if (chd.Rawsha1 != null && !Util.IsAllZeroArray(chd.Rawsha1) && sha1Check is { Hash: not null } && !Util.ByteArrEquals(chd.Rawsha1, sha1Check.Hash))
            {
                return chd_error.CHDERR_DECOMPRESSION_ERROR;
            }

            return chd_error.CHDERR_NONE;
        }
        finally
        {
            blocksToDecompress.Dispose();
            blocksToHash.Dispose();
            md5Check?.Dispose();
            sha1Check?.Dispose();
        }
    }
}
