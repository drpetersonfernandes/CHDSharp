using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates CueParser against the authoritative pipeline: chdman.exe (parse_cue)
/// writes CHT2 metadata into a CD CHD; we compare the metadata produced from
/// chdman's own TOC against the metadata our parser would produce.
/// </summary>
public class CueParserChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public CueParserChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(Path.GetTempPath(), "cue_parser_chdman_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testDataDir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    [Fact]
    public void SaturnStyleCue_MatchesChdmanMetadata()
    {
        if (ChdmanPath == null) return;

        // Saturn-style layout: MODE1/2352 data track + AUDIO tracks with 2s pregaps,
        // single BIN file (INDEX lengths for the first 4 tracks, file-size for the last)
        const string cue = """
                           FILE "game.bin" BINARY
                             TRACK 01 MODE1/2352
                               INDEX 01 00:00:00
                             TRACK 02 AUDIO
                               INDEX 00 03:00:00
                               INDEX 01 03:02:00
                             TRACK 03 AUDIO
                               INDEX 00 06:00:00
                               INDEX 01 06:02:00
                             TRACK 04 AUDIO
                               INDEX 00 09:00:00
                               INDEX 01 09:02:00
                             TRACK 05 AUDIO
                               INDEX 01 12:02:00
                           """;
        string cuePath = Path.Combine(_testDataDir, "saturn.cue");
        string binPath = Path.Combine(_testDataDir, "game.bin");
        string chdPath = Path.Combine(_testDataDir, "saturn.chd");
        File.WriteAllText(cuePath, cue);
        using (var fs = File.Create(binPath))
        {
            fs.SetLength(2352L * 54550);
        }

        var (exitCode, stdout, stderr) = RunChdman("createcd", "-i", cuePath, "-o", chdPath, "-c", "zlib", "-f");
        Assert.True(exitCode == 0, $"chdman createcd failed (exit={exitCode})\nstdout: {stdout}\nstderr: {stderr}");

        // parse the CUE with our parser and build the CHT2 metadata strings it implies
        var toc = CueParser.Parse(cuePath);
        var expected = toc.Tracks.Select(MetadataWriter.BuildChd2String).ToList();

        // read the metadata chdman actually wrote
        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            var actual = chd!.Metadata
                .Where(m => string.Equals(m.Tag, "CHT2", StringComparison.Ordinal))
                .Select(m => m.GetText().TrimEnd('\0'))
                .ToList();

            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
                Assert.Equal(expected[i], actual[i]);
        }
    }

    [Fact]
    public void TwoFileCue_MatchesChdmanMetadata()
    {
        if (ChdmanPath == null) return;

        const string cue = """
                           FILE "data.bin" BINARY
                             TRACK 01 MODE1/2352
                               INDEX 01 00:00:00
                           FILE "audio.bin" BINARY
                             TRACK 02 AUDIO
                               INDEX 00 00:02:00
                               INDEX 01 00:04:00
                           """;
        string cuePath = Path.Combine(_testDataDir, "twofile.cue");
        string chdPath = Path.Combine(_testDataDir, "twofile.chd");
        File.WriteAllText(cuePath, cue);
        using (var fs = File.Create(Path.Combine(_testDataDir, "data.bin")))
        {
            fs.SetLength(2352L * 300);
        }

        using (var fs = File.Create(Path.Combine(_testDataDir, "audio.bin")))
        {
            fs.SetLength(2352L * 100);
        }

        var (exitCode, stdout, stderr) = RunChdman("createcd", "-i", cuePath, "-o", chdPath, "-c", "zlib", "-f");
        Assert.True(exitCode == 0, $"chdman createcd failed (exit={exitCode})\nstdout: {stdout}\nstderr: {stderr}");

        var toc = CueParser.Parse(cuePath);
        var expected = toc.Tracks.Select(MetadataWriter.BuildChd2String).ToList();

        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            var actual = chd!.Metadata
                .Where(m => string.Equals(m.Tag, "CHT2", StringComparison.Ordinal))
                .Select(m => m.GetText().TrimEnd('\0'))
                .ToList();

            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
                Assert.Equal(expected[i], actual[i]);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunChdman(params string[] args)
    {
        string chdmanPath = ChdmanPath ?? throw new InvalidOperationException("chdman.exe not available");

        var psi = new ProcessStartInfo
        {
            FileName = chdmanPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();

        return (p.ExitCode, tOut.Result, tErr.Result);
    }

    private static string? ResolveChdmanPath()
    {
        string exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";

        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate))
            return candidate;

        candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName));
        if (File.Exists(candidate))
            return candidate;

        return null;
    }
}