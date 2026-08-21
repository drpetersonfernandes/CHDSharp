using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Pins the map-encoding edge case from the sixth battle run: a compressed codec storing an
/// individual hunk uncompressed (COMPRESSION_NONE entry inside a compressed CHD's Huffman map)
/// at small hunk counts.
///
/// Root cause found while closing that gap: MAME's <c>compress_v5_map</c> sizes its map
/// bitstream buffer as <c>nbits_needed/8 + 1</c> bytes <em>including</em> the 16-byte map
/// header. For small hunk counts that area is smaller than the actual tree + symbols +
/// auxiliary data, so MAME's <c>bitstream_out</c> silently drops whole trailing bytes (the
/// zero-filled allocation shows through) while <c>flush()</c> keeps counting them in the
/// map's compressed-length field. When a dropped byte would have been nonzero, the stored map
/// no longer matches its header CRC-16 and the resulting CHD cannot be re-opened — not even
/// by chdman itself (upstream bug, reproducible with a single-hunk <c>createraw</c> at hunk
/// sizes 18816/19584/65536).
///
/// Our encoder replicates chdman's allocation and clipping so outputs stay byte-identical
/// wherever chdman's file is well-formed, and falls back to the full bitstream when clipping
/// would corrupt the map (chdman's reference is unreadable in those cases, so there are no
/// valid reference bytes to match).
/// </summary>
public class MapClippingChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public MapClippingChdmanValidationTests()
    {
        _testDataDir = Path.Combine(Path.GetTempPath(), "map_clipping_tests_" + Guid.NewGuid().ToString("N"));
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

    // ----- unit tests on MapCompressor (no chdman required) -----

    [Fact]
    public void SingleNoneEntry_benignClip_countsDroppedZeroByteLikeChdman()
    {
        // hunkBytes=8192 -> lengthbits=14 -> nbits_needed/8+1 = 22 bytes total, i.e. a
        // 6-byte payload area, while the true bitstream needs 7 (tree 32 bits + type symbol
        // 1 bit + CRC-16 16 bits). The dropped final byte holds the CRC's LSB and padding;
        // with an even CRC it is zero, so chdman's clipped output equals the full stream and
        // complen still counts the phantom byte.
        var entries = new[]
        {
            new MapEntry { Compression = MapEntry.CompressionNone, CompLength = 8192, Offset = 124, Crc16 = 0x0000 }
        };

        var map = MapCompressor.Compress(entries, 1, 8192, 512);

        Assert.Equal(23, map.Length);
        Assert.Equal(7u, ReadU32Be(map, 0)); // complen includes the clipped (zero) byte
        Assert.Equal(14, map[12]); // lengthbits
        Assert.Equal(0x00, map[22]); // the dropped byte reads back as zero, like chdman's
    }

    [Fact]
    public void SingleNoneEntry_corruptClip_fallsBackToFullVerifiableBitstream()
    {
        // Same geometry as above but with the CRC's LSB set: chdman would drop the final
        // byte (value 0x80), producing a map whose decoded CRC-16 no longer matches its
        // header — unreadable by chdman itself. We must emit the full bitstream instead.
        var entries = new[]
        {
            new MapEntry { Compression = MapEntry.CompressionNone, CompLength = 8192, Offset = 124, Crc16 = 0x0001 }
        };

        var map = MapCompressor.Compress(entries, 1, 8192, 512);

        Assert.Equal(23, map.Length);
        Assert.Equal(7u, ReadU32Be(map, 0));
        Assert.Equal(0x80, map[22]); // the true final byte survives
    }

    // Whether chdman's single-hunk clip is benign depends solely on the parity of the
    // hunk's CRC-16 (the dropped byte holds that LSB plus zero padding), so each case pins
    // a corpus seed chosen to exercise the intended side deterministically.
    private static int SeedFor(uint hunkBytes, bool oddCrc) => (hunkBytes, oddCrc) switch
    {
        (4096u, false) => 1,
        (4096u, true) => 2,
        (8192u, false) => 1,
        (8192u, true) => 2,
        (18816u, false) => 1,
        (18816u, true) => 2,
        (19584u, false) => 2,
        (19584u, true) => 1,
        (37632u, false) => 2,
        (37632u, true) => 1,
        (65536u, false) => 1,
        _ => 2
    };

    // ----- end-to-end parity against chdman -----

    [Theory]
    [InlineData(4096u, 512u)]
    [InlineData(8192u, 512u)] // benign clip: chdman's file is well-formed, we match it exactly
    [InlineData(18816u, 2352u)]
    [InlineData(19584u, 2448u)]
    [InlineData(37632u, 2352u)]
    [InlineData(65536u, 512u)]
    public void SingleHunk_benignClip_byteIdenticalToChdmanAndReadable(uint hunkBytes, uint unitBytes)
    {
        if (ChdmanPath == null) return;

        var source = new byte[hunkBytes];
        new Random(SeedFor(hunkBytes, oddCrc: false)).NextBytes(source);
        var tag = $"benign-{hunkBytes}";
        var (srcPath, oursPath, refPath) = WriteSources(tag, source);

        ChdEncoder.EncodeRaw(srcPath, oursPath, hunkBytes, unitBytes, [CodecTags.Zlib]);
        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", refPath, "-c", "zlib",
            "-hs", hunkBytes.ToString(), "-us", unitBytes.ToString(), "-f");
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\n{cOut}{cErr}");

        Assert.Equal(File.ReadAllBytes(refPath), File.ReadAllBytes(oursPath));

        // both files must be readable and round-trip the source
        AssertDecodesTo(refPath, source);
        AssertDecodesTo(oursPath, source);
    }

    [Theory]
    [InlineData(4096u, 512u)]
    [InlineData(18816u, 2352u)] // the hunk sizes called out in the sixth-run notes
    [InlineData(19584u, 2448u)]
    [InlineData(65536u, 512u)]
    public void SingleHunk_corruptClip_oursStaysVerifiableWhileChdmanBreaks(uint hunkBytes, uint unitBytes)
    {
        if (ChdmanPath == null) return;

        var source = new byte[hunkBytes];
        new Random(SeedFor(hunkBytes, oddCrc: true)).NextBytes(source);
        var tag = $"corrupt-{hunkBytes}";
        var (srcPath, oursPath, refPath) = WriteSources(tag, source);

        ChdEncoder.EncodeRaw(srcPath, oursPath, hunkBytes, unitBytes, [CodecTags.Zlib]);
        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", refPath, "-c", "zlib",
            "-hs", hunkBytes.ToString(), "-us", unitBytes.ToString(), "-f");
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\n{cOut}{cErr}");

        // our output remains fully valid: chdman verifies it and it round-trips
        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", oursPath);
        Assert.True(verifyExit == 0, $"chdman verify failed on OUR output (exit={verifyExit})\n{vOut}{vErr}");
        AssertDecodesTo(oursPath, source);

        // chdman 0.289 cannot re-open its own output for these inputs (upstream
        // compress_v5_map clipping bug); pin that so a behavior change is noticed
        var (refInfoExit, _, _) = RunChdman("info", "-i", refPath);
        Assert.True(refInfoExit != 0,
            "chdman can now read its own single-hunk output - the upstream clipping bug appears to be fixed; " +
            "this divergence should be revisited and byte parity restored");

        // and our bytes deliberately differ from chdman's broken ones
        Assert.NotEqual(File.ReadAllBytes(refPath), File.ReadAllBytes(oursPath));
    }

    [Theory]
    [InlineData(65536u, 512u)]
    [InlineData(18816u, 2352u)]
    [InlineData(19584u, 2448u)]
    public void Type0EntryInsideCompressedMap_multiHunk_byteIdenticalToChdman(uint hunkBytes, uint unitBytes)
    {
        if (ChdmanPath == null) return;

        // The exact scenario from the sixth-run notes, at a realistic hunk count: mostly
        // incompressible hunks (stored COMPRESSION_NONE) with individual compressible hunks
        // (COMPRESSION_TYPE_0) mixed in. No clipping occurs at this hunk count, so both
        // encoders must agree byte-for-byte and both files must verify.
        const int hunkCount = 16;
        var source = new byte[hunkBytes * hunkCount];
        var rng = new Random(1337);
        for (var h = 0; h < hunkCount; h++)
        {
            var span = source.AsSpan(h * (int)hunkBytes, (int)hunkBytes);
            if (h % 5 != 2)
            {
                rng.NextBytes(span);
                continue;
            }

            const string phrase = "the quick brown fox jumps over the lazy dog. ";
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = (byte)phrase[i % phrase.Length];
            }
        }

        var tag = $"mixed-{hunkBytes}";
        var (srcPath, oursPath, refPath) = WriteSources(tag, source);

        ChdEncoder.EncodeRaw(srcPath, oursPath, hunkBytes, unitBytes, [CodecTags.Zlib]);
        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", refPath, "-c", "zlib",
            "-hs", hunkBytes.ToString(), "-us", unitBytes.ToString(), "-f");
        Assert.True(createExit == 0, $"chdman createraw failed (exit={createExit})\n{cOut}{cErr}");

        Assert.Equal(File.ReadAllBytes(refPath), File.ReadAllBytes(oursPath));

        var (verifyExit, vOut, vErr) = RunChdman("verify", "-i", oursPath);
        Assert.True(verifyExit == 0, $"chdman verify failed (exit={verifyExit})\n{vOut}{vErr}");
        AssertDecodesTo(oursPath, source);
    }

    // ----- helpers -----

    private (string SrcPath, string OursPath, string RefPath) WriteSources(string tag, byte[] source)
    {
        var srcPath = Path.Combine(_testDataDir, tag + ".bin");
        File.WriteAllBytes(srcPath, source);
        return (srcPath, Path.Combine(_testDataDir, tag + ".ours.chd"), Path.Combine(_testDataDir, tag + ".ref.chd"));
    }

    private static void AssertDecodesTo(string chdPath, byte[] expected)
    {
        var err = ChdFile.Open(chdPath, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
            Assert.Fail($"ChdFile.Open failed for '{chdPath}' ({err})");

        using (chd)
        {
            var buffer = new byte[chd.HunkBytes];
            for (uint h = 0; h < chd.HunkCount; h++)
            {
                var readErr = chd.ReadHunk(h, buffer);
                Assert.Equal(ChdError.Chderrnone, readErr);
                var valid = (int)Math.Min((ulong)buffer.Length, (ulong)expected.Length - h * (ulong)buffer.Length);
                Assert.Equal(expected.AsSpan((int)(h * buffer.Length), valid).ToArray(), buffer.AsSpan(0, valid).ToArray());
            }
        }
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
        var exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";

        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate))
            return candidate;

        candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName));
        if (File.Exists(candidate))
            return candidate;

        return null;
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}
