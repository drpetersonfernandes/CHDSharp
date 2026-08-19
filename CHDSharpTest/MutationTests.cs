using System.Diagnostics;
using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Deterministic mutation testing (Phase 6.1): applies thousands of seeded byte mutations,
/// truncations, and header field corruptions to real CHD files and asserts that the library
/// fails gracefully — a <see cref="ChdError"/> or a small bounded set of exceptions, never an
/// <see cref="OutOfMemoryException"/>, a crash, or a hang. This is the CI-visible equivalent of
/// a fuzzer: with a fixed seed the corpus is identical on every run.
/// </summary>
public class MutationTests
{
    private static string TestDataDir
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "TestData");
            Assert.True(Directory.Exists(dir), $"Test data directory not found: {dir}");
            return dir;
        }
    }

    private static IEnumerable<string> CorpusFiles()
    {
        // One file per version/codec family: V1 (legacy map), V3 (zlib), V4 (avhuff), V5 zlib,
        // V5 CD compound, V5 uncompressed, and a child CHD (read against its parent).
        foreach (var name in new[]
                 {
                     "v1_zlib.chd",
                     "v3_av.chd",
                     "v4_av.chd",
                     "v5_zlib.chd",
                     "v5_cd_default.chd",
                     "v5_none.chd",
                     "v5_child.chd"
                 })
        {
            var path = Path.Combine(TestDataDir, name);
            if (File.Exists(path))
                yield return path;
        }
    }

    public static IEnumerable<object[]> MutationSeeds()
    {
        const int mutationsPerFile = 500;
        var seed = 0x5EED;
        foreach (var file in CorpusFiles())
        {
            for (var i = 0; i < mutationsPerFile; i++)
            {
                yield return new object[] { file, seed++ };
            }
        }
    }

    /// <summary>Applies one deterministic mutation of a corpus file and asserts graceful failure.</summary>
    /// <param name="sourcePath">A valid corpus CHD.</param>
    /// <param name="seed">The mutation seed (deterministic per corpus + seed).</param>
    [Theory]
    [MemberData(nameof(MutationSeeds))]
    public void Mutated_file_fails_gracefully(string sourcePath, int seed)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var rng = new Random(seed);

        var mutation = rng.Next(4);
        switch (mutation)
        {
            case 0:
                // random byte flips (1-8 flips)
                var flips = rng.Next(1, 9);
                for (var i = 0; i < flips; i++)
                {
                    bytes[rng.Next(bytes.Length)] ^= (byte)(1 << rng.Next(8));
                }

                break;
            case 1:
                // truncation at a random point
                bytes = bytes[..rng.Next(bytes.Length)];
                break;
            case 2:
                // header field corruption (bytes 8..124 are header fields)
                var start = rng.Next(8, Math.Min(bytes.Length, 124));
                var length = Math.Min(rng.Next(1, 20), bytes.Length - start);
                for (var i = 0; i < length; i++)
                {
                    bytes[start + i] = (byte)rng.Next(256);
                }

                break;
            case 3:
                // append garbage
                var extra = new byte[rng.Next(1, 256)];
                rng.NextBytes(extra);
                bytes = [.. bytes, .. extra];
                break;
        }

        var mutatedPath = Path.Combine(Path.GetTempPath(), $"chdsharp_mut_{seed}.chd");
        try
        {
            File.WriteAllBytes(mutatedPath, bytes);

            // The interesting assertions: no crash, no hang, no unbounded allocation.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var sw = Stopwatch.StartNew();
            var err = ChdFile.Open(mutatedPath, out var chd, cts.Token);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"Open hung on seed {seed}");

            if (err != ChdError.Chderrnone || chd == null)
                return; // rejected at open: graceful.

            using (chd)
            {
                // Read every hunk (and a raw read) — may fail at any hunk, must not throw.
                var buffer = new byte[chd.HunkBytes];
                for (uint h = 0; h < chd.HunkCount; h++)
                {
                    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"Read hung on seed {seed} at hunk {h}");
                    var hunkErr = chd.ReadHunk(h, buffer, cts.Token);
                    Assert.True(hunkErr is ChdError.Chderrnone or ChdError.Chderrdecompressionerror or ChdError.Chderrinvaliddata or ChdError.Chderrrequiresparent or ChdError.Chderrinvalidparent,
                        $"Unexpected hunk error {hunkErr} on seed {seed} at hunk {h}");
                }

                try
                {
                    _ = chd.ReadRawHunk(0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 0 is always in range for a non-empty CHD, but a mutated header may have
                    // zero hunks; accepting a clean error is fine.
                }
            }

            // Also exercise the async path.
            var asyncErr = Task.Run(async () =>
            {
                var err2 = ChdFile.Open(mutatedPath, out var chd2, cts.Token);
                if (err2 != ChdError.Chderrnone || chd2 == null)
                    return err2;

                await using (chd2)
                {
                    var b = new byte[chd2.HunkBytes];
                    for (uint h = 0; h < chd2.HunkCount; h++)
                    {
                        var e = await chd2.ReadHunkAsync(h, b, cts.Token);
                        if (e is not (ChdError.Chderrnone or ChdError.Chderrdecompressionerror or ChdError.Chderrinvaliddata))
                            return e;
                    }
                }

                return ChdError.Chderrnone;
            }, cts.Token).Wait(TimeSpan.FromSeconds(60));
            Assert.True(asyncErr, $"Async read hung on seed {seed}");
        }
        catch (OutOfMemoryException)
        {
            Assert.Fail($"OutOfMemoryException on seed {seed} (mutation {mutation})");
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"Timeout/cancellation on seed {seed} (mutation {mutation})");
        }
        catch (AggregateException ex)
        {
            Assert.Fail($"Unhandled {ex.GetType().Name} on seed {seed} (mutation {mutation}): {ex.InnerException?.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or IndexOutOfRangeException or EndOfStreamException)
        {
            Assert.Fail($"Unhandled {ex.GetType().Name} on seed {seed} (mutation {mutation}): {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(mutatedPath);
            }
            catch (Exception)
            {
                // best effort cleanup
            }
        }
    }

    /// <summary>The pristine corpus files must still open and read cleanly (guard against a
    /// test bug making every mutation "pass" trivially).</summary>
    [Fact]
    public void Pristine_corpus_still_reads()
    {
        foreach (var file in CorpusFiles())
        {
            var parent = Path.GetFileName(file) == "v5_child.chd"
                ? Path.Combine(TestDataDir, "v5_parent.chd")
                : null;
            var err = ChdFile.Open(file, parent, out var chd);
            Assert.Equal(ChdError.Chderrnone, err);
            using (chd)
            {
                var buffer = new byte[chd!.HunkBytes];
                for (uint h = 0; h < chd.HunkCount; h++)
                {
                    Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(h, buffer));
                }
            }
        }
    }
}