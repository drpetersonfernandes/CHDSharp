using BenchmarkDotNet.Attributes;
using CHDSharp;
using CHDSharp.Models;

namespace CHDSharpBench.Benchmarks;

/// <summary>
/// Full-image verification times: <see cref="Chd.CheckFile"/> with deep verification (every
/// hunk decompressed + SHA-1), run over all single-codec corpus files. "Cold" opens a fresh
/// stream per operation (first-touch of the on-disk file; OS page cache still holds a bit),
/// "Warm" precaches the file into a memory stream once and re-verifies from RAM. Bytes per
/// operation ≈ <c>HunkBytes × HunkCount</c> summed over the corpus; MB/s = value ÷ Mean.
/// </summary>
[Config(typeof(BenchConfig))]
public class VerifyBenchmarks
{
    private string[] _files = [];
    private MemoryStream _warm = new();
    private readonly Dictionary<string, string?> _parents = new(StringComparer.Ordinal);

    [GlobalSetup]
    public void Setup()
    {
        // Only files the manifest expects to verify, and only children whose parent is in the
        // corpus. v5_tiny.chd is an intentionally-invalid "unreadable map" file; v3_child has
        // its parent under a manifest name, resolved via Corpus.ParentFor.
        _files =
        [
            .. Corpus.ChdFiles()
                .Where(Corpus.IsExpectedOk)
                .Where(f => !Path.GetFileName(f).Contains("_child", StringComparison.Ordinal) || Corpus.ParentFor(f) != null)];

        _parents.Clear();
        foreach (var file in _files)
        {
            _parents[file] = Corpus.ParentFor(file);
        }

        using var ms = new MemoryStream();
        foreach (var file in _files.Where(f => _parents[f] == null))
        {
            using var fs = File.OpenRead(file);
            fs.CopyTo(ms);
        }

        _warm = new MemoryStream(ms.ToArray());
    }

    [Benchmark(Baseline = true)]
    public ulong CheckFile_Cold()
    {
        ulong bytes = 0;
        foreach (var file in _files)
        {
            if (_parents.TryGetValue(file, out var parent))
            {
                var r = Chd.CheckFileWithParent(file, parent);
                if (r.Error != ChdError.Chderrnone)
                    throw new InvalidOperationException($"CheckFileWithParent failed on '{file}': {r.Error}");

                continue;
            }

            using var fs = File.OpenRead(file);
            var err = Chd.CheckFile(fs, file, deepCheck: true, out _, out _, out _);
            if (err != ChdError.Chderrnone)
                throw new InvalidOperationException($"CheckFile failed on '{file}': {err}");

            bytes += (ulong)fs.Length;
        }

        return bytes;
    }

    [Benchmark]
    public ulong CheckFile_WarmPrecached()
    {
        _warm.Position = 0;
        ulong bytes = 0;
        foreach (var file in _files)
        {
            if (_parents.TryGetValue(file, out var parent))
            {
                var r = Chd.CheckFileWithParent(file, parent);
                if (r.Error != ChdError.Chderrnone)
                    throw new InvalidOperationException($"CheckFileWithParent failed on '{file}': {r.Error}");

                continue;
            }

            var start = _warm.Position;
            var err = Chd.CheckFile(_warm, file, deepCheck: true, out _, out _, out _);
            if (err != ChdError.Chderrnone)
                throw new InvalidOperationException($"CheckFile failed on '{file}': {err}");

            bytes += (ulong)(_warm.Position - start);
        }

        return bytes;
    }

    [Benchmark]
    public ulong CheckFile_WithParent_AllCorpus()
    {
        ulong bytes = 0;
        foreach (var file in _files)
        {
            var parent = _parents[file];
            var result = Chd.CheckFileWithParent(file, parent);
            if (result.Error != ChdError.Chderrnone)
                throw new InvalidOperationException($"CheckFileWithParent failed on '{file}': {result.Error}");

            bytes += (ulong)new FileInfo(file).Length;
        }

        return bytes;
    }
}