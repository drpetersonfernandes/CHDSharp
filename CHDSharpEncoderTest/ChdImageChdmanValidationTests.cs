using System.Diagnostics;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates ISO/GDI/TOC encoding against chdman.exe: our EncodeCd output must pass
/// chdman verify and extract byte-identically to chdman's own createcd output.
/// </summary>
public class ChdImageChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public ChdImageChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(Path.GetTempPath(), "chd_image_chdman_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDataDir, recursive: true); } catch { }
    }

    [Fact]
    public void Iso_MatchesChdman_ByteForByte()
    {
        if (ChdmanPath == null) return;

        byte[] iso = new byte[2048 * 120];
        for (int s = 0; s < 120; s++)
            for (int i = 0; i < 2048; i++)
        {
            iso[s * 2048 + i] = (byte)((s * 13 + i) & 0xFF);
        }

        string isoPath = Path.Combine(_testDataDir, "game.iso");
        File.WriteAllBytes(isoPath, iso);

        string ourChd = Path.Combine(_testDataDir, "our.chd");
        string chdmanChd = Path.Combine(_testDataDir, "chdman.chd");
        ChdEncoder.EncodeCd(isoPath, ourChd);

        var (createExit, cOut, cErr) = RunChdman("createcd", "-i", isoPath, "-o", chdmanChd, "-c", "zlib", "-f");
        Assert.True(createExit == 0, $"chdman createcd failed (exit={createExit})\n{cOut}{cErr}");

        string ourExtract = Path.Combine(_testDataDir, "our.raw");
        string chdmanExtract = Path.Combine(_testDataDir, "chdman.raw");
        var (e1, o1, e1R) = RunChdman("extractraw", "-i", ourChd, "-o", ourExtract, "-f");
        Assert.True(e1 == 0, $"extractraw our failed (exit={e1})\n{o1}{e1R}");
        var (e2, o2, e2R) = RunChdman("extractraw", "-i", chdmanChd, "-o", chdmanExtract, "-f");
        Assert.True(e2 == 0, $"extractraw chdman failed (exit={e2})\n{o2}{e2R}");

        Assert.Equal(File.ReadAllBytes(chdmanExtract), File.ReadAllBytes(ourExtract));

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", ourChd);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        // 120 frames pad to 120; logical image = 120 x 2448 with 2048 data + 400 zeros per frame
        byte[] expected = new byte[120 * CdConstants.FrameSize];
        for (int f = 0; f < 120; f++)
            Array.Copy(iso, f * 2048, expected, f * CdConstants.FrameSize, 2048);
        Assert.Equal(expected, File.ReadAllBytes(ourExtract));
    }

    [Fact]
    public void Gdi_MatchesChdman_ByteForByte()
    {
        if (ChdmanPath == null) return;

        // track 1: 80 MODE1/2352 frames @ LBA 0; track 2: 40 audio frames @ LBA 45000
        // (large Dreamcast-style gap -> pad frames); track 3: 40 audio @ LBA 45100
        byte[] dataBin = new byte[2352 * 80];
        byte[] audio1 = BuildAudio(40, 100);
        byte[] audio2 = BuildAudio(40, 200);
        for (int i = 0; i < dataBin.Length; i++)
        {
            dataBin[i] = (byte)(i & 0xFF);
        }

        File.WriteAllBytes(Path.Combine(_testDataDir, "track01.bin"), dataBin);
        File.WriteAllBytes(Path.Combine(_testDataDir, "track02.raw"), audio1);
        File.WriteAllBytes(Path.Combine(_testDataDir, "track03.raw"), audio2);
        string gdiPath = Path.Combine(_testDataDir, "game.gdi");
        File.WriteAllText(gdiPath, """
            3
            1 0 4 2352 "track01.bin" 0
            2 45000 0 2352 "track02.raw" 0
            3 45100 0 2352 "track03.raw" 0
            """);

        string ourChd = Path.Combine(_testDataDir, "our.chd");
        string chdmanChd = Path.Combine(_testDataDir, "chdman.chd");
        ChdEncoder.EncodeCd(gdiPath, ourChd);

        var (createExit, cOut, cErr) = RunChdman("createcd", "-i", gdiPath, "-o", chdmanChd, "-c", "zlib", "-f");
        Assert.True(createExit == 0, $"chdman createcd failed (exit={createExit})\n{cOut}{cErr}");

        var (infoExit, infoOut, infoErr) = RunChdman("info", "-i", ourChd);
        string info = infoOut + infoErr;
        Assert.True(infoExit == 0, $"chdman info failed (exit={infoExit})\n{info}");
        Assert.Contains("CHGD", info);
        Assert.Contains("PAD:", info);

        string ourExtract = Path.Combine(_testDataDir, "our.raw");
        string chdmanExtract = Path.Combine(_testDataDir, "chdman.raw");
        var (e1, o1, e1R) = RunChdman("extractraw", "-i", ourChd, "-o", ourExtract, "-f");
        Assert.True(e1 == 0, $"extractraw our failed (exit={e1})\n{o1}{e1R}");
        var (e2, o2, e2R) = RunChdman("extractraw", "-i", chdmanChd, "-o", chdmanExtract, "-f");
        Assert.True(e2 == 0, $"extractraw chdman failed (exit={e2})\n{o2}{e2R}");

        Assert.Equal(File.ReadAllBytes(chdmanExtract), File.ReadAllBytes(ourExtract));

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", ourChd);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");

        // expected image: track1 = 80 data + 44920 pad (→ 45000), track2 = 40 + 60 pad (→ 45100),
        // track3 = 40; all 4-frame aligned already
        byte[] expected = new byte[(45000 + 100 + 40) * CdConstants.FrameSize];
        PlaceTrack(expected, 0, dataBin, 80, 0, swap: false);
        PlaceTrack(expected, 45000, audio1, 40, 0, swap: true);
        PlaceTrack(expected, 45100, audio2, 40, 0, swap: true);
        Assert.Equal(expected, File.ReadAllBytes(ourExtract));
    }

    [Fact]
    public void Toc_MatchesChdman_ByteForByte()
    {
        if (ChdmanPath == null) return;

        byte[] data = new byte[2352 * 60];
        byte[] audio = BuildAudio(60, 300);
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 7) & 0xFF);
        }

        File.WriteAllBytes(Path.Combine(_testDataDir, "data.bin"), data);
        File.WriteAllBytes(Path.Combine(_testDataDir, "audio.wav"), audio);
        string tocPath = Path.Combine(_testDataDir, "disc.toc");
        File.WriteAllText(tocPath, """
            TRACK MODE1/2352
            DATAFILE "data.bin" 0 00:00:60
            TRACK AUDIO
            AUDIOFILE "audio.wav" 0 00:00:60
            START 00:00:02
            """);

        string ourChd = Path.Combine(_testDataDir, "our.chd");
        string chdmanChd = Path.Combine(_testDataDir, "chdman.chd");
        ChdEncoder.EncodeCd(tocPath, ourChd);

        var (createExit, cOut, cErr) = RunChdman("createcd", "-i", tocPath, "-o", chdmanChd, "-c", "zlib", "-f");
        Assert.True(createExit == 0, $"chdman createcd failed (exit={createExit})\n{cOut}{cErr}");

        string ourExtract = Path.Combine(_testDataDir, "our.raw");
        string chdmanExtract = Path.Combine(_testDataDir, "chdman.raw");
        var (e1, o1, e1R) = RunChdman("extractraw", "-i", ourChd, "-o", ourExtract, "-f");
        Assert.True(e1 == 0, $"extractraw our failed (exit={e1})\n{o1}{e1R}");
        var (e2, o2, e2R) = RunChdman("extractraw", "-i", chdmanChd, "-o", chdmanExtract, "-f");
        Assert.True(e2 == 0, $"extractraw chdman failed (exit={e2})\n{o2}{e2R}");

        Assert.Equal(File.ReadAllBytes(chdmanExtract), File.ReadAllBytes(ourExtract));

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", ourChd);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");
    }

    // ----- helpers -----

    private static byte[] BuildAudio(int frames, int seed)
    {
        byte[] bin = new byte[frames * CdConstants.MaxSectorData];
        for (int f = 0; f < frames; f++)
        {
            int offset = f * CdConstants.MaxSectorData;
            for (int s = 0; s < 588; s++)
            {
                int sample = (int)(Math.Sin(s * 0.05 + (f + seed) * 0.01) * 12000);
                bin[offset + s * 4] = (byte)sample;
                bin[offset + s * 4 + 1] = (byte)(sample >> 8);
                bin[offset + s * 4 + 2] = (byte)sample;
                bin[offset + s * 4 + 3] = (byte)(sample >> 8);
            }
        }
        return bin;
    }

    /// <summary>Places a track's real frames into the logical image; pad frames stay zero.</summary>
    private static void PlaceTrack(byte[] image, int chdFrameStart, byte[] bin, int binFrameCount, int binOffset, bool swap)
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