using System.Security.Cryptography;
using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>Prevents parallel execution of integration tests that share chdman and file resources.</summary>
[CollectionDefinition("ChdListIntegration", DisableParallelization = true)]
public class ChdListIntegrationFixture;

/// <summary>
/// Data-driven integration tests over real CHD files (any version/codec),
/// cross-checked against chdman.exe. Runs sequentially per file to avoid
/// ThreadPool starvation from parallel decompression.
/// </summary>
[Collection("ChdListIntegration")]
public class ChdListIntegrationTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>Initializes a new instance of the <see cref="ChdListIntegrationTests"/> class.</summary>
    /// <param name="output">The xUnit test output helper for logging.</param>
    public ChdListIntegrationTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>Full verification of one CHD file: header info vs chdman, deep CheckFile,
    /// chdman verify, and full-image read SHA1 comparison.</summary>
    /// <param name="path">The path to the CHD file to verify.</param>
    [Theory]
    [MemberData(nameof(ChdListData.Paths), MemberType = typeof(ChdListData))]
    public void FullVerificationMatchesChdman(string path)
    {
        if (path == null)
            Assert.Skip("No CHD files found");
        if (!File.Exists(path))
            Assert.Skip($"CHD file not present: {path}");

        var name = Path.GetFileName(path);
        _out.WriteLine(string.Empty);
        _out.WriteLine($"=== {name} ===");

        if (TestPaths.ChdmanAvailable)
            VerifyHeaderInfo(path, name);

        VerifyCheckFile(path, name);

        if (TestPaths.ChdmanAvailable)
            VerifyChdmanAgrees(path, name);

        VerifyFullReadSha1(path, name);

        _out.WriteLine($"=== {name}: PASSED ===");
    }

    /// <summary>
    /// Random-access test: reads arbitrary byte ranges via CHDSharpLib.Read() and
    /// compares byte-for-byte against chdman extractraw. Limited to files
    /// under 1 GB to keep runtime reasonable.
    /// </summary>
    /// <param name="path">The path to the CHD file to test.</param>
    [Theory]
    [MemberData(nameof(ChdListData.SmallPaths), MemberType = typeof(ChdListData))]
    public void RandomAccessMatchesChdmanExtract(string path)
    {
        if (path == null)
            Assert.Skip("No small CHD files found");
        if (!File.Exists(path))
            Assert.Skip($"CHD file not present: {path}");
        if (!TestPaths.ChdmanAvailable)
            Assert.Skip("chdman.exe not available");

        var name = Path.GetFileName(path);
        _out.WriteLine(string.Empty);
        _out.WriteLine($"=== RandomAccess: {name} ===");

        var open = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, open);
        using (chd)
        {
            if (chd != null)
            {
                ulong hb = chd.HunkBytes;
                var total = chd.TotalBytes;
                var hunkCount = chd.HunkCount;

                if (hunkCount < 2)
                    Assert.Skip("Need at least 2 hunks");

                // Build a set of test ranges covering key access patterns.
                (ulong offset, int length, string desc)[] ranges =
                [
                    (0, Math.Min((int)hb, (int)total), "first hunk"),
                    (hb, Math.Min((int)hb, (int)(total - hb)), "second hunk"),
                    (hb / 2, (int)hb, "cross-hunk boundary"),
                    (total - Math.Min((ulong)hb, total), (int)Math.Min((ulong)hb, total), "last hunk"),
                    (17, 97, "small unaligned range"),
                    (hb / 3, 7, "tiny unaligned range"),
                    (total - 50 > 0 ? total - 50 : 0, 37, "near end")
                ];

                foreach (var (offset, length, desc) in ranges)
                {
                    if (offset + (ulong)length > total)
                        continue;

                    var chdmanBytes = Chdman.ExtractRaw(path, offset, (ulong)length);
                    Assert.NotNull(chdmanBytes);
                    Assert.Equal(length, chdmanBytes.Length);

                    var csharpBytes = new byte[length];
                    var err = chd.Read(offset, csharpBytes, 0, length);
                    Assert.Equal(ChdError.Chderrnone, err);

                    Assert.True(chdmanBytes.AsSpan().SequenceEqual(csharpBytes),
                        $"Mismatch at offset {offset}, len {length} ({desc}): {name}");

                    _out.WriteLine($"  offset={offset,10} len={length,5} {desc,-25} OK");
                }
            }
        }

        _out.WriteLine($"=== RandomAccess: {name}: PASSED ===");
    }

    private void VerifyHeaderInfo(string path, string name)
    {
        var info = Chdman.GetInfo(path);
        if (info == null)
            Assert.Skip("chdman info failed");

        var open = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, open);
        using (chd)
        {
            if (chd != null)
            {
                Assert.Equal((uint)info.Version, chd.Version);
                Assert.Equal(info.HunkBytes, chd.HunkBytes);
                Assert.Equal(info.LogicalBytes, chd.TotalBytes);
                Assert.Equal(info.TotalHunks, chd.HunkCount);

                if (info.Sha1 != null)
                    Assert.Equal(info.Sha1, HashUtil.ToHex(chd.Sha1));
                if (info.DataSha1 != null)
                    Assert.Equal(info.DataSha1, HashUtil.ToHex(chd.RawSha1));
            }
        }

        _out.WriteLine($"  Header: V{info.Version}, {info.LogicalBytes} bytes, SHA1={info.Sha1}, DataSHA1={info.DataSha1}");
    }

    private static void VerifyCheckFile(string path, string name)
    {
        using Stream s = File.OpenRead(path);
        var err = Chd.CheckFile(s, name, true,
            out var version, out _, out _);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(version);
    }

    private void VerifyChdmanAgrees(string path, string name)
    {
        var chdmanOk = Chdman.Verify(path);
        Assert.True(chdmanOk, $"chdman verify failed for {name}");
        _out.WriteLine("  chdman verify: OK");
    }

    private void VerifyFullReadSha1(string path, string name)
    {
        var open = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, open);
        using (chd)
        {
            if (chd != null)
            {
                var rawSha1 = chd.RawSha1;
                if (rawSha1 == null || HashUtil.IsAllZero(rawSha1))
                {
                    _out.WriteLine("  FullRead SHA1: skipped (no raw SHA1 in V1/V2 header)");
                    return;
                }

                _out.WriteLine($"  FullRead SHA1 ({chd.TotalBytes} bytes)...");
                var computed = ComputeFullImageSha1(chd, _out);
                _out.WriteLine($"  FullRead SHA1: expected={HashUtil.ToHex(rawSha1)} computed={computed}");
                Assert.Equal(HashUtil.ToHex(rawSha1), computed);
            }
        }
    }

    internal static string ComputeFullImageSha1(ChdFile chd, ITestOutputHelper? @out = null)
    {
        using var sha1 = SHA1.Create();
        var buf = new byte[chd.HunkBytes];
        var remaining = chd.TotalBytes;
        ulong offset = 0;
        var nextReport = chd.TotalBytes > 0 ? chd.TotalBytes / 10 : 0;

        while (remaining > 0)
        {
            var chunk = (int)Math.Min((ulong)buf.Length, remaining);
            var err = chd.Read(offset, buf, 0, chunk);
            Assert.Equal(ChdError.Chderrnone, err);
            sha1.TransformBlock(buf, 0, chunk, null, 0);
            offset += (ulong)chunk;
            remaining -= (ulong)chunk;

            if (@out != null && offset >= nextReport)
            {
                var pct = chd.TotalBytes > 0 ? (int)(offset * 100 / chd.TotalBytes) : 100;
                @out.WriteLine($"    {pct}% ({offset}/{chd.TotalBytes} bytes)");
                nextReport = offset + (chd.TotalBytes / 10);
            }
        }

        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return HashUtil.ToHex(sha1.Hash!)!;
    }
}
