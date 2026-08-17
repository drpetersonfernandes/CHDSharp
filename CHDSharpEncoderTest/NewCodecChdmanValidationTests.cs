using System.Diagnostics;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates the Phase-1 codecs ('huff', 'flac', 'cdzl', 'cdlz', 'cdzs') against
/// chdman.exe: files must pass chdman verify, report the right codec in chdman info,
/// and extract byte-identically.
/// </summary>
public class NewCodecChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public NewCodecChdmanValidationTests()
    {
        _testDataDir = Path.Combine(Path.GetTempPath(), "new_codec_chdman_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDataDir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("huff", "Huffman")]
    [InlineData("flac", "FLAC")]
    public void RawCodec_PassesChdmanVerify_AndExtractsByteIdentically(string codecName, string chdmanCodecName)
    {
        if (ChdmanPath == null) return;

        // 16-bit stereo sample data: FLAC-compressible, and huff handles the raw bytes
        byte[] source = new byte[4096 * 32];
        var rng = new Random(1234);
        for (int i = 0; i < source.Length; i += 4)
        {
            source[i] = (byte)rng.Next(0, 0x8000);         // left sample (LE)
            source[i + 1] = (byte)(rng.Next(0, 0x8000) >> 8);
            source[i + 2] = (byte)((i / 4) % 0x7FFF);      // right ramp
            source[i + 3] = (byte)(((i / 4) % 0x7FFF) >> 8);
        }

        string srcPath = Path.Combine(_testDataDir, $"{codecName}_src.bin");
        string chdPath = Path.Combine(_testDataDir, $"{codecName}.chd");
        File.WriteAllBytes(srcPath, source);
        ChdEncoder.EncodeRaw(srcPath, chdPath, 4096, 512, [CodecTags.FromName(codecName)]);

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", chdPath);
        string info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains(chdmanCodecName, info);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        string extractPath = Path.Combine(_testDataDir, $"{codecName}_extracted.raw");
        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Theory]
    [InlineData("cdzl", "CD Deflate")]
    [InlineData("cdlz", "CD LZMA")]
    [InlineData("cdzs", "CD Zstandard")]
    public void CdCodec_PassesChdmanVerify_AndExtractsByteIdentically(string codecName, string chdmanCodecName)
    {
        if (ChdmanPath == null) return;

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

        byte[] bin = new byte[40 * CdConstants.MaxSectorData];
        for (int f = 0; f < 20; f++)
        {
            int offset = f * CdConstants.MaxSectorData;
            for (int i = 0; i < CdConstants.MaxSectorData; i++)
            {
                bin[offset + i] = (byte)(i & 0xFF);
            }
        }
        for (int f = 20; f < 40; f++)
        {
            int offset = f * CdConstants.MaxSectorData;
            for (int s = 0; s < 588; s++)
            {
                int sample = (int)(Math.Sin(s * 0.05) * 12000);
                bin[offset + s * 4] = (byte)sample;
                bin[offset + s * 4 + 1] = (byte)(sample >> 8);
                bin[offset + s * 4 + 2] = (byte)sample;
                bin[offset + s * 4 + 3] = (byte)(sample >> 8);
            }
        }
        File.WriteAllBytes(Path.Combine(_testDataDir, "game.bin"), bin);

        string chdPath = Path.Combine(_testDataDir, $"{codecName}.chd");
        ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes: CdConstants.FramesPerHunk * CdConstants.FrameSize,
            unitBytes: CdConstants.FrameSize, codecTags: [CodecTags.FromName(codecName)]);

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", chdPath);
        string info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains(chdmanCodecName, info);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        string extractPath = Path.Combine(_testDataDir, $"{codecName}_extracted.raw");
        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        // expected logical image: 20 data frames + 20 audio frames (byte-swapped) + zero padding
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

    private static string? ResolveChdmanPath()
    {
        string repoRoot = FindRepoRoot();
        foreach (string candidate in new[] { "chdman.exe", "chdman" })
        {
            string path = Path.Combine(repoRoot, candidate);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "chdman.exe")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static (int exit, string stdout, string stderr) RunChdman(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ChdmanPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}