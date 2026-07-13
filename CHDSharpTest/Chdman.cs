using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CHDSharp.Tests;

/// <summary>
/// Thin wrapper over References\chdman.exe used to cross-check the C# reader.
/// </summary>
internal static class Chdman
{
    public sealed class Info
    {
        public int Version;
        public ulong LogicalBytes;
        public uint HunkBytes;
        public uint TotalHunks;
        public string Compression;
        public string Sha1;      // overall SHA1 (raw + metadata)
        public string DataSha1;  // raw data SHA1 only
    }

    public sealed class Result
    {
        public int ExitCode;
        public string StdOut;
        public string StdErr;
        public string All => StdOut + "\n" + StdErr;
    }

    public static Result Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = TestPaths.ChdmanExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new Result { ExitCode = p.ExitCode, StdOut = tOut.Result, StdErr = tErr.Result };
    }

    /// <summary>Runs `chdman info -i file` and parses key fields. Returns null on failure.</summary>
    public static Info GetInfo(string file)
    {
        Result r = Run("info", "-i", file);
        if (r.ExitCode != 0)
            return null;

        string text = r.All;
        var info = new Info
        {
            Version = ParseIntField(text, @"File Version:\s*(\d+)"),
            LogicalBytes = ParseULongField(text, @"Logical size:\s*([\d,]+)"),
            HunkBytes = (uint)ParseULongField(text, @"Hunk Size:\s*([\d,]+)"),
            TotalHunks = (uint)ParseULongField(text, @"Total Hunks:\s*([\d,]+)"),
            Compression = ParseStringField(text, @"Compression:\s*(.+)"),
            Sha1 = ParseHexField(text, @"(?<!Data )SHA1:\s*([0-9a-fA-F]{40})"),
            DataSha1 = ParseHexField(text, @"Data SHA1:\s*([0-9a-fA-F]{40})"),
        };
        return info;
    }

    /// <summary>Runs `chdman verify` (optionally with a parent) and returns true on success.</summary>
    public static bool Verify(string file, string parent = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = TestPaths.ChdmanExe,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("verify");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(file);
        if (parent != null)
        {
            psi.ArgumentList.Add("-ip");
            psi.ArgumentList.Add(parent);
        }
        using var p = Process.Start(psi);
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    /// <summary>Creates a re-compressed copy with the given codec list (e.g. "cdzs" or "zstd").</summary>
    public static bool Copy(string input, string output, string compression, string parentOut = null)
    {
        var args = new System.Collections.Generic.List<string> { "copy", "-i", input, "-o", output, "-c", compression, "-f" };
        if (parentOut != null)
        {
            args.Add("-op");
            args.Add(parentOut);
        }
        var psi = new ProcessStartInfo
        {
            FileName = TestPaths.ChdmanExe,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        p.WaitForExit();
        return p.ExitCode == 0 && File.Exists(output);
    }

    /// <summary>Same as <see cref="Copy"/> but returns the full chdman result for diagnostics.</summary>
    public static Result CopyVerbose(string input, string output, string compression, string parentOut = null)
    {
        var args = new System.Collections.Generic.List<string> { "copy", "-i", input, "-o", output, "-c", compression, "-f" };
        if (parentOut != null)
        {
            args.Add("-op");
            args.Add(parentOut);
        }
        return Run(args.ToArray());
    }

    /// <summary>
    /// Extracts a byte range from a CHD via `chdman extractraw -isb -ib`.
    /// Returns the raw bytes or null on failure.
    /// </summary>
    public static byte[] ExtractRaw(string input, ulong startByte, ulong length)
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = TestPaths.ChdmanExe,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("extractraw");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(input);
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(tempFile);
            psi.ArgumentList.Add("-isb");
            psi.ArgumentList.Add(startByte.ToString());
            psi.ArgumentList.Add("-ib");
            psi.ArgumentList.Add(length.ToString());
            psi.ArgumentList.Add("-f");

            using var p = Process.Start(psi);
            p.WaitForExit();
            if (p.ExitCode != 0)
                return null;
            return File.ReadAllBytes(tempFile);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private static int ParseIntField(string text, string pattern)
    {
        Match m = Regex.Match(text, pattern);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static ulong ParseULongField(string text, string pattern)
    {
        Match m = Regex.Match(text, pattern);
        return m.Success ? ulong.Parse(m.Groups[1].Value.Replace(",", "")) : 0;
    }

    private static string ParseStringField(string text, string pattern)
    {
        Match m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string ParseHexField(string text, string pattern)
    {
        Match m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }
}
