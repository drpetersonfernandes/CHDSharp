using System.Diagnostics;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates the zstd/lzma/multi-codec CHD output against chdman.exe: files must pass
/// chdman verify, report the right codec in chdman info, and extract byte-identically.
/// </summary>
public class ChdCodecChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public ChdCodecChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(Path.GetTempPath(), "chd_codec_chdman_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDataDir, recursive: true); } catch { }
    }

    [Fact]
    public void ZstdChd_PassesChdmanVerifyAndExtract()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateCompressible(128);
        string srcPath = Path.Combine(_testDataDir, "zstd.bin");
        string chdPath = Path.Combine(_testDataDir, "zstd.chd");
        string extractPath = Path.Combine(_testDataDir, "zstd.raw");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath, 4096, 512, [CodecTags.Zstd]);

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", chdPath);
        string info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains("Zstandard", info);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Fact]
    public void LzmaChd_PassesChdmanVerifyAndExtract()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateCompressible(128);
        string srcPath = Path.Combine(_testDataDir, "lzma.bin");
        string chdPath = Path.Combine(_testDataDir, "lzma.chd");
        string extractPath = Path.Combine(_testDataDir, "lzma.raw");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath, 4096, 512, [CodecTags.Lzma]);

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", chdPath);
        string info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains("LZMA", info);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Fact]
    public void MultiCodecChd_PassesChdmanVerify()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateCompressible(128);
        string srcPath = Path.Combine(_testDataDir, "multi.bin");
        string chdPath = Path.Combine(_testDataDir, "multi.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath, 4096, 512, [CodecTags.Zlib, CodecTags.Zstd, CodecTags.Lzma]);

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", chdPath);
        string info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains("zlib", info);
        Assert.Contains("Zstandard", info);
        Assert.Contains("LZMA", info);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");
    }

    [Fact]
    public void EncodeCd_WithZstd_PassesChdmanVerify()
    {
        if (ChdmanPath == null) return;

        string cue = """
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 00:00:40
                INDEX 01 00:00:42
            """;
        string cuePath = Path.Combine(_testDataDir, "cd.cue");
        string binPath = Path.Combine(_testDataDir, "game.bin");
        string chdPath = Path.Combine(_testDataDir, "cd.chd");
        File.WriteAllText(cuePath, cue);
        using (var fs = File.Create(binPath))
        {
            fs.SetLength(2352L * 82);
        }

        ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes: CdConstants.FramesPerHunk * CdConstants.FrameSize,
            unitBytes: CdConstants.FrameSize, codecTags: [CodecTags.Zstd]);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", chdPath);
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{infoOut}{infoErr}");
        Assert.Contains("Zstandard", infoOut + infoErr);
    }

    // ----- helpers -----

    private static byte[] CreateCompressible(int hunkCount)
    {
        byte[] source = new byte[4096 * hunkCount];
        for (int h = 0; h < hunkCount; h++)
        {
            for (int i = 0; i < 4064; i++)
            {
                source[h * 4096 + i] = 0;
            }

            for (int i = 4064; i < 4096; i++)
            {
                source[h * 4096 + i] = (byte)(h + i);
            }
        }
        return source;
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