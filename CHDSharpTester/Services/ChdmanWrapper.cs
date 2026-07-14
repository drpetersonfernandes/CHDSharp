using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CHDSharpTester.Services;

public class ChdmanWrapper : IDisposable
{
    private readonly string _chdmanPath;

    public ChdmanWrapper(string chdmanPath)
    {
        _chdmanPath = chdmanPath;
    }

    public bool Available => File.Exists(_chdmanPath);

    public sealed class Info
    {
        public int Version;
        public ulong LogicalBytes;
        public uint HunkBytes;
        public uint TotalHunks;
        public string Compression = null!;
        public string? Sha1;
        public string? DataSha1;
    }

    public sealed class Result
    {
        public int ExitCode;
        public string StdOut = null!;
        public string StdErr = null!;
        public string All => StdOut + "\n" + StdErr;
    }

    public Result Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _chdmanPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new Result { ExitCode = p.ExitCode, StdOut = tOut.Result, StdErr = tErr.Result };
    }

    public Info? GetInfo(string file)
    {
        var r = Run("info", "-i", file);
        if (r.ExitCode != 0)
            return null;

        var text = r.All;
        return new Info
        {
            Version = ParseIntField(text, @"File Version:\s*(\d+)"),
            LogicalBytes = ParseULongField(text, @"Logical size:\s*([\d,]+)"),
            HunkBytes = (uint)ParseULongField(text, @"Hunk Size:\s*([\d,]+)"),
            TotalHunks = (uint)ParseULongField(text, @"Total Hunks:\s*([\d,]+)"),
            Compression = ParseStringField(text, @"Compression:\s*(.+)")!,
            Sha1 = ParseHexField(text, @"(?<!Data )SHA1:\s*([0-9a-fA-F]{40})"),
            DataSha1 = ParseHexField(text, @"Data SHA1:\s*([0-9a-fA-F]{40})")
        };
    }

    public bool Verify(string file, string? parent = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _chdmanPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("verify");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(file);
        if (parent != null)
        {
            psi.ArgumentList.Add("-ip");
            psi.ArgumentList.Add(parent);
        }

        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    public byte[]? ExtractRaw(string input, ulong startByte, ulong length)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _chdmanPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("extractraw");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(input);
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(tempFile);
            psi.ArgumentList.Add("-isb");
            psi.ArgumentList.Add(startByte.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-ib");
            psi.ArgumentList.Add(length.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-f");

            using var p = Process.Start(psi)!;
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
        var m = Regex.Match(text, pattern);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static ulong ParseULongField(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? ulong.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) : 0;
    }

    private static string? ParseStringField(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? ParseHexField(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    public void Dispose() { }
}
