using System.Diagnostics;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class ChdmanValidationTests : IDisposable
{
    private static readonly string TestDataDir =
        Path.Combine(Path.GetTempPath(), "chd_encoder_chdman_tests");

    private static readonly string? ChdmanPath = ResolveChdmanPath();

    public ChdmanValidationTests()
    {
        Directory.CreateDirectory(TestDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(TestDataDir, recursive: true); } catch { }
    }

    [Fact]
    public void Chdman_Info_ReportsCorrectly()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateTestFile(8192, 42);
        string srcPath = Path.Combine(TestDataDir, "info_src.bin");
        string chdPath = Path.Combine(TestDataDir, "info.chd");
        File.WriteAllBytes(srcPath, source);

        new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

        var (exitCode, stdout, stderr) = RunChdman("info", "-i", chdPath);
        Assert.True(exitCode == 0, $"chdman info exit code: {exitCode}\nstdout: {stdout}\nstderr: {stderr}");

        string output = stdout + stderr;
        Assert.Contains("File Version: 5", output);
        Assert.Contains("zlib", output);
        Assert.DoesNotContain("Error", output);
    }

    [Fact]
    public void Chdman_Verify_Passes()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateTestFile(65536, 123);
        string srcPath = Path.Combine(TestDataDir, "verify_src.bin");
        string chdPath = Path.Combine(TestDataDir, "verify.chd");
        File.WriteAllBytes(srcPath, source);

        new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

        var (verifyExit, vstdout, vstderr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"verify failed (exit={verifyExit})\nstdout: {vstdout}\nstderr: {vstderr}");
    }

    [Fact]
    public void Chdman_Extract_ProducesIdenticalData()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateTestFile(65536, 456);
        string srcPath = Path.Combine(TestDataDir, "extract_src.bin");
        string chdPath = Path.Combine(TestDataDir, "extract.chd");
        string extractedPath = Path.Combine(TestDataDir, "extracted.raw");
        File.WriteAllBytes(srcPath, source);

        new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

        var (exitCode, estdout, estderr) = RunChdman("extractraw", "-i", chdPath, "-o", extractedPath, "-f");
        Assert.True(exitCode == 0, $"extractraw failed (exit={exitCode})\nstdout: {estdout}\nstderr: {estderr}");

        byte[] extracted = File.ReadAllBytes(extractedPath);
        Assert.Equal(source, extracted);
    }

    [Fact]
    public void OurOutput_MatchesChdmanOutput()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateTestFile(65536, 789);
        string srcPath = Path.Combine(TestDataDir, "cross_src.bin");
        string ourChd = Path.Combine(TestDataDir, "cross_our.chd");
        string chdmanChd = Path.Combine(TestDataDir, "cross_chdman.chd");
        string ourExtract = Path.Combine(TestDataDir, "cross_our_extracted.raw");
        string chdmanExtract = Path.Combine(TestDataDir, "cross_chdman_extracted.raw");
        File.WriteAllBytes(srcPath, source);

        new ChdEncoder().EncodeRaw(srcPath, ourChd, 4096, 512);

        var (createExit, cstdout, cstderr) = RunChdman(
            "createraw", "-i", srcPath, "-o", chdmanChd, "-c", "zlib", "-hs", "4096", "-us", "512", "-f");
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\nstdout: {cstdout}\nstderr: {cstderr}");

        var (ext1Exit, e1stdout, e1stderr) = RunChdman("extractraw", "-i", ourChd, "-o", ourExtract, "-f");
        Assert.True(ext1Exit == 0, $"extractraw our failed (exit={ext1Exit})\nstdout: {e1stdout}\nstderr: {e1stderr}");

        var (ext2Exit, e2stdout, e2stderr) = RunChdman("extractraw", "-i", chdmanChd, "-o", chdmanExtract, "-f");
        Assert.True(ext2Exit == 0, $"extractraw chdman failed (exit={ext2Exit})\nstdout: {e2stdout}\nstderr: {e2stderr}");

        byte[] ourExtracted = File.ReadAllBytes(ourExtract);
        byte[] chdmanExtracted = File.ReadAllBytes(chdmanExtract);

        Assert.Equal(source, ourExtracted);
        Assert.Equal(source, chdmanExtracted);
        Assert.Equal(ourExtracted, chdmanExtracted);

        var (verifyExit, vstdout, vstderr) = RunChdman("verify", "-i", ourChd);
        Assert.True(verifyExit == 0, $"verify our failed (exit={verifyExit})\nstdout: {vstdout}\nstderr: {vstderr}");
    }

    [Fact]
    public void NonAlignedSize_ChdmanExtractWorks()
    {
        if (ChdmanPath == null) return;

        byte[] source = CreateTestFile(10000, 42);
        string srcPath = Path.Combine(TestDataDir, "na_src.bin");
        string chdPath = Path.Combine(TestDataDir, "na.chd");
        string extractedPath = Path.Combine(TestDataDir, "na_extracted.raw");
        File.WriteAllBytes(srcPath, source);

        new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

        var (exitCode, nastdout, nastderr) = RunChdman("extractraw", "-i", chdPath, "-o", extractedPath, "-f");
        Assert.True(exitCode == 0, $"extractraw failed (exit={exitCode})\nstdout: {nastdout}\nstderr: {nastderr}");

        byte[] extracted = File.ReadAllBytes(extractedPath);
        Assert.Equal(source, extracted);
    }

    // ----- helpers -----

    private static byte[] CreateTestFile(int size, int seed)
    {
        byte[] data = new byte[size];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
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

        // check alongside the test assembly
        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate))
            return candidate;

        // check Tester project dir (for IDE Test Explorer runs)
        candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName));
        if (File.Exists(candidate))
            return candidate;

        return null;
    }
}
