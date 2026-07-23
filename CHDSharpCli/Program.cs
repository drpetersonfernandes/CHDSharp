using System.Diagnostics;
using System.Security.Cryptography;
using CHDSharp.Models;
using Serilog;
using Serilog.Extensions.Logging;

namespace CHDSharp;

/// <summary>
/// Command-line entry point for CHDSharp. Provides file verification, random-access testing,
/// CD TOC inspection, CUE sheet generation, CHD classification, and parent/child CHD validation.
/// Uses Serilog for console logging throughout.
/// </summary>
internal class Program
{
    /// <summary>
    /// Application entry point. Parses command-line arguments and dispatches to the
    /// appropriate operation: directory scanning, random-access test, list-based verification,
    /// parent/child test, TOC dump, CUE sheet generation, or CHD classification.
    /// </summary>
    /// <param name="args">Command-line arguments defining the operation and its parameters.</param>
    private static void Main(string[] args)
    {
        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(formatProvider: null, outputTemplate: "{Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Chd.LoggerFactory = new SerilogLoggerFactory(serilogLogger);

        var sw = new Stopwatch();
        sw.Start();

        if (args.Length == 0)
        {
            serilogLogger.Information("Usage:");
            serilogLogger.Information("  CHDSharpCli <directory> [<directory> ...]      Verify all .chd files in directories");
            serilogLogger.Information("  CHDSharpCli --random <file.chd>                Random-access read test on a single CHD");
            serilogLogger.Information("  CHDSharpCli --list <listfile.txt>              Verify every .chd path listed in a text file");
            serilogLogger.Information("  CHDSharpCli --parent <child.chd> <parent.chd>  Verify a child (differential) CHD against its parent");
            serilogLogger.Information("  CHDSharpCli --toc <file.chd>                   Print table-of-contents for CD/GD-ROM CHD");
            serilogLogger.Information("  CHDSharpCli --cue <file.chd> [<binfile>]       Generate CUE sheet for CD CHD");
            serilogLogger.Information("  CHDSharpCli --classify <file.chd>              Classify CHD type (cd/dvd/hdd/gd-rom)");
            return;
        }

        switch (args[0])
        {
            case "--random" when args.Length < 2:
                serilogLogger.Information("--random requires a .chd file path");
                return;
            case "--random":
                RandomAccessTest(args[1].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--list" when args.Length < 2:
                serilogLogger.Information("--list requires a text file of .chd paths");
                return;
            case "--list":
                VerifyList(args[1].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--parent" when args.Length < 3:
                serilogLogger.Information("--parent requires <child.chd> <parent.chd>");
                return;
            case "--parent":
                ParentTest(args[1].Replace("\"", ""), args[2].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--toc" when args.Length < 2:
                serilogLogger.Information("--toc requires a .chd file path");
                return;
            case "--toc":
                TocTest(args[1].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--cue" when args.Length < 2:
                serilogLogger.Information("--cue requires a .chd file path");
                return;
            case "--cue":
                CueTest(args[1].Replace("\"", ""), args.Length >= 3 ? args[2].Replace("\"", "") : null);
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
            case "--classify" when args.Length < 2:
                serilogLogger.Information("--classify requires a .chd file path");
                return;
            case "--classify":
                ClassifyTest(args[1].Replace("\"", ""));
                serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
                return;
        }

        foreach (var arg in args)
        {
            var sDir = arg.Replace("\"", "");
            var di = new DirectoryInfo(sDir);
            Checkdir(di);
        }

        serilogLogger.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Verifies a child (differential) CHD file against its parent.
    /// Opens the child with its parent, reads sample hunks, and runs <see cref="Chd.CheckFileWithParent"/>.
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
            log.Information("  Open(child, parent) => {Error}", err);
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
    /// Each file is fully decompressed and verified using <see cref="Chd.CheckFile"/>.
    /// </summary>
    /// <param name="listFile">Path to a text file containing one CHD path per line.</param>
    private static void VerifyList(string listFile)
    {
        var log = Log.Logger;
        if (!File.Exists(listFile))
        {
            log.Information("List file not found: {Path}", listFile);
            return;
        }

        var lines = File.ReadAllLines(listFile);
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
            try
            {
                using Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
                result = Chd.CheckFile(s, name, true);
            }
            catch (Exception ex)
            {
                result = new ChdResult(ChdError.Chderrdecompressionerror, null, null, null);
                log.Information("       exception: {Message}", ex.Message);
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
            log.Information("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd != null)
            {
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
                        log.Information("  Read(offset={Offset}) => {Error}", offset, err);
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
                    if (md5?.Hash != null)
                        log.Information("    computed: {Hash}", ToHex(md5.Hash));
                    log.Information("    header:   {Hash}", ToHex(expectedMd5));
                }
            }
        }
    }

    /// <summary>
    /// Checks whether every byte in the specified array is zero.
    /// </summary>
    /// <param name="a">The byte array to check.</param>
    /// <returns><c>true</c> if all bytes are zero; otherwise <c>false</c>.</returns>
    private static bool IsAllZero(byte[] a)
    {
        foreach (var b in a) if (b != 0) return false;

        return true;
    }

    /// <summary>
    /// Compares two byte arrays for equality.
    /// </summary>
    /// <param name="a">The first byte array.</param>
    /// <param name="b">The second byte array.</param>
    /// <returns><c>true</c> if the arrays have identical length and content; otherwise <c>false</c>.</returns>
    private static bool ByteEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;

        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;

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
    /// Recursively scans a directory for <c>*.chd</c> files and runs <see cref="Chd.CheckFile"/>
    /// on each one found.
    /// </summary>
    /// <param name="di">The directory to scan.</param>
    private static void Checkdir(DirectoryInfo di)
    {
        var fi = di.GetFiles("*.chd");
        foreach (var f in fi)
        {
            using Stream s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
            Chd.CheckFile(s, f.Name, true);
        }

        var arrdi = di.GetDirectories();
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
            log.Information("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            Console.WriteLine(chd?.ExportToc() ?? "Unable to read CHD.");
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
            log.Information("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            if (chd == null) return;

            binFileName ??= Path.GetFileNameWithoutExtension(file) + ".bin";
            try
            {
                Console.WriteLine(chd.GenerateCueSheet(binFileName));
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("CUE generation failed: " + ex.Message);
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
        var err = Chd.Classify(file, out var classification);
        if (err != ChdError.Chderrnone)
        {
            Console.WriteLine("Classify failed: " + err);
            return;
        }

        Console.WriteLine("{0}: {1}",
            Path.GetFileName(file),
            classification ?? "unknown/raw");
    }
}
