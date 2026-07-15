using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CHDSharpTester.Services;

/// <summary>Wraps the chdman.exe command-line tool to cross-check the C# CHD reader via info, verify, and extractraw operations.</summary>
public class ChdmanWrapper : IDisposable
{
    private readonly string _chdmanPath;

    /// <summary>Initializes a new instance of the <see cref="ChdmanWrapper"/> class with the path to the chdman executable.</summary>
    /// <param name="chdmanPath">The full path to chdman.exe.</param>
    public ChdmanWrapper(string chdmanPath)
    {
        _chdmanPath = chdmanPath;
    }

    /// <summary>Gets whether the configured chdman executable exists on disk.</summary>
    public bool Available => File.Exists(_chdmanPath);

    /// <summary>Represents parsed header information returned by chdman's info command.</summary>
    public sealed class Info
    {
        /// <summary>The CHD file format version.</summary>
        internal int Version;

        /// <summary>The logical (decompressed) size of the CHD image, in bytes.</summary>
        internal ulong LogicalBytes;

        /// <summary>The size of each hunk, in bytes.</summary>
        internal uint HunkBytes;

        /// <summary>The total number of hunks in the image.</summary>
        internal uint TotalHunks;

        /// <summary>The compression codec(s) used by the CHD.</summary>
#pragma warning disable CA1051
        public string Compression = null!;
#pragma warning restore CA1051

        /// <summary>The overall SHA1 hash (raw data + metadata), or null if not present.</summary>
        internal string? Sha1;

        /// <summary>The raw data SHA1 hash, or null if not present.</summary>
        internal string? DataSha1;
    }

    /// <summary>Represents the result of a chdman process execution.</summary>
    public sealed class Result
    {
        /// <summary>The process exit code.</summary>
        internal int ExitCode;

        /// <summary>The captured standard output text.</summary>
        internal string StdOut = null!;

        /// <summary>The captured standard error text.</summary>
        internal string StdErr = null!;

        /// <summary>Gets the combined standard output and standard error text.</summary>
        internal string All => StdOut + "\n" + StdErr;
    }

    /// <summary>Runs chdman with the specified arguments and returns the captured process result.</summary>
    /// <param name="args">The arguments to pass to chdman.</param>
    /// <returns>A <see cref="Result"/> containing the exit code and output streams.</returns>
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

    /// <summary>Runs chmman info on a CHD file and parses key header fields. Returns null on failure.</summary>
    /// <param name="file">The path to the CHD file.</param>
    /// <returns>An <see cref="Info"/> instance with parsed fields, or null if chdman failed.</returns>
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

    /// <summary>Runs chmman verify on a CHD file, optionally with a parent file.</summary>
    /// <param name="file">The path to the CHD file to verify.</param>
    /// <param name="parent">An optional parent CHD file for delta-CHD verification.</param>
    /// <returns><c>true</c> if chdman exits with code 0; otherwise <c>false</c>.</returns>
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

    /// <summary>Extracts a raw byte range from a CHD file using chdman extractraw.</summary>
    /// <param name="input">The path to the CHD file.</param>
    /// <param name="startByte">The starting byte offset within the decompressed image.</param>
    /// <param name="length">The number of bytes to extract.</param>
    /// <returns>The extracted bytes, or null if chdman failed.</returns>
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
            try { File.Delete(tempFile); }
            catch
            {
                // ignored
            }
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

    /// <summary>Releases all resources used by this wrapper.</summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
