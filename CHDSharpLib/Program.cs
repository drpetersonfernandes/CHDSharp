using CHDSharpLib;
using Serilog;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CHDSharp;

internal class Program
{
    static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Stopwatch sw = new Stopwatch();
        sw.Start();

        if (args.Length == 0)
        {
            Log.Information("Usage:");
            Log.Information("  CHDSharpLib <directory> [<directory> ...]      Verify all .chd files in directories");
            Log.Information("  CHDSharpLib --random <file.chd>                Random-access read test on a single CHD");
            Log.Information("  CHDSharpLib --list <listfile.txt>              Verify every .chd path listed in a text file");
            Log.Information("  CHDSharpLib --parent <child.chd> <parent.chd>  Verify a child (differential) CHD against its parent");
            return;
        }

        if (args[0] == "--random")
        {
            if (args.Length < 2)
            {
                Log.Information("--random requires a .chd file path");
                return;
            }
            RandomAccessTest(args[1].Replace("\"", ""));
            Log.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
            return;
        }

        if (args[0] == "--list")
        {
            if (args.Length < 2)
            {
                Log.Information("--list requires a text file of .chd paths");
                return;
            }
            VerifyList(args[1].Replace("\"", ""));
            Log.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
            return;
        }

        if (args[0] == "--parent")
        {
            if (args.Length < 3)
            {
                Log.Information("--parent requires <child.chd> <parent.chd>");
                return;
            }
            ParentTest(args[1].Replace("\"", ""), args[2].Replace("\"", ""));
            Log.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
            return;
        }

        foreach (string arg in args)
        {
            string sDir = arg.Replace("\"", "");
            DirectoryInfo di = new DirectoryInfo(sDir);
            checkdir(di, true);
        }
        Log.Information("Done:  Time = {Time}", sw.Elapsed.TotalSeconds);
    }

    static void ParentTest(string childPath, string parentPath)
    {
        Log.Information("Child:  {Name}", Path.GetFileName(childPath));
        Log.Information("Parent: {Name}", Path.GetFileName(parentPath));

        chd_error err = CHDFile.Open(childPath, parentPath, out CHDFile chd);
        if (err != chd_error.CHDERR_NONE)
        {
            Log.Information("  Open(child, parent) => {Error}", err);
            return;
        }
        using (chd)
        {
            Log.Information("  Opened V{Version}: {TotalBytes} bytes, {HunkCount} hunks x {HunkBytes}", chd.Version, chd.TotalBytes, chd.HunkCount, chd.HunkBytes);
            byte[] hbuf = new byte[chd.HunkBytes];
            uint[] probes = chd.HunkCount <= 1 ? new uint[] { 0 } : new uint[] { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
            foreach (uint h in probes)
            {
                err = chd.ReadHunk(h, hbuf);
                Log.Information("  ReadHunk({Hunk}) => {Error}", h, err);
                if (err != chd_error.CHDERR_NONE)
                    return;
            }
        }

        err = CHD.CheckFileWithParent(childPath, parentPath, out uint? ver, out byte[] sha1, out _);
        Log.Information("  CheckFileWithParent => {Error}  (V{Version}, sha1={Sha1})", err, ver, sha1 != null ? ToHex(sha1) : "(none)");

        chd_error noParent = CHDFile.Open(childPath, out CHDFile tmp);
        tmp?.Dispose();
        Log.Information("  Open(child, no parent) => {Error}  (expected CHDERR_REQUIRES_PARENT if this is a child)", noParent);
    }

    static void VerifyList(string listFile)
    {
        if (!File.Exists(listFile))
        {
            Log.Information("List file not found: {Path}", listFile);
            return;
        }

        string[] lines = File.ReadAllLines(listFile);
        int pass = 0, fail = 0, skip = 0;
        var failures = new List<string>();

        foreach (string raw in lines)
        {
            string path = raw.Trim().Trim('"');
            if (path.Length == 0)
                continue;

            string name = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                Log.Information("[SKIP] {Name}  (not found)", name);
                skip++;
                continue;
            }

            var fileSw = Stopwatch.StartNew();
            chd_error result;
            uint? version = null;
            byte[] sha1 = null;
            try
            {
                using Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
                result = CHD.CheckFile(s, name, true, out version, out sha1, out _);
            }
            catch (Exception ex)
            {
                result = chd_error.CHDERR_DECOMPRESSION_ERROR;
                Log.Information("       exception: {Message}", ex.Message);
            }
            fileSw.Stop();

            string sha1Str = sha1 != null ? ToHex(sha1) : "(none)";
            if (result == chd_error.CHDERR_NONE)
            {
                Log.Information("[PASS] V{Version} {Name}  sha1={Sha1}  ({Time:N1}s)", version, name, sha1Str, fileSw.Elapsed.TotalSeconds);
                pass++;
            }
            else
            {
                Log.Information("[FAIL] {Name}  {Result}  ({Time:N1}s)", name, result, fileSw.Elapsed.TotalSeconds);
                failures.Add($"{name}: {result}");
                fail++;
            }
        }

        Log.Information("");
        Log.Information("==== Summary: {Pass} passed, {Fail} failed, {Skip} skipped, {Total} total ====", pass, fail, skip, pass + fail + skip);
        foreach (string f in failures)
            Log.Information("  FAIL: {Failure}", f);
    }

    static void RandomAccessTest(string file)
    {
        chd_error err = CHDFile.Open(file, out CHDFile chd);
        if (err != chd_error.CHDERR_NONE)
        {
            Log.Information("Open failed: {Error}", err);
            return;
        }

        using (chd)
        {
            Log.Information("Opened V{Version}: {TotalBytes} bytes, {HunkCount} hunks x {HunkBytes} bytes", chd.Version, chd.TotalBytes, chd.HunkCount, chd.HunkBytes);

            byte[] hbuf = new byte[chd.HunkBytes];
            uint[] probes = chd.HunkCount <= 1
                ? new uint[] { 0 }
                : new uint[] { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
            foreach (uint h in probes)
            {
                err = chd.ReadHunk(h, hbuf);
                Log.Information("  ReadHunk({Hunk}) => {Error}", h, err);
                if (err != chd_error.CHDERR_NONE)
                    return;
            }

            byte[] expectedSha1 = chd.RawSHA1;
            byte[] expectedMd5 = chd.MD5;
            bool haveSha1 = expectedSha1 != null && !IsAllZero(expectedSha1);
            bool haveMd5 = expectedMd5 != null && !IsAllZero(expectedMd5);

            if (!haveSha1 && !haveMd5)
            {
                Log.Information("  No raw-data hash stored in header; skipping full-image validation.");
                return;
            }

            using SHA1 sha1 = haveSha1 ? SHA1.Create() : null;
            using MD5 md5 = haveMd5 ? MD5.Create() : null;
            byte[] buf = new byte[chd.HunkBytes];
            ulong remaining = chd.TotalBytes;
            ulong offset = 0;
            while (remaining > 0)
            {
                int chunk = (int)Math.Min((ulong)buf.Length, remaining);
                err = chd.Read(offset, buf, 0, chunk);
                if (err != chd_error.CHDERR_NONE)
                {
                    Log.Information("  Read(offset={Offset}) => {Error}", offset, err);
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
                bool match = ByteEquals(sha1.Hash, expectedSha1);
                Log.Information("  Full-image raw SHA1 {Result} header raw SHA1", match ? "MATCHES" : "DIFFERS from");
                Log.Information("    computed: {Hash}", ToHex(sha1.Hash));
                Log.Information("    header:   {Hash}", ToHex(expectedSha1));
            }
            if (haveMd5)
            {
                bool match = ByteEquals(md5.Hash, expectedMd5);
                Log.Information("  Full-image MD5 {Result} header MD5", match ? "MATCHES" : "DIFFERS from");
                Log.Information("    computed: {Hash}", ToHex(md5.Hash));
                Log.Information("    header:   {Hash}", ToHex(expectedMd5));
            }
        }
    }

    private static bool IsAllZero(byte[] a)
    {
        foreach (byte b in a) if (b != 0) return false;
        return true;
    }

    private static bool ByteEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static string ToHex(byte[] a)
    {
        if (a == null) return "(null)";
        return Convert.ToHexString(a).ToLowerInvariant();
    }

    static void checkdir(DirectoryInfo di, bool verify)
    {
        FileInfo[] fi = di.GetFiles("*.chd");
        foreach (FileInfo f in fi)
        {
            using (Stream s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096))
            {
                CHD.CheckFile(s, f.Name, true, out uint? chdVersion, out byte[] chdSHA1, out byte[] chdMD5);
            }
        }

        DirectoryInfo[] arrdi = di.GetDirectories();
        foreach (DirectoryInfo d in arrdi)
        {
            checkdir(d, verify);
        }
    }
}
