using CHDSharpLib;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CHDSharp;

internal class Program
{
    static void Main(string[] args)
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();

        if (args.Length == 0)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  CHDSharpLib <directory> [<directory> ...]      Verify all .chd files in directories");
            Console.WriteLine("  CHDSharpLib --random <file.chd>                Random-access read test on a single CHD");
            Console.WriteLine("  CHDSharpLib --list <listfile.txt>              Verify every .chd path listed in a text file");
            Console.WriteLine("  CHDSharpLib --parent <child.chd> <parent.chd>  Verify a child (differential) CHD against its parent");
            return;
        }

        if (args[0] == "--random")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("--random requires a .chd file path");
                return;
            }
            RandomAccessTest(args[1].Replace("\"", ""));
            Console.WriteLine($"Done:  Time = {sw.Elapsed.TotalSeconds}");
            return;
        }

        if (args[0] == "--list")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("--list requires a text file of .chd paths");
                return;
            }
            VerifyList(args[1].Replace("\"", ""));
            Console.WriteLine($"Done:  Time = {sw.Elapsed.TotalSeconds}");
            return;
        }

        if (args[0] == "--parent")
        {
            if (args.Length < 3)
            {
                Console.WriteLine("--parent requires <child.chd> <parent.chd>");
                return;
            }
            ParentTest(args[1].Replace("\"", ""), args[2].Replace("\"", ""));
            Console.WriteLine($"Done:  Time = {sw.Elapsed.TotalSeconds}");
            return;
        }

        CHD.progress = fileProgress;
        CHD.fileProcessInfo = fileProcessInfo;
        CHD.consoleOut = consoleOut;

        foreach (string arg in args)
        {
            string sDir = arg.Replace("\"", "");
            DirectoryInfo di = new DirectoryInfo(sDir);
            checkdir(di, true);
        }
        Console.WriteLine($"Done:  Time = {sw.Elapsed.TotalSeconds}");
    }

    // Verifies a child (differential) CHD against its parent, exercising the
    // parent-chain resolution in CHDFile / CheckFileWithParent.
    static void ParentTest(string childPath, string parentPath)
    {
        Console.WriteLine($"Child:  {Path.GetFileName(childPath)}");
        Console.WriteLine($"Parent: {Path.GetFileName(parentPath)}");

        // 1) Open with parent and do a random-access spot check.
        chd_error err = CHDFile.Open(childPath, parentPath, out CHDFile chd);
        if (err != chd_error.CHDERR_NONE)
        {
            Console.WriteLine($"  Open(child, parent) => {err}");
            return;
        }
        using (chd)
        {
            Console.WriteLine($"  Opened V{chd.Version}: {chd.TotalBytes} bytes, {chd.HunkCount} hunks x {chd.HunkBytes}");
            byte[] hbuf = new byte[chd.HunkBytes];
            uint[] probes = chd.HunkCount <= 1 ? new uint[] { 0 } : new uint[] { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
            foreach (uint h in probes)
            {
                err = chd.ReadHunk(h, hbuf);
                Console.WriteLine($"  ReadHunk({h}) => {err}");
                if (err != chd_error.CHDERR_NONE)
                    return;
            }
        }

        // 2) Full verify against raw hash via CheckFileWithParent.
        err = CHD.CheckFileWithParent(childPath, parentPath, out uint? ver, out byte[] sha1, out _);
        Console.WriteLine($"  CheckFileWithParent => {err}  (V{ver}, sha1={(sha1 != null ? ToHex(sha1) : "(none)")})");

        // 3) Negative test: opening the child WITHOUT a parent should be rejected.
        chd_error noParent = CHDFile.Open(childPath, out CHDFile tmp);
        tmp?.Dispose();
        Console.WriteLine($"  Open(child, no parent) => {noParent}  (expected CHDERR_REQUIRES_PARENT if this is a child)");
    }

    // Verifies each .chd path listed (one per line) in a text file, using the
    // full CheckFile path (decompress every hunk + per-block CRC + raw MD5/SHA1
    // + metadata SHA1). Prints a PASS/FAIL summary line per file plus the SHA1
    // so results can be compared against `chdman verify`.
    static void VerifyList(string listFile)
    {
        if (!File.Exists(listFile))
        {
            Console.WriteLine($"List file not found: {listFile}");
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
                Console.WriteLine($"[SKIP] {name}  (not found)");
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
                Console.WriteLine($"       exception: {ex.Message}");
            }
            fileSw.Stop();

            string sha1Str = sha1 != null ? ToHex(sha1) : "(none)";
            if (result == chd_error.CHDERR_NONE)
            {
                Console.WriteLine($"[PASS] V{version} {name}  sha1={sha1Str}  ({fileSw.Elapsed.TotalSeconds:N1}s)");
                pass++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {name}  {result}  ({fileSw.Elapsed.TotalSeconds:N1}s)");
                failures.Add($"{name}: {result}");
                fail++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"==== Summary: {pass} passed, {fail} failed, {skip} skipped, {pass + fail + skip} total ====");
        foreach (string f in failures)
            Console.WriteLine($"  FAIL: {f}");
    }

    // Phase 3.4 - random access self test.
    // Opens a CHD via the random-access API, reads the whole image back through
    // CHDFile.Read() (which decompresses hunks on demand), and validates the
    // result against the SHA1 stored in the header.
    static void RandomAccessTest(string file)
    {
        chd_error err = CHDFile.Open(file, out CHDFile chd);
        if (err != chd_error.CHDERR_NONE)
        {
            Console.WriteLine($"Open failed: {err}");
            return;
        }

        using (chd)
        {
            Console.WriteLine($"Opened V{chd.Version}: {chd.TotalBytes} bytes, {chd.HunkCount} hunks x {chd.HunkBytes} bytes");

            // 1) Spot-check a few individual hunks decompress without error.
            byte[] hbuf = new byte[chd.HunkBytes];
            uint[] probes = chd.HunkCount <= 1
                ? new uint[] { 0 }
                : new uint[] { 0, chd.HunkCount / 2, chd.HunkCount - 1 };
            foreach (uint h in probes)
            {
                err = chd.ReadHunk(h, hbuf);
                Console.WriteLine($"  ReadHunk({h}) => {err}");
                if (err != chd_error.CHDERR_NONE)
                    return;
            }

            // 2) Read the whole image through Read() and validate against the
            //    raw-data hash (RawSHA1 for V3/V4/V5, or MD5 for V1/V2/V3). Note:
            //    the combined SHA1 (chd.SHA1) also covers metadata, so a full
            //    sequential read of the raw image validates against RawSHA1/MD5.
            byte[] expectedSha1 = chd.RawSHA1;
            byte[] expectedMd5 = chd.MD5;
            bool haveSha1 = expectedSha1 != null && !IsAllZero(expectedSha1);
            bool haveMd5 = expectedMd5 != null && !IsAllZero(expectedMd5);

            if (!haveSha1 && !haveMd5)
            {
                Console.WriteLine("  No raw-data hash stored in header; skipping full-image validation.");
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
                    Console.WriteLine($"  Read(offset={offset}) => {err}");
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
                Console.WriteLine($"  Full-image raw SHA1 {(match ? "MATCHES" : "DIFFERS from")} header raw SHA1");
                Console.WriteLine($"    computed: {ToHex(sha1.Hash)}");
                Console.WriteLine($"    header:   {ToHex(expectedSha1)}");
            }
            if (haveMd5)
            {
                bool match = ByteEquals(md5.Hash, expectedMd5);
                Console.WriteLine($"  Full-image MD5 {(match ? "MATCHES" : "DIFFERS from")} header MD5");
                Console.WriteLine($"    computed: {ToHex(md5.Hash)}");
                Console.WriteLine($"    header:   {ToHex(expectedMd5)}");
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

    private static void consoleOut(string message)
    {
        Console.WriteLine(message);
    }

    private static void fileProcessInfo(string message)
    {
        Console.WriteLine(message);
    }

    private static void fileProgress(string message)
    {
        Console.Write(message + "\r");
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
