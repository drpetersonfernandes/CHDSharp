using System.Diagnostics;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates the CD FLAC ('cdfl') codec against chdman.exe: files must pass chdman verify,
/// report "CD FLAC" in chdman info, and extract byte-identically.
/// </summary>
public class CdflChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public CdflChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(Path.GetTempPath(), "cdfl_chdman_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDataDir, recursive: true); } catch { }
    }

    [Fact]
    public void CdflChd_PassesChdmanVerify_AndExtractsByteIdentically()
    {
        if (ChdmanPath == null) return;

        // data track with pattern + audio track with sine samples
        string cue = """
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:20
                INDEX 01 00:00:22
            """;
        string cuePath = Path.Combine(_testDataDir, "test.cue");
        File.WriteAllText(cuePath, cue);

        byte[] bin = new byte[(20 + 20) * CdConstants.MaxSectorData];
        for (int f = 0; f < 20; f++)
        {
            int offset = f * CdConstants.MaxSectorData;
            for (int i = 0; i < CdConstants.MaxSectorData; i++)
            {
                bin[offset + i] = (byte)(i & 0xFF); // MODE1 pattern
            }
        }
        for (int f = 20; f < 40; f++)
        {
            int offset = f * CdConstants.MaxSectorData;
            for (int s = 0; s < 588; s++)
            {
                // standard CUE/BIN audio: little-endian 16-bit samples
                int sample = (int)(Math.Sin(s * 0.05) * 12000);
                bin[offset + s * 4] = (byte)sample;
                bin[offset + s * 4 + 1] = (byte)(sample >> 8);
                bin[offset + s * 4 + 2] = (byte)sample;
                bin[offset + s * 4 + 3] = (byte)(sample >> 8);
            }
        }
        File.WriteAllBytes(Path.Combine(_testDataDir, "game.bin"), bin);

        string chdPath = Path.Combine(_testDataDir, "test.chd");
        ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes: CdConstants.FramesPerHunk * CdConstants.FrameSize,
            unitBytes: CdConstants.FrameSize, codecTags: [CodecTags.Cdfl]);

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", chdPath);
        string info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains("CD FLAC", info, StringComparison.Ordinal);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        string extractPath = Path.Combine(_testDataDir, "extracted.raw");
        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        // expected logical image: 20 data frames (raw) + 20 audio frames (byte-swapped to BE)
        byte[] expected = new byte[40 * CdConstants.FrameSize];
        PlaceBinFrames(expected, 0, bin, 20, 0, swap: false);
        PlaceBinFrames(expected, 20, bin, 20, 20 * CdConstants.MaxSectorData, swap: true);

        Assert.Equal(expected, File.ReadAllBytes(extractPath));
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
                {
                    (image[dest + i], image[dest + i + 1]) = (image[dest + i + 1], image[dest + i]);
                }
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