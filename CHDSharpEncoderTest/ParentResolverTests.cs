using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

public class ParentResolverTests : IDisposable
{
    private readonly string _dir;

    public ParentResolverTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ParentResolverTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Open_WithResolver_ResolvesParentOnFirstRead()
    {
        // 64 hunks: most identical to parent, hunks 20..39 replaced with new data.
        // This ensures parent-referenced hunks exist in the child.
        var parentData = CreateTestFile(4096 * 64, 11);
        var childData = (byte[])parentData.Clone();
        for (var h = 20; h < 40; h++)
        {
            var rng = new Random(100 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        // Open the child with a resolver.
        var resolverCalled = false;
        ParentResolver resolver = (sha1, md5) =>
        {
            resolverCalled = true;
            var perr = ChdFile.Open(parentPath, out var parentChd);
            return perr == ChdError.Chderrnone ? parentChd : null;
        };

        var err = ChdFile.Open(childPath, resolver, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);

        using (chd)
        {
            Assert.False(resolverCalled, "Resolver should not be called at open time");

            // Read hunks until we hit a parent-referenced one.
            // Hunks 0..19 should be parent-referenced (identical to parent).
            var buffer = new byte[chd!.HunkBytes];
            var readErr = chd.ReadHunk(0, buffer);
            Assert.Equal(ChdError.Chderrnone, readErr);
            Assert.True(resolverCalled, "Resolver should be called on first parent hunk read");

            // Verify data round-trips correctly.
            Assert.Equal(parentData.AsSpan(0, 4096).ToArray(), buffer);
        }
    }

    [Fact]
    public void Open_WithResolver_CachesResolvedParent()
    {
        var parentData = CreateTestFile(4096 * 64, 22);
        var childData = (byte[])parentData.Clone();
        for (var h = 20; h < 40; h++)
        {
            var rng = new Random(200 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        var resolverCallCount = 0;
        ParentResolver resolver = (sha1, md5) =>
        {
            resolverCallCount++;
            var perr = ChdFile.Open(parentPath, out var parentChd);
            return perr == ChdError.Chderrnone ? parentChd : null;
        };

        var err = ChdFile.Open(childPath, resolver, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);

        using (chd)
        {
            // Read multiple parent hunks (0..19 are parent-referenced).
            var buffer = new byte[chd!.HunkBytes];
            for (uint i = 0; i < 10; i++)
            {
                chd.ReadHunk(i, buffer);
            }

            Assert.True(resolverCallCount == 1, "Resolver should be called only once (cached)");
        }
    }

    [Fact]
    public void Open_WithResolver_ReturnsRequiresParentWhenResolverReturnsNull()
    {
        var parentData = CreateTestFile(4096 * 64, 33);
        var childData = (byte[])parentData.Clone();

        var parentPath = Path.Combine(_dir, "parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        ParentResolver resolver = (sha1, md5) => null;

        var err = ChdFile.Open(childPath, resolver, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);

        using (chd)
        {
            var buffer = new byte[chd!.HunkBytes];
            var readErr = chd.ReadHunk(0, buffer);
            Assert.Equal(ChdError.Chderrrequiresparent, readErr);
        }
    }

    [Fact]
    public void Open_WithResolver_ReturnsInvalidParentWhenHashesMismatch()
    {
        var parentData = CreateTestFile(4096 * 64, 44);
        var wrongParentData = CreateTestFile(4096 * 64, 55);
        var childData = (byte[])parentData.Clone();
        for (var h = 20; h < 40; h++)
        {
            var rng = new Random(300 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "parent.chd");
        var wrongParentPath = Path.Combine(_dir, "wrong_parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(wrongParentData))
        {
            ChdEncoder.EncodeRaw(ms, wrongParentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        ParentResolver resolver = (sha1, md5) =>
        {
            var perr = ChdFile.Open(wrongParentPath, out var parentChd);
            return perr == ChdError.Chderrnone ? parentChd : null;
        };

        var err = ChdFile.Open(childPath, resolver, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);

        using (chd)
        {
            var buffer = new byte[chd!.HunkBytes];
            var readErr = chd.ReadHunk(0, buffer);
            Assert.Equal(ChdError.Chderrinvalidparent, readErr);
        }
    }

    [Fact]
    public void Open_WithNullResolver_FailsOnChildChd()
    {
        var parentData = CreateTestFile(4096 * 64, 55);
        var childData = (byte[])parentData.Clone();

        var parentPath = Path.Combine(_dir, "parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        var err = ChdFile.Open(childPath, (ParentResolver?)null, out var chd);
        Assert.Equal(ChdError.Chderrrequiresparent, err);
        Assert.Null(chd);
    }

    [Fact]
    public void CheckFileWithParent_WithResolver_VerifiesSuccessfully()
    {
        var parentData = CreateTestFile(4096 * 64, 66);
        var childData = (byte[])parentData.Clone();
        for (var h = 20; h < 40; h++)
        {
            var rng = new Random(400 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        ParentResolver resolver = (sha1, md5) =>
        {
            var perr = ChdFile.Open(parentPath, out var parentChd);
            return perr == ChdError.Chderrnone ? parentChd : null;
        };

        var result = Chd.CheckFileWithParent(childPath, resolver);
        Assert.Equal(ChdError.Chderrnone, result.Error);
    }

    [Fact]
    public void Open_WithResolver_ReceivesCorrectHashes()
    {
        var parentData = CreateTestFile(4096 * 64, 77);
        var childData = (byte[])parentData.Clone();
        for (var h = 20; h < 40; h++)
        {
            var rng = new Random(500 + h);
            rng.NextBytes(childData.AsSpan(h * 4096, 4096));
        }

        var parentPath = Path.Combine(_dir, "parent.chd");
        var childPath = Path.Combine(_dir, "child.chd");

        using (var ms = new MemoryStream(parentData))
        {
            ChdEncoder.EncodeRaw(ms, parentPath, 4096, 512);
        }

        using (var ms = new MemoryStream(childData))
        {
            ChdEncoder.EncodeRaw(ms, childPath, 4096, 512, null, new ChdEncodeOptions { ParentPath = parentPath });
        }

        byte[]? capturedSha1 = null;
        ParentResolver resolver = (sha1, md5) =>
        {
            capturedSha1 = sha1;
            var perr = ChdFile.Open(parentPath, out var parentChd);
            return perr == ChdError.Chderrnone ? parentChd : null;
        };

        var err = ChdFile.Open(childPath, resolver, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.NotNull(chd);

        using (chd)
        {
            var buffer = new byte[chd!.HunkBytes];
            chd.ReadHunk(0, buffer);

            Assert.NotNull(capturedSha1);
            Assert.Contains(capturedSha1, b => b != 0);
        }
    }

    private static byte[] CreateTestFile(int size, byte seed)
    {
        var data = new byte[size];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
    }
}
