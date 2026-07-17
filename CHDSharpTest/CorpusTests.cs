using System.Text.Json;
using CHDSharp.Models;

namespace CHDSharp.Tests;

public sealed class CorpusTests
{
    public sealed record Entry(string File, uint Version, string? Parent, string Expect, string Note);

    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    private static readonly string ManifestPath =
        Path.Combine(TestDataDir, "manifest.json");

    private static List<Entry> LoadManifest()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(ManifestPath));
        return doc.RootElement.EnumerateArray()
            .Select(e => new Entry(
                e.GetProperty("file").GetString()!,
                e.GetProperty("version").GetUInt32(),
                e.GetProperty("parent").GetString() is { Length: > 0 } p ? p : null,
                e.GetProperty("expect").GetString() ?? "ok",
                e.GetProperty("note").GetString() ?? ""))
            .ToList();
    }

    public static TheoryData<Entry, string> StandaloneFiles()
    {
        var data = new TheoryData<Entry, string>();
        foreach (var e in LoadManifest().Where(e => e.Parent == null))
            data.Add(e, Path.Combine(TestDataDir, e.File));
        return data;
    }

    public static TheoryData<Entry, string, string> ChildFiles()
    {
        var data = new TheoryData<Entry, string, string>();
        foreach (var e in LoadManifest().Where(e => e.Parent != null))
            data.Add(e,
                Path.Combine(TestDataDir, e.File),
                Path.Combine(TestDataDir, e.Parent!));
        return data;
    }

    [Theory]
    [MemberData(nameof(StandaloneFiles))]
    public void Deep_verify(Entry entry, string path)
    {
        using var fs = File.OpenRead(path);
        var err = Chd.CheckFile(fs, path, deepCheck: true, out var ver, out _, out _);

        if (entry.Expect == "invalid")
        {
            Assert.NotEqual(ChdError.Chderrnone, err);
        }
        else
        {
            Assert.Equal(ChdError.Chderrnone, err);
            Assert.Equal(entry.Version, ver);
        }
    }

    [Theory]
    [MemberData(nameof(StandaloneFiles))]
    public void Open_and_read(Entry entry, string path)
    {
        var err = ChdFile.Open(path, out var chd);
        using (chd)
        {
            if (entry.Expect == "invalid")
            {
                Assert.NotEqual(ChdError.Chderrnone, err);
                return;
            }

            Assert.Equal(ChdError.Chderrnone, err);
            Assert.NotNull(chd);
            Assert.Equal(entry.Version, chd.Version);

            var buffer = new byte[chd.HunkBytes];
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(chd.HunkCount - 1, buffer));
        }
    }

    [Theory]
    [MemberData(nameof(ChildFiles))]
    public void Requires_parent(Entry entry, string childPath, string _)
    {
        var err = ChdFile.Open(childPath, out var chd);
        Assert.Equal(ChdError.Chderrrequiresparent, err);
        Assert.Null(chd);
    }

    [Theory]
    [MemberData(nameof(ChildFiles))]
    public void Full_chain_pass(Entry entry, string childPath, string parentPath)
    {
        var err = Chd.CheckFileWithParent(childPath, parentPath, out var ver, out _, out _);
        Assert.Equal(ChdError.Chderrnone, err);
        Assert.Equal(entry.Version, ver);
    }

    [Fact]
    public void V5_tiny_rejected_gracefully()
    {
        string path = Path.Combine(TestDataDir, "v5_tiny.chd");
        var err = Chd.CheckFile(File.OpenRead(path), path, deepCheck: false, out _, out _, out _);
        Assert.NotEqual(ChdError.Chderrnone, err);
    }
}
