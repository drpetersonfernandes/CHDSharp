using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

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
        Assert.Contains("Zstandard", info, StringComparison.Ordinal);

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
        Assert.Contains("LZMA", info, StringComparison.Ordinal);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        var (extractExit, eOut, eErr) = RunChdman("extractraw", "-i", chdPath, "-o", extractPath, "-f");
        Assert.True(extractExit == 0, $"extractraw failed (exit={extractExit})\n{eOut}{eErr}");

        Assert.Equal(source, File.ReadAllBytes(extractPath));
    }

    [Fact]
    public void LzmaChd_ByteIdenticalToChdmanOnTextCorpus()
    {
        if (ChdmanPath == null) return;

        // Deterministic pseudo-English text (seed 1337, the battle-test corpus that exposed
        // the LZMA parse divergence: 4-byte-hash collisions + near-tie price decisions).
        var rng = new Random(1337);
        const string common = "etaoinshrdlucmfwypvbgkjqxz";
        const string all = "etaoinshrdlucmfwypvbgkjqxz ETAOINSHRDLUCMFWYPVBGKJQXZ0123456789.,!?;:'\"()-";
        const int size = 4096 * 128;
        var source = new byte[size];
        for (int i = 0; i < size; i++)
        {
            var r = rng.NextDouble();
            source[i] = r switch
            {
                < 0.45 => (byte)common[rng.Next(common.Length)],
                < 0.90 => (byte)all[rng.Next(all.Length)],
                < 0.94 => (byte)' ',
                _ => (byte)'\n'
            };
        }

        string srcPath = Path.Combine(_testDataDir, "lzma-text.bin");
        string oursPath = Path.Combine(_testDataDir, "lzma-text.ours.chd");
        string refPath = Path.Combine(_testDataDir, "lzma-text.ref.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, oursPath, 4096, 512, [CodecTags.Lzma]);

        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", refPath, "-c", "lzma", "-hs", "4096", "-us", "512", "-f");
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\n{cOut}{cErr}");

        Assert.Equal(File.ReadAllBytes(refPath), File.ReadAllBytes(oursPath));
    }

    [Fact]
    public void CdzsChd_PassesChdmanVerifyAndExtractsByteIdentically()
    {
        if (ChdmanPath == null) return;

        // Compressible mixed CD (MODE1 data + AUDIO). The cdzs compound codec zstd-compresses the
        // base and subcode buffers separately; the managed ZstdSharp port emits frames with a
        // different trailing byte than C zstd on such buffers, so output is valid and chdman-
        // verifiable but not bit-identical to chdman's own file — asserted via verify + deep
        // CheckFile + extractcd parity instead of whole-file byte equality.
        const string cue = """
                           FILE "cdzs.bin" BINARY
                             TRACK 01 MODE1/2352
                               INDEX 01 00:00:00
                             TRACK 02 AUDIO
                               INDEX 01 00:04:00
                           """;
        string cuePath = Path.Combine(_testDataDir, "cdzs.cue");
        string binPath = Path.Combine(_testDataDir, "cdzs.bin");
        string oursPath = Path.Combine(_testDataDir, "cdzs.ours.chd");
        string refPath = Path.Combine(_testDataDir, "cdzs.ref.chd");
        File.WriteAllText(cuePath, cue);

        const int dataFrames = 300;
        const int audioFrames = 300;
        var bin = new byte[(dataFrames + audioFrames) * CdConstants.MaxSectorData];
        int pos = 0;
        for (int f = 0; f < dataFrames; f++)
        {
            for (int i = 0; i < CdConstants.MaxSectorData; i++, pos++)
                bin[pos] = (byte)("the quick brown fox jumps over the lazy dog "[i % 40] + (f % 7));
        }

        for (int f = 0; f < audioFrames; f++)
        {
            for (int i = 0; i < CdConstants.MaxSectorData / 2; i++)
            {
                short v = (short)(12000 * Math.Sin((f * CdConstants.MaxSectorData / 2 + i) * 0.02));
                bin[pos++] = (byte)(v & 0xFF);
                bin[pos++] = (byte)((v >> 8) & 0xFF);
            }
        }

        File.WriteAllBytes(binPath, bin);

        uint hunkBytes = CdConstants.FramesPerHunk * (uint)CdConstants.FrameSize;
        ChdEncoder.EncodeCd(cuePath, oursPath, hunkBytes: hunkBytes,
            unitBytes: (uint)CdConstants.FrameSize, codecTags: [CodecTags.Cdzs]);

        var (createExit, cOut, cErr) = RunChdman("createcd", "-i", cuePath, "-o", refPath, "-c", "cdzs",
            "-hs", hunkBytes.ToString(), "-f");
        Assert.True(createExit == 0, $"chdman createcd failed (exit={createExit})\n{cOut}{cErr}");

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", oursPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        using (var fs = File.OpenRead(oursPath))
        {
            var check = Chd.CheckFile(fs, oursPath, deepCheck: true);
            Assert.Equal(ChdError.Chderrnone, check.Error);
        }

        // chdman extractcd must reproduce the source BIN exactly from our output
        string extractPath = Path.Combine(_testDataDir, "cdzs.extract.bin");
        string extractCue = Path.Combine(_testDataDir, "cdzs.extract.cue");
        var (exExit, eOut, eErr) = RunChdman("extractcd", "-i", oursPath, "-o", extractCue, "-ob", extractPath, "-f");
        Assert.True(exExit == 0, $"extractcd failed (exit={exExit})\n{eOut}{eErr}");
        Assert.Equal(bin, File.ReadAllBytes(extractPath));
    }

    [Fact]
    public void FlacChd_ByteIdenticalToChdmanOnPcm16Corpus()
    {
        if (ChdmanPath == null) return;

        // Deterministic sine-wave PCM (compressible, exercises LPC subframe selection).
        const int size = 4096 * 8;
        var source = new byte[size];
        for (int i = 0; i < size / 2; i++)
        {
            short v = (short)(30000 * Math.Sin(i * 0.01) + 5000 * Math.Sin(i * 0.001));
            source[2 * i] = (byte)(v & 0xFF);
            source[2 * i + 1] = (byte)((v >> 8) & 0xFF);
        }

        string srcPath = Path.Combine(_testDataDir, "flac-pcm16.bin");
        string oursPath = Path.Combine(_testDataDir, "flac-pcm16.ours.chd");
        string refPath = Path.Combine(_testDataDir, "flac-pcm16.ref.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, oursPath, 4096, 512, [CodecTags.Flac]);

        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", refPath, "-c", "flac", "-hs", "4096", "-us", "512", "-f");
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\n{cOut}{cErr}");

        Assert.Equal(File.ReadAllBytes(refPath), File.ReadAllBytes(oursPath));
    }

    [Fact]
    public void CdflChd_RandomData_PassesChdmanVerifyAndDeepCheck()
    {
        if (ChdmanPath == null) return;

        // Random CD data forces VERBATIM subframes in the FLAC encoding; the encoder must
        // store the actual samples (a stale zeroed sample buffer corrupts every hunk).
        const string cue = """
                           FILE "cdfl.bin" BINARY
                             TRACK 01 MODE1/2352
                               INDEX 01 00:00:00
                             TRACK 02 AUDIO
                               INDEX 01 00:00:10
                           """;
        string cuePath = Path.Combine(_testDataDir, "cdfl.cue");
        string binPath = Path.Combine(_testDataDir, "cdfl.bin");
        string chdPath = Path.Combine(_testDataDir, "cdfl.chd");
        File.WriteAllText(cuePath, cue);

        var bin = new byte[(10 + 40) * CdConstants.MaxSectorData];
        new Random(1234).NextBytes(bin);
        File.WriteAllBytes(binPath, bin);

        ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes: CdConstants.FramesPerHunk * CdConstants.FrameSize,
            unitBytes: CdConstants.FrameSize, codecTags: [CodecTags.Cdfl]);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        using var fs = File.OpenRead(chdPath);
        var check = Chd.CheckFile(fs, chdPath, deepCheck: true);
        Assert.Equal(ChdError.Chderrnone, check.Error);

        // chdman extractcd must reproduce the source BIN exactly
        string extractPath = Path.Combine(_testDataDir, "cdfl.extract.bin");
        string extractCue = Path.Combine(_testDataDir, "cdfl.extract.cue");
        var (exExit, eOut, eErr) = RunChdman("extractcd", "-i", chdPath, "-o", extractCue, "-ob", extractPath, "-f");
        Assert.True(exExit == 0, $"extractcd failed (exit={exExit})\n{eOut}{eErr}");
        Assert.Equal(bin, File.ReadAllBytes(extractPath));
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
        Assert.Contains("zlib", info, StringComparison.Ordinal);
        Assert.Contains("Zstandard", info, StringComparison.Ordinal);
        Assert.Contains("LZMA", info, StringComparison.Ordinal);

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", chdPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");
    }

    [Fact]
    public void EncodeCd_WithZstd_PassesChdmanVerify()
    {
        if (ChdmanPath == null) return;

        const string cue = """
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
        Assert.Contains("Zstandard", infoOut + infoErr, StringComparison.Ordinal);
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