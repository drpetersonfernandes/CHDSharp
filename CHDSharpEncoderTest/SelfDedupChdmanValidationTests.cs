using System.Diagnostics;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates SELF-hunk deduplication output against chdman.exe: deduplicated CHDs must
/// pass chdman verify, extract byte-identically, and report repeat blocks in chdman info.
/// </summary>
public class SelfDedupChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public SelfDedupChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(Path.GetTempPath(), "self_dedup_chdman_tests_" + Guid.NewGuid().ToString("N"));
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
    public void RepeatedHunks_PassChdmanVerify_AndExtract()
    {
        if (ChdmanPath == null) return;

        // 1 MiB made of 256 identical 4 KiB hunks
        byte[] source = new byte[4096 * 256];
        for (int i = 0; i < 4096; i++)
        {
            source[i] = (byte)(i & 0xFF);
        }

        for (int h = 1; h < 256; h++)
            Array.Copy(source, 0, source, h * 4096, 4096);

        string srcPath = Path.Combine(_testDataDir, "repeated.bin");
        string chdPath = Path.Combine(_testDataDir, "repeated.chd");
        string extractPath = Path.Combine(_testDataDir, "repeated.raw");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, chdPath, 4096, 512);

        // dedup proof: 255 of 256 hunks are SELF references, so the CHD is tiny
        Assert.True(new FileInfo(chdPath).Length < 4096 * 4,
            $"expected a deduplicated CHD, got {new FileInfo(chdPath).Length} bytes");

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Fact]
    public void RepeatedHunks_MatchChdmanExtraction()
    {
        if (ChdmanPath == null) return;

        byte[] patternA = new byte[4096];
        byte[] patternB = new byte[4096];
        for (int i = 0; i < 4096; i++)
        {
            patternA[i] = (byte)(i & 0xFF);
            patternB[i] = (byte)(~i & 0xFF);
        }

        byte[] source = new byte[4096 * 128];
        for (int h = 0; h < 128; h++)
            Array.Copy(h % 2 == 0 ? patternA : patternB, 0, source, h * 4096, 4096);

        string srcPath = Path.Combine(_testDataDir, "alternating.bin");
        string ourChd = Path.Combine(_testDataDir, "our.chd");
        string chdmanChd = Path.Combine(_testDataDir, "chdman.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, ourChd, 4096, 512);

        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", chdmanChd, "-c", "zlib", "-hs", "4096", "-us", "512", "-f");
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\n{cOut}{cErr}");

        // strongest check: byte-for-byte identical CHD files (dedup + map encoding parity)
        Assert.Equal(File.ReadAllBytes(chdmanChd), File.ReadAllBytes(ourChd));

        string ourExtract = Path.Combine(_testDataDir, "our.raw");
        string chdmanExtract = Path.Combine(_testDataDir, "chdman.raw");
        var (e1, o1, e1R) = RunChdman("extractraw", "-i", ourChd, "-o", ourExtract, "-f");
        Assert.True(e1 == 0, $"extractraw our failed (exit={e1})\n{o1}{e1R}");
        var (e2, o2, e2R) = RunChdman("extractraw", "-i", chdmanChd, "-o", chdmanExtract, "-f");
        Assert.True(e2 == 0, $"extractraw chdman failed (exit={e2})\n{o2}{e2R}");

        Assert.Equal(File.ReadAllBytes(chdmanExtract), File.ReadAllBytes(ourExtract));
        Assert.Equal(source, File.ReadAllBytes(ourExtract));
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