using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Builds a parent/child (and a wrong-parent) CHD set once via chdman, shared by
/// all tests in <see cref="ParentChainTests"/>. Skips everything if chdman or a
/// suitable source CHD is unavailable.
/// </summary>
public sealed class ParentChainFixture : IDisposable
{
    public string TempDir { get; } = null!;
    public string ParentPath { get; } = null!;
    public string ChildPath { get; } = null!;
    public string WrongParentPath { get; } = null!;
    public bool HasWrongParent { get; private set; }
    public string SourceRawSha1 { get; } = null!;
    public bool Ready { get; }
    public string SkipReason { get; } = null!;

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
            .Select(static p => new FileInfo(p))
            .OrderBy(static fi => fi.Length)
            .Select(static fi => fi.FullName)
            .ToList();

        var source = candidates.FirstOrDefault(static p =>
        {
            var e = ChdFile.Open(p, out var c);
            if (e != ChdError.Chderrnone) return false;

            using (c)
            {
                return c != null && c.HunkBytes % 2448 == 0; // CD frame multiple
            }
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
        var rChild = Chdman.CopyVerbose(source, ChildPath, "cdzl,cdfl", ParentPath);
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
        if (ChdFile.Open(source, out var src) == ChdError.Chderrnone)
        {
            using (src)
            {
                if (src != null)
                {
                    SourceRawSha1 = HashUtil.ToHex(src.RawSha1);
                }
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
            Assert.Skip(_fx.SkipReason);
    }

    [Fact]
    public void ChildWithoutParentRequiresParent()
    {
        RequireReady();
        var err = ChdFile.Open(_fx.ChildPath, out var chd);
        chd?.Dispose();
        Assert.Equal(ChdError.Chderrrequiresparent, err);
    }

    [Fact]
    public void ChildWithCorrectParentOpens()
    {
        RequireReady();
        var err = ChdFile.Open(_fx.ChildPath, _fx.ParentPath, out var chd);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, err);
        }
    }

    [Fact]
    public void ChildWithWrongParentReturnsInvalidParent()
    {
        RequireReady();
        if (!_fx.HasWrongParent)
            Assert.Skip("no alternate CHD available to use as a wrong parent");
        var err = ChdFile.Open(_fx.ChildPath, _fx.WrongParentPath, out var chd);
        chd?.Dispose();
        Assert.Equal(ChdError.Chderrinvalidparent, err);
    }

    [Fact]
    public void ChildFullReadMatchesSourceRawSha1()
    {
        RequireReady();
        var err = ChdFile.Open(_fx.ChildPath, _fx.ParentPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            if (chd != null)
            {
                var computed = ChdListIntegrationTests.ComputeFullImageSha1(chd);
                Assert.Equal(_fx.SourceRawSha1, computed);
            }
        }
    }

    [Fact]
    public void ChildCheckFileWithParentSucceeds()
    {
        RequireReady();
        var err = Chd.CheckFileWithParent(_fx.ChildPath, _fx.ParentPath,
            out var version, out _, out _);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(version);
    }

    [Fact]
    public void ChdmanAgreesChildVerifiesWithParent()
    {
        RequireReady();
        Assert.True(Chdman.Verify(_fx.ChildPath, _fx.ParentPath));
    }
}
