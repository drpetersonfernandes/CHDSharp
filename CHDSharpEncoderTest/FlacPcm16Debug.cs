using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>Regression pin: pcm16/flac must be byte-identical to chdman (battle check
/// `pcm16 x flac encode byte-identical`, fixed 2026-08-21).</summary>
/// <remarks>
/// Root causes (both required):
/// 1. <c>FlacLpcMath.ComputeAutocorrelation</c> used a backward loop with fused multiply-add,
///    but chdman's dispatch (<c>FLAC__lpc_compute_autocorrelation_intrin_fma_lag_16</c>, and the
///    scalar fallback alike) is plain double mul+add in ascending sample order
///    (deduplication/lpc_compute_autocorrelation_intrin.c).
/// 2. libFLAC 1.4.3's <c>apply_apodization_</c> copies/subtracts only <c>max_lpc_order</c>
///    (12, not 13) autocorrelation entries for subdivide_tukey root/punchout windows, so
///    <c>autoc[12]</c> keeps the preceding partial window's value when Levinson-Durbin runs.
/// </remarks>
public class FlacPcm16Debug : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flac_dbg_" + Guid.NewGuid().ToString("N"));

    public FlacPcm16Debug() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Pcm16_Flac_ByteIdenticalToChdman()
    {
        // battle pcm16 corpus (seed 1337) — replicate TestDataGenerator.Pcm16 exactly
        var source = Pcm16(512 * 1024, 1337);

        var srcPath = Path.Combine(_dir, "pcm16.bin");
        var oursPath = Path.Combine(_dir, "pcm16.ours.chd");
        var refPath = Path.Combine(_dir, "pcm16.ref.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, oursPath, 4096, 512, [CodecTags.Flac]);
        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", refPath, "-c", "flac", "-hs", "4096", "-us", "512", "-f");
        Assert.True(createExit == 0, $"chdman createraw failed\n{cOut}{cErr}");

        var ours = ChdFile.Open(oursPath, out var oFile);
        var refs = ChdFile.Open(refPath, out var rFile);
        Assert.Equal(ChdError.Chderrnone, ours);
        Assert.Equal(ChdError.Chderrnone, refs);

        var firstDiff = -1;
        for (uint h = 0; h < oFile!.HunkCount; h++)
        {
            var oRaw = oFile.ReadRawHunk(h);
            var rRaw = rFile!.ReadRawHunk(h);
            if (oRaw == null || rRaw == null) continue;
            if (!oRaw.AsSpan().SequenceEqual(rRaw))
            {
                firstDiff = (int)h;
                break;
            }
        }
        Assert.True(firstDiff < 0, $"pcm16 flac diverges from chdman at hunk {firstDiff}");
    }

    private static byte[] Pcm16(int size, int seed)
    {
        var rng = new Random(seed);
        var samples = size / 2;
        var b = new byte[samples * 2];
        var freq = 220 + rng.NextDouble() * 200;
        double phase = 0;
        for (var i = 0; i < samples; i++)
        {
            if (i % 4096 == 0) freq = 180 + rng.NextDouble() * 1200;
            phase += 2 * Math.PI * freq / 44100.0;
            var sample = (short)(Math.Sin(phase) * 11000 + (rng.NextDouble() - 0.5) * 400);
            b[i * 2] = (byte)sample;
            b[i * 2 + 1] = (byte)(sample >> 8);
        }
        return b;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunChdman(params string[] args)
    {
        var chdmanPath = ChdmanPath ?? throw new InvalidOperationException("chdman.exe not available");
        var psi = new ProcessStartInfo
        {
            FileName = chdmanPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, tOut.Result, tErr.Result);
    }

    private static string? ResolveChdmanPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate)) return candidate;
        candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName));
        if (File.Exists(candidate)) return candidate;
        return null;
    }
}
