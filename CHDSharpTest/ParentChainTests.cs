using System;
using System.IO;
using System.Linq;
using CHDSharp.Models;
using Xunit;

namespace CHDSharp.Tests;

/// <summary>
/// Builds a parent/child (and a wrong-parent) CHD set once via chdman, shared by
/// all tests in <see cref="ParentChainTests"/>. Skips everything if chdman or a
/// suitable source CHD is unavailable.
/// </summary>
public sealed class ParentChainFixture : IDisposable
{
    public string TempDir { get; }
    public string ParentPath { get; }
    public string ChildPath { get; }
    public string WrongParentPath { get; }
    public bool HasWrongParent { get; private set; }
    public string SourceRawSha1 { get; }
    public bool Ready { get; }
    public string SkipReason { get; }

    public ParentChainFixture()
    {
        if (!TestPaths.ChdmanAvailable)
        {
            SkipReason = "chdman.exe not available";
            return;
        }

        // Pick the two smallest available CD-type CHDs (hunk size multiple of the
        // CD frame size) so parent/child creation is quick and cdzs-compatible.
        var candidates = ChdListData.AllPaths()
            .Where(File.Exists)
            .Select(p => new FileInfo(p))
            .OrderBy(fi => fi.Length)
            .Select(fi => fi.FullName)
            .ToList();

        var source = candidates.FirstOrDefault(p =>
        {
            var e = ChdFile.Open(p, out var c);
            if (e != chd_error.CHDERR_NONE) return false;
            using (c) return c.HunkBytes % 2448 == 0; // CD frame multiple
        });

        if (source == null)
        {
            SkipReason = "Need at least one present CD-type CHD file to build a parent/child pair";
            return;
        }

        TempDir = TestPaths.CreateTempDir();
        ParentPath = Path.Combine(TempDir, "parent.chd");
        ChildPath = Path.Combine(TempDir, "child.chd");
        WrongParentPath = Path.Combine(TempDir, "wrongparent.chd");

        // Parent = recompressed copy of source; child = same source referencing parent.
        var rParent = Chdman.CopyVerbose(source, ParentPath, "cdzl,cdfl");
        var rChild = Chdman.CopyVerbose(source, ChildPath, "cdzl,cdfl", parentOut: ParentPath);
        if (!File.Exists(ParentPath) || !File.Exists(ChildPath))
        {
            SkipReason = "chdman failed to build the parent/child set. " +
                $"parent(exit={rParent.ExitCode}) child(exit={rChild.ExitCode}). " +
                $"childErr={Truncate(rChild.All)}";
            Dispose();
            return;
        }

        // A "wrong parent" is any other source that recompresses successfully.
        // Best-effort only: the wrong-parent test skips if this could not be built.
        foreach (var other in candidates.Where(p => p != source))
        {
            if (Chdman.Copy(other, WrongParentPath, "cdzl,cdfl"))
            {
                HasWrongParent = true;
                break;
            }
        }

        // Capture the source's raw SHA1 for correctness comparison.
        if (ChdFile.Open(source, out var src) == chd_error.CHDERR_NONE)
        {
            using (src)
            {
                SourceRawSha1 = HashUtil.ToHex(src.RawSha1);
            }
        }

        Ready = true;
    }

    public void Dispose()
    {
        try { if (TempDir != null && Directory.Exists(TempDir)) Directory.Delete(TempDir, true); }
        catch { /* best effort */ }
    }

    private static string Truncate(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s.Substring(s.Length - 300) : s).Replace("\r", " ").Replace("\n", " ");
    }
}

public class ParentChainTests : IClassFixture<ParentChainFixture>
{
    private readonly ParentChainFixture _fx;
    public ParentChainTests(ParentChainFixture fx)
    {
        _fx = fx;
    }

    private void RequireReady()
    {
        if (!_fx.Ready)
            Assert.Skip(_fx.SkipReason ?? "parent/child fixture not ready");
    }

    [Fact]
    public void Child_WithoutParent_RequiresParent()
    {
        RequireReady();
        var err = ChdFile.Open(_fx.ChildPath, out var chd);
        chd?.Dispose();
        Assert.Equal(chd_error.CHDERR_REQUIRES_PARENT, err);
    }

    [Fact]
    public void Child_WithCorrectParent_Opens()
    {
        RequireReady();
        var err = ChdFile.Open(_fx.ChildPath, _fx.ParentPath, out var chd);
        using (chd)
            Assert.Equal(chd_error.CHDERR_NONE, err);
    }

    [Fact]
    public void Child_WithWrongParent_ReturnsInvalidParent()
    {
        RequireReady();
        if (!_fx.HasWrongParent)
            Assert.Skip("no alternate CHD available to use as a wrong parent");
        var err = ChdFile.Open(_fx.ChildPath, _fx.WrongParentPath, out var chd);
        chd?.Dispose();
        Assert.Equal(chd_error.CHDERR_INVALID_PARENT, err);
    }

    [Fact]
    public void Child_FullRead_MatchesSourceRawSha1()
    {
        RequireReady();
        var err = ChdFile.Open(_fx.ChildPath, _fx.ParentPath, out var chd);
        Assert.Equal(chd_error.CHDERR_NONE, err);
        using (chd)
        {
            var computed = ChdListIntegrationTests.ComputeFullImageSha1(chd);
            Assert.Equal(_fx.SourceRawSha1, computed);
        }
    }

    [Fact]
    public void Child_CheckFileWithParent_Succeeds()
    {
        RequireReady();
        var err = Chd.CheckFileWithParent(_fx.ChildPath, _fx.ParentPath,
            out var version, out _, out _);
        Assert.Equal(chd_error.CHDERR_NONE, err);
        Assert.NotNull(version);
    }

    [Fact]
    public void Chdman_Agrees_ChildVerifiesWithParent()
    {
        RequireReady();
        Assert.True(Chdman.Verify(_fx.ChildPath, _fx.ParentPath));
    }
}
