using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CHDSharp.Models;
using CHDSharp.Utils;
using Microsoft.Extensions.Logging;

namespace CHDSharp;

/// <summary>Provides methods for validating, inspecting, and reading CHD files using parallel decompression.</summary>
public static class Chd
{
    private static readonly ILogger Log = ChdLogger.GetLogger(nameof(Chd));

    private static readonly Action<ILogger, uint, Exception?> LogChdVersion =
        LoggerMessage.Define<uint>(LogLevel.Information, new EventId(1), "CHD Version {Version}");

    private static readonly Action<ILogger, uint, Exception?> LogUnknownVersion =
        LoggerMessage.Define<uint>(LogLevel.Warning, new EventId(2), "Unknown version {Version}");

    private static readonly Action<ILogger, Exception?> LogChildChdFound =
        LoggerMessage.Define(LogLevel.Warning, new EventId(3), "Child CHD found, cannot be processed");

    private static readonly Action<ILogger, ulong, ulong, Exception?> LogBlockSizeMismatch =
        LoggerMessage.Define<ulong, ulong>(LogLevel.Debug, new EventId(4), "{BlocksXSize} != {TotalBytes}");

    private static readonly Action<ILogger, string, uint, string, Exception?> LogFileInfo =
        LoggerMessage.Define<string, uint, string>(LogLevel.Information, new EventId(5), "{Filename}, V:{Version} {Compression}");

    private static readonly Action<ILogger, ChdError, Exception?> LogDecompressFailed =
        LoggerMessage.Define<ChdError>(LogLevel.Error, new EventId(6), "Data Decompress Failed: {Error}");

    private static readonly Action<ILogger, ChdError, Exception?> LogMetaDataFailed =
        LoggerMessage.Define<ChdError>(LogLevel.Error, new EventId(7), "Meta Data Failed: {Error}");

    private static readonly Action<ILogger, Exception?> LogValid =
        LoggerMessage.Define(LogLevel.Information, new EventId(8), "Valid");

    private static readonly Action<ILogger, long, Exception?> LogVerifyingPercent =
        LoggerMessage.Define<long>(LogLevel.Debug, new EventId(9), "Verifying: {Percent:N0}%");

    private static readonly Action<ILogger, Exception?> LogVerifyingComplete =
        LoggerMessage.Define(LogLevel.Debug, new EventId(10), "Verifying, 100% complete");

    private static readonly Action<ILogger, string, int, int, uint, Exception?> LogArrayStats =
        LoggerMessage.Define<string, int, int, uint>(LogLevel.Debug, new EventId(11), "{Where}: Issued Arrays Total {Issued}, returned Arrays Total {Returned}, block size {BlockSize}");

    /// <summary>
    /// Gets or sets the <see cref="ILoggerFactory"/> used for internal logging. Set before
    /// using any other API. If not set, logging is silently discarded.
    /// </summary>
    public static ILoggerFactory? LoggerFactory
    {
        get => ChdLogger.Factory;
        set => ChdLogger.Factory = value;
    }

    /// <summary>Number of parallel decompression tasks used during verification. Can be changed at runtime.</summary>
    public static int TaskCount = 8;

    /// <summary>
    /// Validates a CHD file from a <see cref="Stream"/> using parallel decompression and hash verification.
    /// Returns a <see cref="ChdResult"/> with version, SHA1, and MD5 hashes.
    /// </summary>
    /// <param name="s">The stream to read the CHD from.</param>
    /// <param name="filename">The filename associated with the stream, used for logging.</param>
    /// <param name="deepCheck">If <c>true</c>, performs full decompression and hash verification; otherwise only validates the header.</param>
    /// <returns>A <see cref="ChdResult"/> with the verification result and hashes.</returns>
    /// <remarks>This method does not handle differential (parent/child) CHDs. Use <see cref="CheckFileWithParent(string,string)"/> for those.</remarks>
    public static ChdResult CheckFile(Stream s, string filename, bool deepCheck)
    {
        var err = CheckFile(s, filename, deepCheck, out var ver, out var sha1, out var md5);
        return new ChdResult(err, ver, sha1, md5);
    }

    /// <inheritdoc cref="CheckFile(Stream,string,bool)"/>
    public static ChdError CheckFile(Stream s, string filename, bool deepCheck, out uint? chdVersion, out byte[]? chdSha1, out byte[]? chdMd5)
    {
        chdSha1 = null;
        chdMd5 = null;
        chdVersion = null;

        if (!CheckHeader(s, out _, out var version))
            return ChdError.Chderrinvalidfile;

        LogChdVersion(Log, version, null);
        ChdError valid;
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
                        LogUnknownVersion(Log, version, null);
                        return ChdError.Chderrunsupportedversion;
                    }
            }
        }
        catch
        {
            valid = ChdError.Chderrinvaliddata;
        }

        if (valid != ChdError.Chderrnone)
        {
            LogChildChdFound(Log, null);
            return valid;
        }

        if (chd != null)
        {
            chdSha1 = chd.Sha1;
            chdMd5 = chd.Md5;
            chdVersion = version;

            if (!Util.IsAllZeroArray(chd.Parentmd5) || !Util.IsAllZeroArray(chd.Parentsha1))
            {
                LogChildChdFound(Log, null);
                return ChdError.Chderrrequiresparent;
            }

            if (!deepCheck)
                return ChdError.Chderrnone;

            if ((chd.Totalblocks * (ulong)chd.Blocksize) != chd.Totalbytes)
            {
                LogBlockSizeMismatch(Log, chd.Totalblocks * (ulong)chd.Blocksize, chd.Totalbytes, null);
            }

            var strComp = "";
            foreach (var t in chd.Compression)
            {
                strComp += $", {t}";
            }

            LogFileInfo(Log, Path.GetFileName(filename), version, strComp, null);

            ChdBlockRead.FindBlockReaders(chd);
            ChdBlockRead.FindRepeatedBlocks(chd);
            var blocksToKeep = (1024 * 1024 * 512) / (int)chd.Blocksize;
            ChdBlockRead.KeepMostRepeatedBlocks(chd, blocksToKeep);

            valid = DecompressDataParallel(s, chd);

            if (valid != ChdError.Chderrnone)
            {
                LogDecompressFailed(Log, valid, null);
                return valid;
            }

            valid = ChdMetaData.ReadMetaData(s, chd);
        }

        if (valid != ChdError.Chderrnone)
        {
            LogMetaDataFailed(Log, valid, null);
            return valid;
        }


        LogValid(Log, null);
        return ChdError.Chderrnone;
    }

    /// <summary>
    /// Fully verifies a (possibly child/differential) CHD, resolving parent
    /// references against the CHD at <paramref name="parentFilename"/> (pass null
    /// for a standalone CHD). Returns a <see cref="ChdResult"/>.
    /// </summary>
    public static ChdResult CheckFileWithParent(string filename, string parentFilename)
    {
        var err = CheckFileWithParent(filename, parentFilename, out var ver, out var sha1, out var md5);
        return new ChdResult(err, ver, sha1, md5);
    }

    /// <inheritdoc cref="CheckFileWithParent(string,string)"/>
    public static ChdError CheckFileWithParent(string filename, string parentFilename,
        out uint? chdVersion, out byte[]? chdSha1, out byte[]? chdMd5)
    {
        chdVersion = null;
        chdSha1 = null;
        chdMd5 = null;

        var err = ChdFile.Open(filename, parentFilename, out var chd);
        if (err != ChdError.Chderrnone)
            return err;

        using (chd)
        {
            chdVersion = chd!.Version;
            chdSha1 = chd.Sha1;
            chdMd5 = chd.Md5;

            var expectedSha1 = chd.RawSha1;
            var expectedMd5 = chd.Md5;
            var haveSha1 = !Util.IsAllZeroArray(expectedSha1);
            var haveMd5 = !Util.IsAllZeroArray(expectedMd5);

            using var md5Check = haveMd5 ? MD5.Create() : null;
            using var sha1Check = haveSha1 ? SHA1.Create() : null;

            var buffer = new byte[chd.HunkBytes];
            var sizetoGo = chd.TotalBytes;
            ulong offset = 0;
            while (sizetoGo > 0)
            {
                var chunk = (int)Math.Min((ulong)buffer.Length, sizetoGo);
                err = chd.Read(offset, buffer, 0, chunk);
                if (err != ChdError.Chderrnone)
                    return err;

                md5Check?.TransformBlock(buffer, 0, chunk, null, 0);
                sha1Check?.TransformBlock(buffer, 0, chunk, null, 0);
                offset += (ulong)chunk;
                sizetoGo -= (ulong)chunk;
            }

            var tmp = Array.Empty<byte>();
            md5Check?.TransformFinalBlock(tmp, 0, 0);
            sha1Check?.TransformFinalBlock(tmp, 0, 0);

            if (md5Check != null && sha1Check != null && md5Check.Hash != null && sha1Check.Hash != null && ((haveMd5 && !Util.ByteArrEquals(expectedMd5, md5Check.Hash)) || (haveSha1 && !Util.ByteArrEquals(expectedSha1, sha1Check.Hash))))
                return ChdError.Chderrdecompressionerror;

            return ChdError.Chderrnone;
        }
    }

    /// <summary>
    /// Quickly checks whether a file at the given path has a valid CHD header.
    /// </summary>
    /// <param name="path">Filesystem path to a potential CHD file.</param>
    /// <param name="version">When this method returns, contains the CHD version number (1-5) if valid.</param>
    /// <returns><c>true</c> if the file exists and has a valid CHD header.</returns>
    public static bool IsChdFile(string path, out uint version)
    {
        version = 0;
        if (!File.Exists(path))
            return false;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return CheckHeader(fs, out _, out version);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc cref="IsChdFile(string,out uint)"/>
    public static bool IsChdFile(string path)
    {
        return IsChdFile(path, out _);
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
        length = br.ReadUInt32Be();
        version = br.ReadUInt32Be();
        return HeaderLengths[version] == length;
    }


    /// <summary>Reads and decompresses all hunk data from the CHD file in parallel, validating CRC and building SHA1/MD5 checksums.</summary>
    /// <param name="file">The stream positioned at the start of the compressed data section.</param>
    /// <param name="chd">The parsed CHD header containing compression and hunk information.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    internal static ChdError DecompressDataParallel(Stream file, ChdHeader chd)
    {
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var md5Check = MD5.Create();
        var sha1Check = SHA1.Create();
        var blocksToDecompress = new BlockingCollection<int>(TaskCount * 100);
        var blocksToHash = new BlockingCollection<int>(TaskCount * 100);
        try
        {
            var errMaster = ChdError.Chderrnone;

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
                            LogVerifyingPercent(Log, (long)block * 100 / chd.Totalblocks, null);
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

                    if (errMaster == ChdError.Chderrnone)
                    {
                        errMaster = ChdError.Chderrinvalidfile;
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
                            if (err != ChdError.Chderrnone)
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

                        if (errMaster == ChdError.Chderrnone)
                        {
                            errMaster = ChdError.Chderrdecompressionerror;
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
                            var sizenext = sizetoGo > chd.Blocksize ? (int)chd.Blocksize : (int)sizetoGo;

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

                    if (errMaster == ChdError.Chderrnone)
                    {
                        errMaster = ChdError.Chderrdecompressionerror;
                    }

                    ts.Cancel();
                }
            });
            allTasks.Add(hashingThread);

            Task.WaitAll(allTasks.ToArray());


            LogVerifyingComplete(Log, null);

            arrPoolIn.ReadStats(out var issuedArraysTotal, out var returnedArraysTotal);
            LogArrayStats(Log, "In", issuedArraysTotal, returnedArraysTotal, chd.Blocksize, null);
            arrPoolOut.ReadStats(out issuedArraysTotal, out returnedArraysTotal);
            LogArrayStats(Log, "Out", issuedArraysTotal, returnedArraysTotal, chd.Blocksize, null);
            arrPoolCache.ReadStats(out issuedArraysTotal, out returnedArraysTotal);
            LogArrayStats(Log, "Cache", issuedArraysTotal, returnedArraysTotal, chd.Blocksize, null);

            if (errMaster != ChdError.Chderrnone)
                return errMaster;

            var tmp = Array.Empty<byte>();
            md5Check?.TransformFinalBlock(tmp, 0, 0);
            sha1Check?.TransformFinalBlock(tmp, 0, 0);

            // here it is now using the rawsha1 value from the header to validate the raw binary data.
            if (!Util.IsAllZeroArray(chd.Md5) && md5Check is { Hash: not null } && !Util.ByteArrEquals(chd.Md5, md5Check.Hash))
            {
                return ChdError.Chderrdecompressionerror;
            }

            if (!Util.IsAllZeroArray(chd.Rawsha1) && sha1Check is { Hash: not null } && !Util.ByteArrEquals(chd.Rawsha1, sha1Check.Hash))
            {
                return ChdError.Chderrdecompressionerror;
            }

            return ChdError.Chderrnone;
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
