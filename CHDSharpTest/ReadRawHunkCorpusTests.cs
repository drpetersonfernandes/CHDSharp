using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Corpus-level invariants for <see cref="ChdFile.ReadRawHunk"/> (raw on-disk hunk access):
/// raw bytes match the decompressed data for uncompressed maps, and parent/zero-fill hunks
/// have no on-disk data.
/// </summary>
[Collection("TestData")]
public sealed class ReadRawHunkCorpusTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void UncompressedMap_RawEqualsDecompressed()
    {
        // v5_none.chd uses the V5 uncompressed map: every hunk is stored raw at
        // hunkIndex * HunkBytes — except all-zero hunks, which are unallocated
        // (offset word 0) and read as zero-fill with no on-disk data
        var path = Path.Combine(TestDataDir, "v5_none.chd");
        var err = ChdFile.Open(path, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            var hunk = new byte[file!.HunkBytes];
            var zeroFillHunks = 0;
            for (uint h = 0; h < file.HunkCount; h++)
            {
                var raw = file.ReadRawHunk(h);
                Assert.Equal(ChdError.Chderrnone, file.ReadHunk(h, hunk));

                if (raw == null)
                {
                    zeroFillHunks++;
                    Assert.True(hunk.All(b => b == 0), $"hunk {h} has no on-disk data but is not zero-filled");
                    continue;
                }

                Assert.Equal(hunk, raw);
            }

            Assert.True(zeroFillHunks > 0, "expected at least one zero-fill hunk in the uncompressed map");
        }
    }

    [Fact]
    public void CompressedMap_NoRawDataForParentReferences()
    {
        // v3_child.chd is a differential CHD whose map contains parent references; such
        // hunks must report no on-disk data, yet still decompress through the parent
        var childPath = Path.Combine(TestDataDir, "v3_child.chd");
        var parentPath = Path.Combine(TestDataDir, "v3_zlib.chd");

        var err = ChdFile.Open(childPath, parentPath, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            var hunk = new byte[file!.HunkBytes];
            var parentReferenced = 0;
            for (uint h = 0; h < file.HunkCount; h++)
            {
                var raw = file.ReadRawHunk(h);
                if (raw == null)
                {
                    parentReferenced++;
                }

                Assert.Equal(ChdError.Chderrnone, file.ReadHunk(h, hunk));
            }

            Assert.True(parentReferenced > 0, "expected at least one parent-referenced hunk");
        }
    }

    [Fact]
    public void RawHunk_MatchesDecompressed_WhereStoredRaw()
    {
        // v1_zlib.chd has a legacy map mixing none/self/type0 entries: hunks stored raw
        // (COMPRESSION_NONE) must satisfy ReadRawHunk == ReadHunk; every hunk must either
        // return raw bytes or still decompress (SELF resolves to a stored hunk)
        var path = Path.Combine(TestDataDir, "v1_zlib.chd");
        var err = ChdFile.Open(path, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            var hunk = new byte[file!.HunkBytes];
            var rawCount = 0;
            for (uint h = 0; h < file.HunkCount; h++)
            {
                var raw = file.ReadRawHunk(h);
                Assert.Equal(ChdError.Chderrnone, file.ReadHunk(h, hunk));

                if (raw is { Length: > 0 })
                {
                    rawCount++;
                    if (raw.Length == file.HunkBytes)
                        Assert.Equal(hunk, raw);
                }
            }

            Assert.True(rawCount > 0, "expected at least one hunk with on-disk data");
        }
    }

    [Fact]
    public void RawHunk_AfterPrecache_Matches()
    {
        var path = Path.Combine(TestDataDir, "v5_multi.chd");
        var err = ChdFile.Open(path, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        using (file)
        {
            var before = file!.ReadRawHunk(0)!;
            Assert.NotNull(before);
            Assert.Equal(ChdError.Chderrnone, file.Precache());
            Assert.Equal(before, file.ReadRawHunk(0));
        }
    }

    [Fact]
    public async Task RawHunk_AsyncMatchesSync_OnCorpus()
    {
        var path = Path.Combine(TestDataDir, "v3_zlib.chd");
        var err = ChdFile.Open(path, out var file);
        Assert.Equal(ChdError.Chderrnone, err);
        await using (file)
        {
            for (uint h = 0; h < file!.HunkCount; h++)
            {
                var sync = file.ReadRawHunk(h);
                var async = await file.ReadRawHunkAsync(h);
                Assert.Equal(sync, async);
            }
        }
    }
}