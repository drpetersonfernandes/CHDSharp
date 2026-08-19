using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CHDSharpBattleTest;

/// <summary>
/// Thin wrapper around chdman.exe (MAME): runs commands, captures output, and parses
/// <c>chdman info</c> output into strongly typed fields for cross-checking against
/// CHDSharpLib's <see cref="CHDSharp.Chd.ReadHeader"/>.
/// </summary>
public sealed class ChdmanRunner
{
    public string ExePath { get; }

    public ChdmanRunner(string exePath)
    {
        ExePath = exePath;
    }

    public sealed record RunResult(int ExitCode, string Stdout, string Stderr)
    {
        public string Combined => Stdout + Stderr;
    }

    /// <summary>Runs <c>chdman &lt;command&gt; [args...]</c> and captures stdout/stderr.</summary>
    public RunResult Run(string command, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(command);
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {ExePath}");
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(300_000))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException($"chdman {command} timed out after 300s");
        }

        return new RunResult(p.ExitCode, tOut.Result, tErr.Result);
    }

    /// <summary>The version banner line, e.g. "chdman - MAME Compressed Hunks of Data (CHD) manager 0.289 (mame0289)".</summary>
    public string VersionBanner()
    {
        var r = Run("help", "createraw");
        var first = r.Combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "(unknown chdman version)" : first;
    }

    /// <summary>Parsed <c>chdman info</c> output (the fields that matter for cross-checks).</summary>
    public sealed record ChdmanInfo(
        int Version,
        ulong LogicalBytes,
        uint HunkBytes,
        uint TotalHunks,
        uint UnitBytes,
        uint TotalUnits,
        string Compression,
        long ChdSize,
        string? Sha1,
        string? DataSha1,
        string? Md5,
        string? ParentSha1,
        string? ParentMd5);

    /// <summary>Runs <c>chdman info</c> and parses the output; returns null when the file cannot be read.</summary>
    public ChdmanInfo? Info(string chdPath)
    {
        var r = Run("info", "-i", chdPath);
        if (r.ExitCode != 0)
            return null;

        return ParseInfo(r.Combined);
    }

    /// <summary>Parses the text output of <c>chdman info</c>.</summary>
    public static ChdmanInfo? ParseInfo(string output)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var m = Regex.Match(line, @"^([A-Za-z][A-Za-z ]*?):\s+(.+?)\s*$");
            if (!m.Success)
                continue;

            fields[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
        }

        if (!fields.TryGetValue("File Version", out var versionText) || !int.TryParse(versionText, out var version))
            return null;

        static ulong ParseNum(string text)
        {
            text = text.Replace(",", "", StringComparison.Ordinal).Split(' ')[0];
            return ulong.Parse(text, CultureInfo.InvariantCulture);
        }

        return new ChdmanInfo(
            Version: version,
            LogicalBytes: fields.TryGetValue("Logical size", out var ls) ? ParseNum(ls) : 0,
            HunkBytes: fields.TryGetValue("Hunk Size", out var hs) ? (uint)ParseNum(hs) : 0,
            TotalHunks: fields.TryGetValue("Total Hunks", out var th) ? (uint)ParseNum(th) : 0,
            UnitBytes: fields.TryGetValue("Unit Size", out var us) ? (uint)ParseNum(us) : 0,
            TotalUnits: fields.TryGetValue("Total Units", out var tu) ? (uint)ParseNum(tu) : 0,
            Compression: fields.TryGetValue("Compression", out var c) ? c : "none",
            ChdSize: fields.TryGetValue("CHD size", out var cs) ? (long)ParseNum(cs) : 0,
            Sha1: NormalizeHash(fields, "SHA1"),
            DataSha1: NormalizeHash(fields, "Data SHA1"),
            Md5: NormalizeHash(fields, "MD5"),
            ParentSha1: NormalizeHash(fields, "Parent SHA1"),
            ParentMd5: NormalizeHash(fields, "Parent MD5"));
    }

    private static string? NormalizeHash(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var v))
            return null;

        v = v.Trim();
        return string.IsNullOrEmpty(v) || v.Equals("(none)", StringComparison.OrdinalIgnoreCase) ? null : v.ToLowerInvariant();
    }
}