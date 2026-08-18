using System.Diagnostics;
using System.Security.Cryptography;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Large-file (100 MB+) integration tests: encodes, then validates the result with
/// <c>chdman verify</c>, <c>chdman extractraw</c> (SHA-1 compared against the source),
/// and a deep CHDSharpLib check.
/// </summary>
public class LargeFileValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _dir;

    public LargeFileValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _dir = Path.Combine(Path.GetTempPath(), "large_file_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    [Fact]
    public void Raw_100Mb_RoundTrip_PassesChdman()
    {
        if (ChdmanPath == null) return;

        const int hunkBytes = 65536;
        const long size = 100L * 1024 * 1024; // 100 MB
        string srcPath = Path.Combine(_dir, "large_src.bin");
        string chdPath = Path.Combine(_dir, "large.chd");
        string extractPath = Path.Combine(_dir, "large_extracted.raw");

        WriteMixedData(srcPath, size, hunkBytes, seed: 2024);
        string srcSha1 = Sha1Hex(srcPath);

        ChdEncoder.EncodeRaw(srcPath, chdPath, hunkBytes, 4096);

        long chdSize = new FileInfo(chdPath).Length;
        // two thirds of the data is a repeating pattern, so the CHD must be well under half the source size
        Assert.True(chdSize < size / 2, $"expected significant compression, CHD is {chdSize:N0} bytes");

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        Assert.Equal(srcSha1, Sha1Hex(extractPath));

        // the CHDSharpLib deep check must agree with chdman
        using var fs = File.OpenRead(chdPath);
        var err = Chd.CheckFile(fs, chdPath, true, out var version, out _, out _);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(5u, version);
    }

    [Fact]
    public void Cd_100Mb_RoundTrip_PassesChdman()
    {
        if (ChdmanPath == null) return;

        // 16 data frames + 44600 audio frames: BIN is (16 + 44600) * 2352 ≈ 100 MB;
        // 44616 % 4 == 0, so no track padding is needed
        const int dataFrames = 16;
        const int audioFrames = 44600;
        string cuePath = Path.Combine(_dir, "large.cue");
        string binPath = Path.Combine(_dir, "large.bin");
        string chdPath = Path.Combine(_dir, "large_cd.chd");
        string extractPath = Path.Combine(_dir, "large_cd.raw");

        WriteCdBin(binPath, dataFrames, audioFrames);
        File.WriteAllText(cuePath, $"""
            FILE "large.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 {dataFrames / (60 * 75):D2}:{(dataFrames / 75) % 60:D2}:{dataFrames % 75:D2}
            """);

        ChdEncoder.EncodeCd(cuePath, chdPath);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        // extractraw returns the big-endian logical image (audio byte-swapped, zero subcode)
        Assert.Equal(ExpectedCdImageSha1(dataFrames, audioFrames), Sha1Hex(extractPath));

        // the CHDSharpLib deep check must agree with chdman
        using var fs = File.OpenRead(chdPath);
        var err = Chd.CheckFile(fs, chdPath, true, out var version, out _, out _);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(5u, version);
    }

    // ----- helpers -----

    /// <summary>Writes a deterministic 100 MB-class raw file: alternating compressible
    /// pattern blocks and incompressible seeded-random blocks.</summary>
    private static void WriteMixedData(string path, long size, int blockBytes, int seed)
    {
        var pattern = new byte[blockBytes];
        for (int i = 0; i < blockBytes; i++)
        {
            pattern[i] = (byte)(i & 0xFF);
        }

        var randomBlock = new byte[blockBytes];
        var rng = new Random(seed);

        using var fs = File.Create(path);
        for (long offset = 0; offset < size; offset += blockBytes)
        {
            int n = (int)Math.Min(blockBytes, size - offset);
            // every third block is incompressible; the rest are a repeating pattern
            byte[] block = (offset / blockBytes) % 3 == 0 ? randomBlock : pattern;
            if (ReferenceEquals(block, randomBlock))
                rng.NextBytes(randomBlock.AsSpan(0, n));
            fs.Write(block, 0, n);
        }
    }

    /// <summary>Writes a BIN file: <paramref name="dataFrames"/> patterned data sectors
    /// followed by <paramref name="audioFrames"/> little-endian audio sectors (matching the
    /// sector layout used by the other CD tests).</summary>
    private static void WriteCdBin(string path, int dataFrames, int audioFrames)
    {
        var sector = new byte[CdConstants.MaxSectorData];
        using var fs = File.Create(path);

        for (int f = 0; f < dataFrames; f++)
        {
            for (int j = 0; j < CdConstants.MaxSectorData; j++)
            {
                sector[j] = (byte)((f * 31 + j * 7) & 0xFF);
            }

            fs.Write(sector);
        }

        for (int f = 0; f < audioFrames; f++)
        {
            for (int j = 0; j < CdConstants.MaxSectorData / 2; j++)
            {
                int sample = (f * 1000 + j) & 0xFFFF;
                sector[j * 2] = (byte)sample;
                sector[j * 2 + 1] = (byte)(sample >> 8);
            }

            fs.Write(sector);
        }
    }

    /// <summary>SHA-1 of the logical image chdman extractraw returns for the BIN written by
    /// <see cref="WriteCdBin"/>: 2448-byte frames, audio samples byte-swapped to big-endian,
    /// 96 bytes of zero subcode per frame.</summary>
    private static string ExpectedCdImageSha1(int dataFrames, int audioFrames)
    {
        using var sha = SHA1.Create();
        var frame = new byte[CdConstants.FrameSize];
        int total = dataFrames + audioFrames;

        for (int f = 0; f < total; f++)
        {
            Array.Clear(frame);
            if (f < dataFrames)
            {
                for (int j = 0; j < CdConstants.MaxSectorData; j++)
                {
                    frame[j] = (byte)((f * 31 + j * 7) & 0xFF);
                }
            }
            else
            {
                int audioFrame = f - dataFrames;
                for (int j = 0; j < CdConstants.MaxSectorData / 2; j++)
                {
                    int sample = (audioFrame * 1000 + j) & 0xFFFF;
                    frame[j * 2] = (byte)(sample >> 8);
                    frame[j * 2 + 1] = (byte)sample;
                }
            }

            sha.TransformBlock(frame, 0, frame.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash ?? []).ToLowerInvariant();
    }

    private static string Sha1Hex(string path)
    {
        using var sha = SHA1.Create();
        using var fs = File.OpenRead(path);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            sha.TransformBlock(buffer, 0, read, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash ?? []).ToLowerInvariant();
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