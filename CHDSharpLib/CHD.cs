using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using CHDSharp.Models;
using CHDSharp.Utils;
using Microsoft.Extensions.Logging;

namespace CHDSharp;

/// <summary>
/// Provides static methods for validating, inspecting, and verifying CHD (Compressed Hunks of Data)
/// files using parallel decompression. Supports CHD format versions 1-5 and all MAME codecs
/// (zlib, LZMA, Huffman, FLAC, Zstd, AVHuff and the CD variants).
/// </summary>
/// <remarks>
/// Use <see cref="CheckFile(Stream,string,bool)"/> for full (parallel) verification of a standalone CHD,
/// <see cref="CheckFileWithParent(string,string)"/> for child (differential) CHDs, and
/// <see cref="IsChdFile(string)"/> / <see cref="CheckHeader"/> for fast header-only checks.
/// For random access to decompressed data use <see cref="ChdFile"/> instead.
/// </remarks>
/// <example>
/// <code>
/// using Stream s = File.OpenRead("game.chd");
/// ChdResult result = Chd.CheckFile(s, "game.chd", deepCheck: true);
/// if (result.IsSuccess)
///     Console.WriteLine($"V{result.Version} SHA1={result.Sha1Hex}");
/// else
///     Console.WriteLine(result.Error.GetMessage());
/// </code>
/// </example>
public static class Chd
{
    private static readonly ILogger Log = ChdLogger.GetLogger(nameof(Chd));

    private static readonly Action<ILogger, uint, Exception?> LogChdVersion =
        LoggerMessage.Define<uint>(LogLevel.Information, new EventId(1), "CHD Version {Version}");

    private static readonly Action<ILogger, uint, Exception?> LogUnknownVersion =
        LoggerMessage.Define<uint>(LogLevel.Warning, new EventId(2), "Unknown version {Version}");

    private static readonly Action<ILogger, ChdError, Exception?> LogHeaderReadFailed =
        LoggerMessage.Define<ChdError>(LogLevel.Warning, new EventId(12), "Header/map read failed: {Error}");

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
    /// Gets or sets the <see cref="ILoggerFactory"/> used for internal logging.
    /// Can be set (or changed) at any time; loggers resolve the factory lazily.
    /// If not set, logging is silently discarded.
    /// </summary>
    public static ILoggerFactory? LoggerFactory
    {
        get => ChdLogger.Factory;
        set => ChdLogger.Factory = value;
    }

    private static int _taskCount = 8;

    /// <summary>
    /// Number of parallel decompression tasks used during verification (default 8).
    /// Must be between 1 and 64. Changing it affects subsequent <see cref="CheckFile(Stream,string,bool)"/>
    /// calls; verifications already in progress keep the value they started with.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less than 1 or greater than 64.</exception>
    public static int TaskCount
    {
        get => _taskCount;
        set
        {
            if (value is < 1 or > 64)
                throw new ArgumentOutOfRangeException(nameof(value), value, "TaskCount must be between 1 and 64.");

            _taskCount = value;
        }
    }

    /// <summary>
    /// Validates a CHD file from a <see cref="Stream"/> using parallel decompression and hash verification.
    /// Returns a <see cref="ChdResult"/> with version, SHA1, and MD5 hashes.
    /// </summary>
    /// <param name="s">A readable, seekable stream positioned at the start of the CHD file.</param>
    /// <param name="filename">The filename associated with the stream, used only for logging.</param>
    /// <param name="deepCheck">If <c>true</c>, performs full decompression of every hunk plus SHA1/MD5 hash
    /// verification (using up to <see cref="TaskCount"/> parallel workers); if <c>false</c>, only the header is validated.</param>
    /// <returns>A <see cref="ChdResult"/> with the verification result, CHD version, and header hashes.</returns>
    /// <remarks>
    /// This method does not handle differential (parent/child) CHDs; it returns
    /// <see cref="ChdError.Chderrrequiresparent"/> for those. Use <see cref="CheckFileWithParent(string,string)"/> instead.
    /// </remarks>
    public static ChdResult CheckFile(Stream s, string filename, bool deepCheck)
    {
        var err = CheckFile(s, filename, deepCheck, out var ver, out var sha1, out var md5);
        return new ChdResult(err, ver, sha1, md5);
    }

    /// <inheritdoc cref="CheckFile(Stream,string,bool)"/>
    /// <param name="s">A readable, seekable stream positioned at the start of the CHD file.</param>
    /// <param name="filename">The filename associated with the stream, used only for logging.</param>
    /// <param name="deepCheck">If <c>true</c>, performs full decompression of every hunk plus SHA1/MD5 hash
    /// verification (using up to <see cref="TaskCount"/> parallel workers); if <c>false</c>, only the header is validated.</param>
    /// <param name="chdVersion">When this method returns, contains the CHD version (1-5), or <c>null</c> if the header was invalid.</param>
    /// <param name="chdSha1">When this method returns, contains the SHA1 hash from the header, or <c>null</c> if not available (V1/V2).</param>
    /// <param name="chdMd5">When this method returns, contains the MD5 hash from the header, or <c>null</c> if not available (V4/V5).</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code describing the failure.</returns>
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
        catch (Exception)
        {
            valid = ChdError.Chderrinvaliddata;
        }

        if (valid != ChdError.Chderrnone)
        {
            LogHeaderReadFailed(Log, valid, null);
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
    /// Fully verifies a (possibly child/differential) CHD by decompressing the whole image and
    /// comparing the computed hashes against the values stored in the header, resolving parent
    /// references against the CHD at <paramref name="parentFilename"/>.
    /// </summary>
    /// <param name="filename">Path to the CHD file to verify.</param>
    /// <param name="parentFilename">Path to the parent CHD, or <c>null</c>/empty for a standalone CHD.</param>
    /// <returns>A <see cref="ChdResult"/> with the verification result, CHD version, and header hashes.</returns>
    /// <remarks>
    /// Unlike <see cref="CheckFile(Stream,string,bool)"/>, this method is single-threaded but supports
    /// parent/child CHD chains. Returns <see cref="ChdError.Chderrinvalidparent"/> when the supplied
    /// parent does not match, and <see cref="ChdError.Chderrrequiresparent"/> when the CHD is a child
    /// and no parent was supplied.
    /// </remarks>
    public static ChdResult CheckFileWithParent(string filename, string? parentFilename)
    {
        var err = CheckFileWithParent(filename, parentFilename, out var ver, out var sha1, out var md5);
        return new ChdResult(err, ver, sha1, md5);
    }

    /// <inheritdoc cref="CheckFileWithParent(string,string)"/>
    /// <param name="filename">Path to the CHD file to verify.</param>
    /// <param name="parentFilename">Path to the parent CHD, or <c>null</c>/empty for a standalone CHD.</param>
    /// <param name="chdVersion">When this method returns, contains the CHD version (1-5), or <c>null</c> if the file could not be opened.</param>
    /// <param name="chdSha1">When this method returns, contains the SHA1 hash from the header, or <c>null</c> if not available.</param>
    /// <param name="chdMd5">When this method returns, contains the MD5 hash from the header, or <c>null</c> if not available.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code describing the failure.</returns>
    public static ChdError CheckFileWithParent(string filename, string? parentFilename,
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

            var md5Mismatch = haveMd5 && md5Check?.Hash != null && !Util.ByteArrEquals(expectedMd5, md5Check.Hash);
            var sha1Mismatch = haveSha1 && sha1Check?.Hash != null && !Util.ByteArrEquals(expectedSha1, sha1Check.Hash);
            if (md5Mismatch || sha1Mismatch)
            {
                return ChdError.Chderrdecompressionerror;
            }

            return ChdError.Chderrnone;
        }
    }

    /// <summary>
    /// Quickly checks whether a file at the given path has a valid CHD header.
    /// Only the 16-byte header signature is read; no decompression is performed.
    /// </summary>
    /// <param name="path">Filesystem path to a potential CHD file.</param>
    /// <param name="version">When this method returns, contains the CHD version number (1-5) if valid; otherwise 0.</param>
    /// <returns><c>true</c> if the file exists, is readable, and has a valid CHD header; otherwise <c>false</c>. Never throws.</returns>
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

    /// <summary>Quickly classify a CHD file as CD, DVD, HDD, GD-ROM, or unknown without full decompression.</summary>
    /// <param name="filename">Path to the CHD file.</param>
    /// <param name="classification">When this method returns, contains "cd", "dvd", "hdd", "gd-rom", or <c>null</c> for unknown types.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    public static ChdError Classify(string filename, out string? classification)
    {
        classification = null;
        var err = ChdFile.Open(filename, out var chd);
        if (err != ChdError.Chderrnone || chd == null) return err;

        using (chd)
        {
            if (chd.IsGdRom)
            {
                classification = "gd-rom";
            }
            else if (chd.IsCd)
            {
                classification = "cd";
            }
            else if (chd.IsDvd)
            {
                classification = "dvd";
            }
            else if (chd.IsHdd)
            {
                classification = "hdd";
            }
            else
            {
                classification = null;
            }
        }
        return ChdError.Chderrnone;
    }

    private static readonly uint[] HeaderLengths = [0, 76, 80, 120, 108, 124];

    private static readonly byte[] Id = "MComprHD"u8.ToArray();

    /// <summary>Reads and validates the CHD file header signature ("MComprHD") and version.</summary>
    /// <param name="file">The stream, positioned at the start of the CHD file (byte 0).</param>
    /// <param name="length">When this method returns, contains the header length in bytes declared by the file; 0 if invalid.</param>
    /// <param name="version">When this method returns, contains the CHD version number (1-5); 0 if invalid.</param>
    /// <returns><c>true</c> if the signature is valid, the version is recognized (1-5), and the declared
    /// header length matches that version; otherwise <c>false</c>.</returns>
    /// <remarks>The stream is advanced past the 16-byte signature. Unknown versions and truncated streams return <c>false</c> rather than throwing.</remarks>
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
        try
        {
            length = br.ReadUInt32Be();
            version = br.ReadUInt32Be();
        }
        catch (EndOfStreamException)
        {
            length = 0;
            version = 0;
            return false;
        }

        if (version == 0 || version >= HeaderLengths.Length)
            return false;

        return HeaderLengths[version] == length;
    }


    /// <summary>Reads and decompresses all hunk data from the CHD file in parallel, validating CRC and building SHA1/MD5 checksums.</summary>
    /// <param name="file">The stream positioned at the start of the compressed data section.</param>
    /// <param name="chd">The parsed CHD header containing compression and hunk information.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    private static ChdError DecompressDataParallel(Stream file, ChdHeader chd)
    {
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var taskCount = TaskCount; // snapshot so a concurrent change cannot desync sentinels vs workers
        var md5Check = MD5.Create();
        var sha1Check = SHA1.Create();
        var blocksToDecompress = new BlockingCollection<int>(taskCount * 100);
        var blocksToHash = new BlockingCollection<int>(taskCount * 100);
        var allTasks = new List<Task>();
        var ts = new CancellationTokenSource();
        try
        {
            var errMaster = ChdError.Chderrnone;

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
                    for (var i = 0; i < taskCount; i++)
                        blocksToDecompress.Add(-1, ct);
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
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            allTasks.Add(producerThread);

            for (var i = 0; i < taskCount; i++)
            {
                var decompressionThread = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        var codec = new ChdCodecState();
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
                                arrPoolOut.Return(mapEntry.BuffOut);
                                mapEntry.BuffOut = null!;
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
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

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
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
            ts.Cancel();
            Task.WaitAll(allTasks.ToArray());
            ts.Dispose();
            blocksToDecompress.Dispose();
            blocksToHash.Dispose();
            md5Check?.Dispose();
            sha1Check?.Dispose();
        }
    }
}
