namespace CHDSharpBench;

/// <summary>
/// Resolves the benchmark corpus directory (CHD files to decode/verify and, when present,
/// cue/bin pairs for CD encode benchmarks). Defaults to the repo's <c>CHDSharpTest/TestData</c>
/// folder — walk up from the working directory until the repo root is found — and can be
/// overridden with <c>--corpus &lt;dir&gt;</c>. Any CHD files in the corpus are used; codec
/// benchmarks pick files whose header declares the matching compressor.
/// </summary>
public static class Corpus
{
    private const string EnvVar = "CHDSHARP_BENCH_CORPUS";

    /// <summary>The configured corpus directory (absolute). Resolved lazily so that the
    /// BenchmarkDotNet child process (which re-enters <see cref="Corpus"/> without a fresh
    /// Configure call) still finds it: explicit value → environment variable → repo layout.</summary>
    public static string Dir
    {
        get
        {
            if (!string.IsNullOrEmpty(_dir))
                return _dir;

            var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv))
                return fromEnv;

            var resolved = ResolveDefault();
            if (!Directory.Exists(resolved))
                throw new InvalidOperationException(
                    "Corpus directory could not be resolved. Pass --corpus <dir> or run from the repo root.");

            return resolved;
        }
    }

    private static string _dir = "";

    public static void Configure(string corpusDir)
    {
        _dir = Path.GetFullPath(corpusDir);
        if (!Directory.Exists(_dir))
            throw new DirectoryNotFoundException($"Corpus directory '{_dir}' does not exist.");
        // Propagate to the BenchmarkDotNet child benchmark process, which runs as a
        // separate executable and re-enters this static class.
        Environment.SetEnvironmentVariable(EnvVar, _dir);
    }

    /// <summary>Finds <c>CHDSharpTest/TestData</c> by walking up from the current directory.</summary>
    public static string ResolveDefault()
    {
        var probe = Directory.GetCurrentDirectory();
        while (probe != null)
        {
            var candidate = Path.Combine(probe, "CHDSharpTest", "TestData");
            if (Directory.Exists(candidate))
                return candidate;

            probe = Directory.GetParent(probe)?.FullName;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "TestData");
    }

    /// <summary>All .chd files in the corpus, sorted by name.</summary>
    public static IReadOnlyList<string> ChdFiles()
    {
        return [.. Directory.EnumerateFiles(Dir, "*.chd", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Finds a corpus CHD whose V5 header declares the given compressor slot (single codec
    /// files like <c>v5_zlib.chd</c> or <c>v5_cd_cdzs.chd</c>), or null when absent.</summary>
    public static string? FindChdForCodec(uint codecTag)
    {
        foreach (var file in ChdFiles())
        {
            try
            {
                CHDSharp.Chd.ReadHeader(file, out var header);
                if (header?.Compression is { Length: > 0 } comps &&
                    comps[0] == (CHDSharp.Models.ChdCodec)codecTag &&
                    comps.Skip(1).All(c => c == CHDSharp.Models.ChdCodec.None))
                {
                    return file;
                }
            }
            catch (Exception)
            {
                // skip files that do not parse
            }
        }

        return null;
    }

    // ---- manifest.json integration (CHDSharpTest's TestData files carry a manifest that
    // marks intentionally-invalid files and child→parent links) ----

    private static readonly Dictionary<string, (string? Parent, bool Ok)> Manifest = LoadManifest();

    private static Dictionary<string, (string? Parent, bool Ok)> LoadManifest()
    {
        var result = new Dictionary<string, (string?, bool)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dir = ResolveDefault();
            var path = Path.Combine(dir, "manifest.json");
            if (!File.Exists(path))
                return result;

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("file", out var f) || f.GetString() is not { } name)
                    continue;

                var parent = e.TryGetProperty("parent", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() : null;
                var ok = e.TryGetProperty("expect", out var x) && string.Equals(x.GetString(), "ok", StringComparison.Ordinal);
                result[name] = (parent, ok);
            }
        }
        catch (Exception)
        {
            // manifest absent or unreadable: treat every file as standalone + ok
        }

        return result;
    }

    /// <summary>Parent file path per the manifest (child CHDs), or <c>null</c> for standalone files.</summary>
    public static string? ParentFor(string file)
    {
        var name = Path.GetFileName(file);
        Manifest.TryGetValue(name, out var entry);

        // Naming convention first (v5_child → v5_parent), manifest parent second (v3_child → v3_zlib).
        if (name.Contains("_child", StringComparison.Ordinal))
        {
            var sibling = name.Replace("_child", "_parent", StringComparison.Ordinal);
            if (File.Exists(Path.Combine(Dir, sibling)))
                return Path.Combine(Dir, sibling);
        }

        if (entry.Parent != null && File.Exists(Path.Combine(Dir, entry.Parent)))
            return Path.Combine(Dir, entry.Parent);

        return null;
    }

    /// <summary>True when the file is expected to verify (manifest "ok"), or when the manifest
    /// carries no entry for it (all files assumed ok).</summary>
    public static bool IsExpectedOk(string file)
    {
        var name = Path.GetFileName(file);
        return !Manifest.TryGetValue(name, out var entry) || entry.Ok;
    }
}