using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Tests for libchdr parity features: the public metadata query API
/// (chd_get_metadata), Precache (chd_precache), V1/V2 synthesized GDDD
/// metadata, and metadata flags exposure.
/// </summary>
public class ParityFeaturesTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    // ── GetMetadata (libchdr chd_get_metadata parity) ──

    [Fact]
    public void GetMetadata_by_tag_returns_first_match()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var gErr = chd!.GetMetadata("CHT2", 0, out var entry);
            Assert.Equal(ChdError.Chderrnone, gErr);
            Assert.NotNull(entry);
            Assert.Equal("CHT2", entry.Tag);
            Assert.StartsWith("TRACK:1", entry.GetText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GetMetadata_by_index_returns_nth_match()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var gErr = chd!.GetMetadata("CHT2", 1, out var entry);
            Assert.Equal(ChdError.Chderrnone, gErr);
            Assert.NotNull(entry);
            Assert.Equal("CHT2", entry.Tag);
            Assert.StartsWith("TRACK:2", entry.GetText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GetMetadata_wildcard_returns_any_tag()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var gErr = chd!.GetMetadata(null, 0, out var entry);
            Assert.Equal(ChdError.Chderrnone, gErr);
            Assert.NotNull(entry);
            Assert.Equal("CHT2", entry.Tag);

            var gErr2 = chd.GetMetadata(string.Empty, 0, out _);
            Assert.Equal(ChdError.Chderrnone, gErr2);
        }
    }

    [Fact]
    public void GetMetadata_missing_tag_returns_metadatanotfound()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var gErr = chd!.GetMetadata("ZZZZ", 0, out var entry);
            Assert.Equal(ChdError.Chderrmetadatanotfound, gErr);
            Assert.Null(entry);
        }
    }

    [Fact]
    public void GetMetadata_index_out_of_range_returns_metadatanotfound()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var gErr = chd!.GetMetadata("CHT2", 5, out var entry);
            Assert.Equal(ChdError.Chderrmetadatanotfound, gErr);
            Assert.Null(entry);
        }
    }

    [Fact]
    public void GetMetadata_on_file_without_metadata_returns_metadatanotfound()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Empty(chd!.Metadata);
            var gErr = chd.GetMetadata("GDDD", 0, out var entry);
            Assert.Equal(ChdError.Chderrmetadatanotfound, gErr);
            Assert.Null(entry);
        }
    }

    [Fact]
    public void GetMetadata_matches_metadata_collection()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var list = chd!.Metadata;
            foreach (var tag in list.Select(e => e.Tag).Distinct(StringComparer.Ordinal))
            {
                var occurrences = list.Where(e => string.Equals(e.Tag, tag, StringComparison.Ordinal)).ToList();
                for (var i = 0; i < occurrences.Count; i++)
                {
                    var gErr = chd.GetMetadata(tag, (uint)i, out var entry);
                    Assert.Equal(ChdError.Chderrnone, gErr);
                    Assert.NotNull(entry);
                    Assert.Equal(occurrences[i].Tag, entry.Tag);
                    Assert.Same(occurrences[i].Data, entry.Data);
                }
            }
        }
    }

    [Fact]
    public void Metadata_entries_expose_flags()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            foreach (var entry in chd!.Metadata)
            {
                // Flag bit 0 = checksummed. Whatever the stored value, the
                // property must round-trip from the entry header.
                Assert.True(entry.Flags is 0 or 1);
            }
        }
    }

    // ── Precache (libchdr chd_precache parity) ──

    [Fact]
    public void Precache_then_read_returns_same_bytes()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        // reference: plain read
        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        byte[] expected;
        using (chd)
        {
            var buffer = new byte[chd!.HunkBytes];
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(0, buffer));
            expected = (byte[])buffer.Clone();
        }

        // precached read must be identical
        err = ChdFile.Open(path, out var chd2);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd2)
        {
            Assert.Equal(ChdError.Chderrnone, chd2!.Precache());
            var buffer = new byte[chd2.HunkBytes];
            Assert.Equal(ChdError.Chderrnone, chd2.ReadHunk(0, buffer));
            Assert.Equal(expected, buffer);

            // random-access Read over several hunks
            var data = new byte[chd2.HunkBytes * 3];
            Assert.Equal(ChdError.Chderrnone, chd2.Read(0, data, 0, data.Length));
            Assert.Equal(expected, data[..(int)chd2.HunkBytes]);
        }
    }

    [Fact]
    public void Precache_is_idempotent()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrnone, chd!.Precache());
            Assert.Equal(ChdError.Chderrnone, chd.Precache());
            Assert.Equal(ChdError.Chderrnone, chd.Precache());
        }
    }

    [Fact]
    public void Open_with_failing_read_stream_returns_readerror()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        using var failing = new ReadThrowingStream(fs);
        var err2 = ChdFile.Open(failing, true, out var chd2);
        Assert.Equal(ChdError.Chderrreaderror, err2);
        Assert.Null(chd2);
    }

    [Fact]
    public void Precache_on_failing_read_stream_returns_readerror()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        using var failing = new PartialReadFailingStream(fs, 16384); // header+map reads OK, bulk read fails
        var err = ChdFile.Open(failing, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            Assert.Equal(ChdError.Chderrreaderror, chd!.Precache());
        }
    }

    // ── V1/V2 synthesized GDDD (libchdr parity) ──

    [Fact]
    public void V1_chd_has_synthesized_gddd_metadata()
    {
        var path = Path.Combine(TestDataDir, "v1_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var gErr = chd!.GetMetadata("GDDD", 0, out var entry);
            Assert.Equal(ChdError.Chderrnone, gErr);
            Assert.NotNull(entry);
            Assert.Equal("GDDD", entry.Tag);
            Assert.Equal("CYLS:16,HEADS:4,SECS:16,BPS:512", entry.GetText());
            Assert.Equal(512u, chd.UnitBytes);
            Assert.True(chd.IsHdd);
        }
    }

    [Fact]
    public void V2_chd_has_synthesized_gddd_metadata()
    {
        var path = Path.Combine(TestDataDir, "v2_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var gErr = chd!.GetMetadata("GDDD", 0, out var entry);
            Assert.Equal(ChdError.Chderrnone, gErr);
            Assert.NotNull(entry);
            Assert.Equal("GDDD", entry.Tag);
            Assert.StartsWith("CYLS:", entry.GetText(), StringComparison.Ordinal);
            Assert.Contains("BPS:512", entry.GetText(), StringComparison.Ordinal);
            Assert.Equal(512u, chd.UnitBytes);
        }
    }

    [Fact]
    public void V1_synthesized_gddd_uses_header_geometry()
    {
        var path = Path.Combine(TestDataDir, "v1_zlib.chd");
        if (!File.Exists(path)) return;

        var err = ChdFile.Open(path, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        using (chd)
        {
            var text = chd!.GetMetadata("GDDD", 0, out var entry) == ChdError.Chderrnone
                ? entry!.GetText()
                : string.Empty;
            var bps = ParseBps(text);
            Assert.Equal(512u, bps);
            Assert.Equal(bps, chd.UnitBytes);
        }
    }

    private static uint ParseBps(string gddd)
    {
        foreach (var part in gddd.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("BPS:", StringComparison.Ordinal) &&
                uint.TryParse(trimmed.AsSpan(4), out var bps))
                return bps;
        }

        return 0;
    }

    // ── OpenAsync stream+parent overload ──

    [Fact]
    public async Task OpenAsync_stream_with_parent_overload_compiles_and_works()
    {
        var childPath = Path.Combine(TestDataDir, "v5_child.chd");
        var parentPath = Path.Combine(TestDataDir, "v5_parent.chd");
        if (!File.Exists(childPath) || !File.Exists(parentPath)) return;

        // standalone stream open of a child must demand a parent
        await using var fs = File.OpenRead(childPath);
        var (err, chd) = await ChdFile.OpenAsync(fs, true);
        Assert.Equal(ChdError.Chderrrequiresparent, err);
        chd?.Dispose();

        // stream open with an external parent must succeed
        var pErr = ChdFile.Open(parentPath, out var parent);
        Assert.Equal(ChdError.Chderrnone, pErr);
        await using (parent)
        {
            await using var fs2 = File.OpenRead(childPath);
            var (err2, chd2) = await ChdFile.OpenAsync(fs2, true, parent);
            Assert.Equal(ChdError.Chderrnone, err2);
            chd2?.Dispose();
        }
    }

    /// <summary>Wraps a seekable stream whose reads fail, for open error-path testing.</summary>
    private sealed class ReadThrowingStream : Stream
    {
        private readonly Stream _inner;

        public ReadThrowingStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("simulated read failure");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Wraps a seekable stream that allows only the first <paramref name="budget"/> bytes to be read; later reads throw.</summary>
    private sealed class PartialReadFailingStream : Stream
    {
        private readonly Stream _inner;
        private long _budget;

        public PartialReadFailingStream(Stream inner, long budget)
        {
            _inner = inner;
            _budget = budget;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_budget <= 0)
                throw new IOException("simulated read failure");

            var n = _inner.Read(buffer, offset, (int)Math.Min(count, _budget));
            _budget -= n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
