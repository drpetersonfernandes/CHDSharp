using System.Diagnostics;
using System.Security.Cryptography;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;
using Serilog;
using Serilog.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CHDSharp.Cli;

/// <summary>
/// Command-line entry point for CHDSharp. Provides file verification, random-access testing,
/// CD TOC inspection, CUE sheet generation, CHD classification, parent/child CHD validation,
/// and CHD creation (raw and CUE/BIN CD images).
/// Uses Serilog for console logging throughout.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Application entry point. Parses command-line arguments and dispatches to the
    /// appropriate operation: directory scanning, random-access test, list-based verification,
    /// parent/child test, TOC dump, CUE sheet generation, CHD classification, or CHD creation.
    /// </summary>
    /// <param name="args">Command-line arguments defining the operation and its parameters.</param>
    private static void Main(string[] args)
    {
        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(formatProvider: null, outputTemplate: "{Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Logger = serilogLogger;
        Chd.LoggerFactory = new SerilogLoggerFactory(serilogLogger);

        var sw = new Stopwatch();
        sw.Start();

        if (args.Length == 0 || args[0] is "--help" or "-h" or "-?")
        {
            serilogLogger.Information("Usage:");
            serilogLogger.Information("  CHDSharpCli <directory> [<directory> ...]      Verify all .chd files in directories");
            serilogLogger.Information("  CHDSharpCli --random <file.chd>                Random-access read test on a single CHD");
            serilogLogger.Information("  CHDSharpCli --list <listfile.txt>              Verify every .chd path listed in a text file");
            serilogLogger.Information("  CHDSharpCli --parent <child.chd> <parent.chd>  Verify a child (differential) CHD against its parent");
            serilogLogger.Information("  CHDSharpCli --toc <file.chd>                   Print table-of-contents for CD/GD-ROM CHD");
            serilogLogger.Information("  CHDSharpCli --cue <file.chd> [<binfile>]       Generate CUE sheet for CD CHD");
            serilogLogger.Information("  CHDSharpCli --classify <file.chd>              Classify CHD type (cd/dvd/hdd/gd-rom)");
            serilogLogger.Information("  CHDSharpCli --create <in.bin> <out.chd>        Create CHD from raw binary [-c zlib,zstd,lzma,none] [-hs N] [-us N] [-t N] [-ip parent.chd] [-v]");
            serilogLogger.Information("  CHDSharpCli --createcd <in.cue> <out.chd>      Create CD CHD from CUE/BIN [-c zlib,zstd,lzma,none] [-hs N] [-us N] [-t N] [-ip parent.chd] [-v]");
            serilogLogger.Information("  CHDSharpCli --copy <in.chd> <out.chd>          Re-compress a CHD [-c zlib,zstd,lzma,none] [-t N] [-ip parent.chd] [-op parent.chd] [-v]");
            return;
        }

        switch (args[0])
        {
            case "--random" when args.Length < 2:
                serilogLogger.Warning("--random requires a .chd file path");
                return;
            case "--random":
                RandomAccessTest(args[1].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--list" when args.Length < 2:
                serilogLogger.Warning("--list requires a text file of .chd paths");
                return;
            case "--list":
                VerifyList(args[1].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--parent" when args.Length < 3:
                serilogLogger.Warning("--parent requires <child.chd> <parent.chd>");
                return;
            case "--parent":
                ParentTest(args[1].Replace("\"", ""), args[2].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--toc" when args.Length < 2:
                serilogLogger.Warning("--toc requires a .chd file path");
                return;
            case "--toc":
                TocTest(args[1].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--cue" when args.Length < 2:
                serilogLogger.Warning("--cue requires a .chd file path");
                return;
            case "--cue":
                CueTest(args[1].Replace("\"", ""), args.Length >= 3 ? args[2].Replace("\"", "") : null);
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--classify" when args.Length < 2:
                serilogLogger.Warning("--classify requires a .chd file path");
                return;
            case "--classify":
                ClassifyTest(args[1].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--create" when args.Length < 3:
                serilogLogger.Warning("--create requires <input.bin> <output.chd>");
                return;
            case "--create":
                CreateRawTest(args[1].Replace("\"", ""), args[2].Replace("\"", ""), args.Skip(3).ToArray());
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--createcd" when args.Length < 3:
                serilogLogger.Warning("--createcd requires <input.cue> <output.chd>");
                return;
            case "--createcd":
                CreateCdTest(args[1].Replace("\"", ""), args[2].Replace("\"", ""), args.Skip(3).ToArray());
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--copy" when args.Length < 3:
                serilogLogger.Warning("--copy requires <input.chd> <output.chd>");
                return;
            case "--copy":
                CopyTest(args[1].Replace("\"", ""), args[2].Replace("\"", ""), args.Skip(3).ToArray());
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
        }

        foreach (var arg in args)
        {
            var sDir = arg.Replace("\"", "");
            if (!Directory.Exists(sDir))
            {
                serilogLogger.Warning("Directory not found: {Path}", sDir);
                continue;
            }

            var di = new DirectoryInfo(sDir);
            Checkdir(di);
        }

        serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Verifies a child (differential) CHD file against its parent.
    /// Opens the child with its parent, reads sample hunks, and runs <see cref="Chd.CheckFileWithParent(string, string?, IProgress{CHDSharp.Models.ChdProgress}?, System.Threading.CancellationToken)"/>.
    /// </summary>
    /// <param name="childPath">Path to the child CHD file.</param>
    /// <param name="parentPath">Path to the parent CHD file.</param>
    private static void ParentTest(string childPath, string parentPath)
    {
        var log = Log.Logger;
        log.Information("Child:  {Name}", Path.GetFileName(childPath));
        log.Information("Parent: {Name}", Path.GetFileName(parentPath));

        var err = ChdFile.Open(childPath, parentPath, out var chd);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("  Open(child, parent) => {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd != null)
            {
                log.Information("  Opened {Info}", chd.ToString());
                log.Information("  IsChild={IsChild}, Metadata entries={Count}", chd.IsChild, chd.Metadata.Count);
                foreach (var meta in chd.Metadata)
                    log.Information("    {Meta}", meta.ToString());

                var hbuf = new byte[chd.HunkBytes];
                var probes = chd.HunkCount <= 1 ? new uint[] { 0 } : new uint[] { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
                foreach (var h in probes)
                {
                    err = chd.ReadHunk(h, hbuf);
                    log.Information("  ReadHunk({Hunk}) => {Error}", h, err);
                    if (err != ChdError.Chderrnone)
                        return;
                }
            }
        }

        var result = Chd.CheckFileWithParent(childPath, parentPath);
        log.Information("  CheckFileWithParent => {Error}  (V{Version}, sha1={Sha1})", result.Error, result.Version, result.Sha1Hex);

        var noParent = ChdFile.Open(childPath, out var tmp);
        tmp?.Dispose();
        log.Information("  Open(child, no parent) => {Error}  (expected CHDERR_REQUIRES_PARENT if this is a child)", noParent);
    }

    /// <summary>
    /// Verifies all CHD files listed in a text file (one path per line).
    /// Each file is fully decompressed and verified using <see cref="Chd.CheckFile(Stream, string, bool, IProgress{CHDSharp.Models.ChdProgress}?, System.Threading.CancellationToken)"/>.
    /// </summary>
    /// <param name="listFile">Path to a text file containing one CHD path per line.</param>
    private static void VerifyList(string listFile)
    {
        var log = Log.Logger;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(listFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning("Cannot read list file {Path}: {Message}", listFile, ex.Message);
            return;
        }

        int pass = 0, fail = 0, skip = 0;
        var failures = new List<string>();

        foreach (var raw in lines)
        {
            var path = raw.Trim().Trim('"');
            if (path.Length == 0)
                continue;

            var name = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                log.Information("[SKIP] {Name}  (not found)", name);
                skip++;
                continue;
            }

            var fileSw = Stopwatch.StartNew();
            ChdResult result;
            var lastPercent = -1;
            try
            {
                using Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
                var progress = new Progress<ChdProgress>(p =>
                {
                    var pct = (int)p.Percent / 10 * 10;
                    if (pct != lastPercent)
                    {
                        lastPercent = pct;
                        log.Information("   {Pct,3}% {Name}  ({Bytes:N0} / {Total:N0} bytes, {Elapsed:N1}s)",
                            pct, name, p.BytesProcessed, p.TotalBytes, p.Elapsed.TotalSeconds);
                    }
                });
                result = Chd.CheckFile(s, name, true, progress);
            }
            catch (Exception ex)
            {
                var errCode = ex is IOException or UnauthorizedAccessException
                    ? ChdError.Chderrfilenotfound
                    : ChdError.Chderrdecompressionerror;
                result = new ChdResult(errCode, null, null, null);
                log.Warning("       exception: {Message}", ex.Message);
            }

            fileSw.Stop();

            if (result.IsSuccess)
            {
                log.Information("[PASS] V{Version} {Name}  sha1={Sha1}  ({Time:N1}s)", result.Version, name, result.Sha1Hex, fileSw.Elapsed.TotalSeconds);
                pass++;
            }
            else
            {
                log.Information("[FAIL] {Name}  {Result}  ({Time:N1}s)", name, result.Error, fileSw.Elapsed.TotalSeconds);
                failures.Add($"{name}: {result.Error.GetMessage()}");
                fail++;
            }
        }

        log.Information("");
        log.Information("==== Summary: {Pass} passed, {Fail} failed, {Skip} skipped, {Total} total ====", pass, fail, skip, pass + fail + skip);
        foreach (var f in failures)
            log.Information("  FAIL: {Failure}", f);
    }

    /// <summary>
    /// Performs a random-access read test on a single CHD file.
    /// Reads sample hunks (first, middle, last) and computes the full-image raw SHA1 and MD5
    /// to compare against the hashes stored in the CHD header.
    /// </summary>
    /// <param name="file">Path to the CHD file to test.</param>
    private static void RandomAccessTest(string file)
    {
        var log = Log.Logger;
        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd == null) return;

            log.Information("Opened {Info}", chd.ToString());
            log.Information("  IsChild={IsChild}, Metadata entries={Count}", chd.IsChild, chd.Metadata.Count);
            foreach (var meta in chd.Metadata)
                log.Information("    {Meta}", meta.ToString());

            var hbuf = new byte[chd.HunkBytes];
            var probes = chd.HunkCount <= 1
                ? new uint[] { 0 }
                : new uint[] { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
            foreach (var h in probes)
            {
                err = chd.ReadHunk(h, hbuf);
                log.Information("  ReadHunk({Hunk}) => {Error}", h, err);
                if (err != ChdError.Chderrnone)
                    return;
            }

            var expectedSha1 = chd.RawSha1;
            var expectedMd5 = chd.Md5;
            var haveSha1 = !IsAllZero(expectedSha1);
            var haveMd5 = !IsAllZero(expectedMd5);

            if (!haveSha1 && !haveMd5)
            {
                log.Information("  No raw-data hash stored in header; skipping full-image validation.");
                return;
            }

            using var sha1 = haveSha1 ? SHA1.Create() : null;
            using var md5 = haveMd5 ? MD5.Create() : null;
            var buf = new byte[chd.HunkBytes];
            var remaining = chd.TotalBytes;
            ulong offset = 0;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min((ulong)buf.Length, remaining);
                err = chd.Read(offset, buf, 0, chunk);
                if (err != ChdError.Chderrnone)
                {
                    log.Warning("  Read(offset={Offset}) => {Error}", offset, err);
                    return;
                }

                sha1?.TransformBlock(buf, 0, chunk, null, 0);
                md5?.TransformBlock(buf, 0, chunk, null, 0);
                offset += (ulong)chunk;
                remaining -= (ulong)chunk;
            }

            sha1?.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            md5?.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            if (haveSha1)
            {
                var match = sha1 is { Hash: not null } && ByteEquals(sha1.Hash, expectedSha1);
                log.Information("  Full-image raw SHA1 {Result} header raw SHA1", match ? "MATCHES" : "DIFFERS from");
                if (sha1 is { Hash: not null }) log.Information("    computed: {Hash}", ToHex(sha1.Hash));
                log.Information("    header:   {Hash}", ToHex(expectedSha1));
            }

            if (haveMd5)
            {
                var match = md5 is { Hash: not null } && ByteEquals(md5.Hash, expectedMd5);
                log.Information("  Full-image MD5 {Result} header MD5", match ? "MATCHES" : "DIFFERS from");
                if (md5 is { Hash: not null })
                    log.Information("    computed: {Hash}", ToHex(md5.Hash));
                log.Information("    header:   {Hash}", ToHex(expectedMd5));
            }
        }
    }

    /// <summary>
    /// Checks whether every byte in the specified array is zero.
    /// </summary>
    /// <param name="a">The byte array to check.</param>
    /// <returns><c>true</c> if all bytes are zero; otherwise <c>false</c>.</returns>
    private static bool IsAllZero(byte[]? a)
    {
        if (a == null) return true;

        foreach (var b in a)
            if (b != 0)
                return false;

        return true;
    }

    /// <summary>
    /// Compares two byte arrays for equality.
    /// </summary>
    /// <param name="a">The first byte array.</param>
    /// <param name="b">The second byte array.</param>
    /// <returns><c>true</c> if the arrays have identical length and content; otherwise <c>false</c>.</returns>
    private static bool ByteEquals(byte[]? a, byte[]? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;

        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;

        return true;
    }

    /// <summary>
    /// Converts a byte array to a lowercase hexadecimal string.
    /// </summary>
    /// <param name="a">The byte array to convert.</param>
    /// <returns>The lowercase hexadecimal representation of the byte array.</returns>
    private static string ToHex(byte[] a)
    {
        return Convert.ToHexString(a).ToLowerInvariant();
    }

    /// <summary>
    /// Recursively scans a directory for <c>*.chd</c> files and runs <see cref="Chd.CheckFile(Stream, string, bool, IProgress{CHDSharp.Models.ChdProgress}?, System.Threading.CancellationToken)"/>
    /// on each one found.
    /// </summary>
    /// <param name="di">The directory to scan.</param>
    private static void Checkdir(DirectoryInfo di)
    {
        FileInfo[] fi;
        try
        {
            fi = di.GetFiles("*.chd");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Logger.Warning("Access denied listing {Dir}: {Message}", di.FullName, ex.Message);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        foreach (var f in fi)
        {
            try
            {
                var lastPercent = -1;
                using Stream s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
                var progress = new Progress<ChdProgress>(p =>
                {
                    var pct = (int)p.Percent / 10 * 10;
                    if (pct != lastPercent)
                    {
                        lastPercent = pct;
                        Log.Logger.Information("   {Pct,3}% {Name}  ({Bytes:N0} / {Total:N0} bytes, {Elapsed:N1}s)",
                            pct, f.Name, p.BytesProcessed, p.TotalBytes, p.Elapsed.TotalSeconds);
                    }
                });
                Chd.CheckFile(s, f.Name, true, progress);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("[FAIL] {Name}: {Message}", f.Name, ex.Message);
            }
        }

        DirectoryInfo[] arrdi;
        try
        {
            arrdi = di.GetDirectories();
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Logger.Warning("Access denied listing subdirs of {Dir}: {Message}", di.FullName, ex.Message);
            return;
        }

        foreach (var d in arrdi)
        {
            Checkdir(d);
        }
    }

    /// <summary>
    /// Opens a CHD file and prints its table of contents (track layout) to the console.
    /// </summary>
    /// <param name="file">Path to the CHD file.</param>
    private static void TocTest(string file)
    {
        var log = Log.Logger;
        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd == null) return;

            log.Information("{Toc}", chd.ExportToc());
        }
    }

    /// <summary>
    /// Opens a CD CHD file and generates a CUE sheet, printing it to the console.
    /// </summary>
    /// <param name="file">Path to the CHD file.</param>
    /// <param name="binFileName">Optional target bin file name for the CUE sheet. Defaults to the CHD filename with a .bin extension.</param>
    private static void CueTest(string file, string? binFileName)
    {
        var log = Log.Logger;
        var err = ChdFile.Open(file, out var chd);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd == null) return;

            binFileName ??= Path.GetFileNameWithoutExtension(file) + ".bin";
            try
            {
                log.Information("{Cue}", chd.GenerateCueSheet(binFileName));
            }
            catch (InvalidOperationException ex)
            {
                log.Warning("CUE generation failed: {Message}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Opens a CHD file and classifies its media type (cd, dvd, hdd, or gd-rom).
    /// Prints the classification to the console.
    /// </summary>
    /// <param name="file">Path to the CHD file.</param>
    private static void ClassifyTest(string file)
    {
        var log = Log.Logger;
        var err = Chd.Classify(file, out var classification);
        if (err != ChdError.Chderrnone)
        {
            log.Warning("Classify failed: {Error}", err);
            return;
        }

        log.Information("{File}: {Classification}",
            Path.GetFileName(file),
            classification ?? "unknown/raw");
    }

    /// <summary>
    /// Creates a CHD from a raw binary file and verifies the result with a deep
    /// CHDSharpLib check.
    /// </summary>
    /// <param name="inputPath">Path to the raw input file.</param>
    /// <param name="outputPath">Path of the output .chd file.</param>
    /// <param name="options">Optional <c>-c</c> codec list, <c>-hs</c> hunk size and <c>-us</c> unit size arguments.</param>
    private static void CreateRawTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("--create: input file not found: {Path}", inputPath);
            return;
        }

        var hunkBytes = 4096u;
        var unitBytes = 512u;
        string? codecs = null;
        string? parentPath = null;
        var verbose = false;
        int? taskCount = null;
        if (!TryParseOptions(options, ref hunkBytes, ref unitBytes, ref codecs, ref parentPath, ref verbose, ref taskCount))
            return;

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs);
            log.Information("Creating CHD: {Input} -> {Output}  (hunk {Hunk}B, unit {Unit}B, codecs {Codecs}{Parent}{Tasks})",
                Path.GetFileName(inputPath), outputPath, hunkBytes, unitBytes,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                parentPath != null ? $", parent {Path.GetFileName(parentPath)}" : "",
                taskCount.HasValue ? $", {taskCount} tasks" : "");
            var logger = verbose ? new VerboseHunkLogger() : null;
            var encodeOptions = logger?.Options;
            if (encodeOptions == null && (taskCount.HasValue || parentPath != null))
            {
                encodeOptions = new ChdEncodeOptions();
            }

            if (encodeOptions != null)
            {
                if (taskCount.HasValue)
                {
                    encodeOptions.TaskCount = taskCount;
                }

                if (parentPath != null)
                {
                    encodeOptions.ParentPath = parentPath;
                }
            }

            ChdEncoder.EncodeRaw(inputPath, outputPath, hunkBytes, unitBytes, codecTags, encodeOptions);
            logger?.LogSummary();
            log.Information("  Created {Size:N0} bytes", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath, parentPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log.Warning("--create failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Creates a CD CHD from a CUE sheet using the CHDSharpEncoder, then verifies
    /// the file with a deep CHDSharpLib check.
    /// </summary>
    /// <param name="inputPath">Path of the .cue file.</param>
    /// <param name="outputPath">Path of the output .chd file.</param>
    /// <param name="options">Optional <c>-c</c> codec list, <c>-hs</c> hunk size and <c>-us</c> unit size arguments.</param>
    private static void CreateCdTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("--createcd: input file not found: {Path}", inputPath);
            return;
        }

        uint hunkSize = CdConstants.FramesPerHunk * CdConstants.FrameSize;
        uint unitBytes = CdConstants.FrameSize;
        string? codecs = null;
        string? parentPath = null;
        var verbose = false;
        int? taskCount = null;
        if (!TryParseOptions(options, ref hunkSize, ref unitBytes, ref codecs, ref parentPath, ref verbose, ref taskCount))
            return;

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs);
            log.Information("Creating CD CHD: {Input} -> {Output}  (hunk {Hunk}B, unit {Unit}B, codecs {Codecs}{Parent}{Tasks})",
                Path.GetFileName(inputPath), outputPath, hunkSize, unitBytes,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                parentPath != null ? $", parent {Path.GetFileName(parentPath)}" : "",
                taskCount.HasValue ? $", {taskCount} tasks" : "");
            var logger = verbose ? new VerboseHunkLogger() : null;
            var encodeOptions = logger?.Options;
            if (encodeOptions == null && (taskCount.HasValue || parentPath != null))
            {
                encodeOptions = new ChdEncodeOptions();
            }

            if (encodeOptions != null)
            {
                if (taskCount.HasValue)
                {
                    encodeOptions.TaskCount = taskCount;
                }

                if (parentPath != null)
                {
                    encodeOptions.ParentPath = parentPath;
                }
            }

            ChdEncoder.EncodeCd(inputPath, outputPath, hunkSize, unitBytes, codecTags, encodeOptions);
            logger?.LogSummary();
            log.Information("  Created ({File:N0} bytes)", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath, parentPath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            log.Warning("--createcd failed: {Message}", ex.Message);
        }
    }

    /// <summary>Parses optional <c>-c</c>/<c>-hs</c>/<c>-us</c>/<c>-t</c>/<c>-ip</c>/<c>-v</c> arguments from the CLI.</summary>
    private static bool TryParseOptions(string[] options, ref uint hunkSize, ref uint unitSize, ref string? codecs,
        ref string? parentPath, ref bool verbose, ref int? taskCount)
    {
        for (int i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "-c" or "--codecs" when i + 1 < options.Length:
                    codecs = options[++i];
                    break;
                case "-ip" or "--input-parent" when i + 1 < options.Length:
                    parentPath = options[++i];
                    break;
                case "-hs" or "--hunk-size" when i + 1 < options.Length:
                    if (!uint.TryParse(options[++i], out var hs) || hs == 0)
                    {
                        Log.Logger.Warning("Invalid hunk size: {Value}", options[i]);
                        return false;
                    }

                    hunkSize = hs;
                    break;
                case "-us" or "--unit-size" when i + 1 < options.Length:
                    if (!uint.TryParse(options[++i], out var us) || us == 0)
                    {
                        Log.Logger.Warning("Invalid unit size: {Value}", options[i]);
                        return false;
                    }

                    unitSize = us;
                    break;
                case "-t" or "--tasks" when i + 1 < options.Length:
                    if (!int.TryParse(options[++i], out var t) || t < 1 || t > 64)
                    {
                        Log.Logger.Warning("Invalid task count (1-64): {Value}", options[i]);
                        return false;
                    }

                    taskCount = t;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    Log.Logger.Warning("Unknown option: {Option}", options[i]);
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Re-compresses a CHD file into a new CHD with the target codecs (<c>--copy</c>),
    /// cloning the source's metadata, then verifies the result with a deep CHDSharpLib check.
    /// </summary>
    /// <param name="inputPath">Path of the source CHD file.</param>
    /// <param name="outputPath">Path of the output .chd file.</param>
    /// <param name="options">Optional <c>-c</c> codec list, <c>-t</c> task count, <c>-ip</c> source
    /// parent, <c>-op</c> output parent, and <c>-v</c> verbose arguments.</param>
    private static void CopyTest(string inputPath, string outputPath, string[] options)
    {
        var log = Log.Logger;
        if (!File.Exists(inputPath))
        {
            log.Warning("--copy: input file not found: {Path}", inputPath);
            return;
        }

        string? codecs = null;
        string? sourceParentPath = null;
        string? outputParentPath = null;
        var verbose = false;
        int? taskCount = null;
        for (int i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "-c" or "--codecs" when i + 1 < options.Length:
                    codecs = options[++i];
                    break;
                case "-ip" or "--input-parent" when i + 1 < options.Length:
                    sourceParentPath = options[++i];
                    break;
                case "-op" or "--output-parent" when i + 1 < options.Length:
                    outputParentPath = options[++i];
                    break;
                case "-t" or "--tasks" when i + 1 < options.Length:
                    if (!int.TryParse(options[++i], out var t) || t < 1 || t > 64)
                    {
                        log.Warning("Invalid task count (1-64): {Value}", options[i]);
                        return;
                    }

                    taskCount = t;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    log.Warning("Unknown option: {Option}", options[i]);
                    return;
            }
        }

        try
        {
            var codecTags = ChdCodecs.ParseCodecTags(codecs);
            log.Information("Copying CHD: {Input} -> {Output}  (codecs {Codecs}{SourceParent}{OutputParent}{Tasks})",
                Path.GetFileName(inputPath), outputPath,
                string.Join(",", codecTags.Select(CodecTags.ToString)),
                sourceParentPath != null ? $", source parent {Path.GetFileName(sourceParentPath)}" : "",
                outputParentPath != null ? $", output parent {Path.GetFileName(outputParentPath)}" : "",
                taskCount.HasValue ? $", {taskCount} tasks" : "");

            var encodeOptions = new ChdEncodeOptions
            {
                SourceParentPath = sourceParentPath,
                ParentPath = outputParentPath,
                TaskCount = taskCount
            };
            var logger = verbose ? new VerboseHunkLogger() : null;
            encodeOptions.HunkCompleted = logger?.Options.HunkCompleted;

            ChdEncoder.Copy(inputPath, outputPath, codecTags, encodeOptions);
            logger?.LogSummary();
            log.Information("  Created {Size:N0} bytes", new FileInfo(outputPath).Length);
            VerifyResultChd(outputPath, outputParentPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or FileNotFoundException)
        {
            log.Warning("--copy failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Logs one line per hunk (codec, sizes, compression ratio) while encoding, then a
    /// summary of the stored bytes and per-codec hunk counts.
    /// </summary>
    private sealed class VerboseHunkLogger
    {
        private long _totalRaw;
        private long _totalStored;
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        /// <summary>The <see cref="ChdEncodeOptions"/> to pass to the encoder.</summary>
        public ChdEncodeOptions Options { get; } = new();

        public VerboseHunkLogger()
        {
            Options.HunkCompleted = p =>
            {
                _totalRaw += p.RawBytes;
                _totalStored += p.StoredBytes;
                _counts[p.CodecName] = _counts.GetValueOrDefault(p.CodecName) + 1;
                Log.Logger.Information("  hunk {Hunk,6}/{Count,6}  {Codec,-5} {Raw,10} -> {Stored,10} B  ({Ratio,5:P1})",
                    p.HunkIndex, p.HunkCount, p.CodecName, p.RawBytes, p.StoredBytes, p.Ratio);
            };
        }

        public void LogSummary()
        {
            var overall = _totalRaw == 0 ? 1.0 : _totalStored / (double)_totalRaw;
            Log.Logger.Information("  Ratio: {Stored:N0} / {Raw:N0} bytes = {Overall:P1}  [{Counts}]",
                _totalStored, _totalRaw, overall,
                string.Join(", ", _counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}: {kv.Value}")));
        }
    }

    /// <summary>Runs a deep CHDSharpLib check on a created CHD file (raw + combined SHA1);
/// for differential children the parent CHD is supplied so parent references resolve.</summary>
    private static void VerifyResultChd(string path, string? parentPath = null)
    {
        if (parentPath != null)
        {
            var parentResult = Chd.CheckFileWithParent(path, parentPath);
            if (parentResult.IsSuccess)
                Log.Logger.Information("  Verified OK (V{Version}, sha1={Sha1}, parent={Parent})",
                    parentResult.Version, parentResult.Sha1Hex, Path.GetFileName(parentPath));
            else
                Log.Logger.Warning("  Verified FAILED: {Error}", parentResult.Error);
            return;
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = Chd.CheckFile(fs, Path.GetFileName(path), deepCheck: true);
        if (result.IsSuccess)
            Log.Logger.Information("  Verified OK (V{Version}, sha1={Sha1})", result.Version, result.Sha1Hex);
        else
            Log.Logger.Warning("  Verified FAILED: {Error}", result.Error);
    }
}
