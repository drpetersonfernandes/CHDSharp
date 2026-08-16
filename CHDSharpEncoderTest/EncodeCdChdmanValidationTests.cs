using System.Diagnostics;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates EncodeCd against chdman.exe: the same CUE+BIN is converted with our encoder and
/// with chdman createcd; extracted data, verification, and metadata must all agree.
/// </summary>
public class EncodeCdChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public EncodeCdChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(Path.GetTempPath(), "encode_cd_chdman_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDataDir, recursive: true); } catch { }
    }

    [Fact]
    public void EncodeCd_ExtractRaw_MatchesChdman()
    {
        if (ChdmanPath == null) return;

        // data track + 2 audio tracks with pregaps, single BIN (10 + 12 + 8 = 30 sectors)
        string cue = """
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:10
                INDEX 01 00:00:12
              TRACK 03 AUDIO
                INDEX 01 00:00:22
            """;
        byte[] bin = BuildBin(30);
        string cuePath = WriteCue("game.cue", cue);
        File.WriteAllBytes(Path.Combine(_testDataDir, "game.bin"), bin);

        string ourChd = Path.Combine(_testDataDir, "our.chd");
        string chdmanChd = Path.Combine(_testDataDir, "chdman.chd");
        ChdEncoder.EncodeCd(cuePath, ourChd);

        var (createExit, cstdout, cstderr) = RunChdman("createcd", "-i", cuePath, "-o", chdmanChd, "-c", "zlib", "-f");
        Assert.True(createExit == 0, $"chdman createcd failed (exit={createExit})\nstdout: {cstdout}\nstderr: {cstderr}");

        string ourExtract = Path.Combine(_testDataDir, "our.raw");
        string chdmanExtract = Path.Combine(_testDataDir, "chdman.raw");
        var (e1, o1, e1r) = RunChdman("extractraw", "-i", ourChd, "-o", ourExtract, "-f");
        Assert.True(e1 == 0, $"extractraw our failed (exit={e1})\nstdout: {o1}\nstderr: {e1r}");
        var (e2, o2, e2r) = RunChdman("extractraw", "-i", chdmanChd, "-o", chdmanExtract, "-f");
        Assert.True(e2 == 0, $"extractraw chdman failed (exit={e2})\nstdout: {o2}\nstderr: {e2r}");

        // byte-identical logical images (audio swapped, tracks padded to 4-frame boundaries)
        Assert.Equal(File.ReadAllBytes(chdmanExtract), File.ReadAllBytes(ourExtract));
    }

    [Fact]
    public void EncodeCd_PassesChdmanVerify()
    {
        if (ChdmanPath == null) return;

        string cue = """
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:08
                INDEX 01 00:00:10
              TRACK 03 AUDIO
                INDEX 01 00:00:20
            """;
        string cuePath = WriteCue("verify.cue", cue);
        File.WriteAllBytes(Path.Combine(_testDataDir, "game.bin"), BuildBin(30));
        string chdPath = Path.Combine(_testDataDir, "verify.chd");

        ChdEncoder.EncodeCd(cuePath, chdPath);

        var (exitCode, stdout, stderr) = RunChdman("verify", "-i", chdPath);
        Assert.True(exitCode == 0, $"chdman verify failed (exit={exitCode})\nstdout: {stdout}\nstderr: {stderr}");
    }

    [Fact]
    public void EncodeCd_ChdmanInfo_ShowsMetadata()
    {
        if (ChdmanPath == null) return;

        string cue = """
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 01:00:00
            """;
        string cuePath = WriteCue("info.cue", cue);
        File.WriteAllBytes(Path.Combine(_testDataDir, "game.bin"), BuildBin(60 * 75 + 100));
        string chdPath = Path.Combine(_testDataDir, "info.chd");

        ChdEncoder.EncodeCd(cuePath, chdPath);

        var (exitCode, stdout, stderr) = RunChdman("info", "-i", chdPath);
        string output = stdout + stderr;
        Assert.True(exitCode == 0, $"chdman info failed (exit={exitCode})\n{output}");
        Assert.Contains("File Version: 5", output);
        Assert.Contains("Compression:  zlib", output);
        Assert.Contains("TRACK:1 TYPE:MODE1_RAW", output);
        Assert.Contains("TRACK:2 TYPE:AUDIO", output);
        Assert.DoesNotContain("Error", output);
    }

    [Fact]
    public void SaturnStyle_ChdmanRoundTrip()
    {
        if (ChdmanPath == null) return;

        // 1 data track (75 frames) + 7 audio tracks (75,75,75,75,75,75,50) with 2-frame pregaps
        const int dataFrames = 75;
        const int audioFrames = 75;
        const int lastTrackFrames = 50;
        byte[] bin = BuildBin(dataFrames + 6 * audioFrames + lastTrackFrames, dataFrames);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("FILE \"game.bin\" BINARY");
        sb.AppendLine("  TRACK 01 MODE1/2352");
        sb.AppendLine("    INDEX 01 00:00:00");
        for (int i = 2; i <= 8; i++)
        {
            int index00 = audioFrames * (i - 1);
            sb.AppendLine($"  TRACK {i:D2} AUDIO");
            sb.AppendLine($"    INDEX 00 {Msf(index00)}");
            sb.AppendLine($"    INDEX 01 {Msf(index00 + 2)}");
        }

        string cuePath = WriteCue("saturn.cue", sb.ToString());
        File.WriteAllBytes(Path.Combine(_testDataDir, "game.bin"), bin);
        string chdPath = Path.Combine(_testDataDir, "saturn.chd");

        ChdEncoder.EncodeCd(cuePath, chdPath);

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", chdPath);
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{infoOut}{infoErr}");

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        string extractPath = Path.Combine(_testDataDir, "saturn.raw");
        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        // 75→76 and 50→52 padded frames per track = 584 total
        byte[] expected = new byte[(76 * 7 + 52) * CdConstants.FrameSize];
        int chdStart = 0;
        int binOffsetBytes = 0;
        PlaceBinFrames(expected, chdStart, bin, dataFrames, binOffsetBytes, swap: false);
        chdStart += 76;
        binOffsetBytes += dataFrames * CdConstants.MaxSectorData;
        for (int i = 0; i < 6; i++)
        {
            PlaceBinFrames(expected, chdStart, bin, audioFrames, binOffsetBytes, swap: true);
            chdStart += 76;
            binOffsetBytes += audioFrames * CdConstants.MaxSectorData;
        }
        PlaceBinFrames(expected, chdStart, bin, lastTrackFrames, binOffsetBytes, swap: true);

        var actual = File.ReadAllBytes(extractPath);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    int frame = i / CdConstants.FrameSize;
                    throw new Xunit.Sdk.XunitException(
                        $"first difference at byte {i} (frame {frame}, offset {i % CdConstants.FrameSize}): " +
                        $"expected {expected[i]:X2}, actual {actual[i]:X2}");
                }
            }
        }
        Assert.Equal(expected, actual);
    }

    private static string Msf(int frames)
    {
        int m = frames / (60 * 75);
        int s = (frames / 75) % 60;
        int f = frames % 75;
        return $"{m:D2}:{s:D2}:{f:D2}";
    }

    // ----- helpers -----

    private string WriteCue(string name, string content)
    {
        string path = Path.Combine(_testDataDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Builds a BIN file: 2352-byte sectors; data pattern for the first
    /// <paramref name="dataFrames"/> sectors, then little-endian audio samples.</summary>
    private static byte[] BuildBin(int frames, int dataFrames = 1)
    {
        var bin = new byte[frames * CdConstants.MaxSectorData];
        for (int f = 0; f < frames; f++)
        {
            int offset = f * CdConstants.MaxSectorData;
            if (f < dataFrames)
            {
                // data track: distinct byte pattern
                for (int j = 0; j < CdConstants.MaxSectorData; j++)
                    bin[offset + j] = (byte)((f * 31 + j * 7) & 0xFF);
            }
            else
            {
                // audio track: little-endian 16-bit samples
                for (int j = 0; j < CdConstants.MaxSectorData / 2; j++)
                {
                    int sample = (f * 1000 + j) & 0xFFFF;
                    bin[offset + j * 2] = (byte)sample;
                    bin[offset + j * 2 + 1] = (byte)(sample >> 8);
                }
            }
        }
        return bin;
    }

    private static void PlaceBinFrames(byte[] image, int chdFrameStart, byte[] bin, int binFrameCount, int binOffset, bool swap)
    {
        for (int f = 0; f < binFrameCount; f++)
        {
            int dest = (chdFrameStart + f) * CdConstants.FrameSize;
            Array.Copy(bin, binOffset + f * CdConstants.MaxSectorData, image, dest, CdConstants.MaxSectorData);
            if (swap)
            {
                for (int i = 0; i < CdConstants.MaxSectorData; i += 2)
                    (image[dest + i], image[dest + i + 1]) = (image[dest + i + 1], image[dest + i]);
            }
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