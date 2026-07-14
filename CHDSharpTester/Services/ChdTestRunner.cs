using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpTester.Models;
using Serilog;

namespace CHDSharpTester.Services;

/// <summary>Executes a multi-test verification suite against a list of CHD files, cross-checking the C# reader against chdman when available.</summary>
public class ChdTestRunner
{
    private static string? _chdmanVersion;

    /// <summary>Gets the detected chdman version string from the last run, or null if not yet detected.</summary>
    public string? ChdmanVersion => _chdmanVersion;

    /// <summary>Runs the full test suite against the specified files asynchronously.</summary>
    /// <param name="files">The list of CHD file entries to test.</param>
    /// <param name="chdmanPath">The path to the chdman executable (can be empty if chdman is unavailable).</param>
    /// <param name="progress">An optional progress reporter for UI updates.</param>
    /// <returns>A <see cref="TestSessionResult"/> containing aggregated results for all files.</returns>
    public async Task<TestSessionResult> RunAsync(
        List<ChdFileEntry> files,
        string chdmanPath,
        IProgress<TestProgress>? progress = null)
    {
        var session = new TestSessionResult();
        var chdmanAvailable = File.Exists(chdmanPath);

        using var chdman = chdmanAvailable ? new ChdmanWrapper(chdmanPath) : null;

        if (chdman != null)
        {
            try
            {
                var r = chdman.Run("info");
                if (r.ExitCode == 0)
                {
                    var lines = r.StdOut.Split('\n');
                    _chdmanVersion = lines.FirstOrDefault()?.Trim();
                }
            }
            catch { }
        }

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            progress?.Report(new TestProgress(file.FileName, i + 1, files.Count,
                "Starting", $"Testing {file.FileName}..."));

            var result = await Task.Run(() => TestSingleFile(file, chdman, progress, i, files.Count));
            session.FileResults.Add(result);
        }

        progress?.Report(new TestProgress("Done", files.Count, files.Count,
            "Complete", "All tests finished.", true));

        return session;
    }

    private PerFileResult TestSingleFile(
        ChdFileEntry entry,
        ChdmanWrapper? chdman,
        IProgress<TestProgress>? progress,
        int fileIndex,
        int totalFiles)
    {
        var sw = Stopwatch.StartNew();
        var result = new PerFileResult
        {
            FileName = entry.FileName,
            FilePath = entry.FilePath,
            FileSize = entry.FileSize
        };

        var path = entry.FilePath;

        if (!File.Exists(path))
        {
            result.SubTests.Add(new SubTestResult
            {
                TestName = "All Tests",
                Status = TestStatus.Skipped,
                Detail = "File not found on disk."
            });
            result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        // Test 1: CheckHeader (basic magic + version validation)
        Report(progress, entry.FileName, fileIndex + 1, totalFiles, "CheckHeader",
            "Validating CHD header magic and version...");
        var t1Sw = Stopwatch.StartNew();
        using (var fs = File.OpenRead(path))
        {
            var headerOk = Chd.CheckHeader(fs, out var headerLen, out var headerVer);
            t1Sw.Stop();
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Header Basic Check",
                Status = headerOk ? TestStatus.Passed : TestStatus.Failed,
                Detail = headerOk
                    ? $"Valid V{headerVer} header, {headerLen} bytes"
                    : "Invalid CHD header (bad magic or version)",
                ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
            });
        }

        // Test 2: Header vs chdman info
        if (chdman != null && chdman.Available)
        {
            Report(progress, entry.FileName, fileIndex + 1, totalFiles, "Header vs chdman",
                "Comparing header fields with chdman info...");
            t1Sw.Restart();
            var info = chdman.GetInfo(path);
            t1Sw.Stop();
            if (info != null)
            {
                var openErr = ChdFile.Open(path, out var chd);
                if (openErr == ChdError.Chderrnone && chd != null)
                {
                    using (chd)
                    {
                        var details = new List<string>();
                        var allMatch = true;

                        if (chd.Version == (uint)info.Version)
                            details.Add($"Version: V{chd.Version} ✓");
                        else { details.Add($"Version: lib={chd.Version} chdman={info.Version} ✗"); allMatch = false; }

                        if (chd.HunkBytes == info.HunkBytes)
                            details.Add($"HunkBytes: {chd.HunkBytes} ✓");
                        else { details.Add($"HunkBytes: lib={chd.HunkBytes} chdman={info.HunkBytes} ✗"); allMatch = false; }

                        if (chd.TotalBytes == info.LogicalBytes)
                            details.Add($"TotalBytes: {chd.TotalBytes} ✓");
                        else { details.Add($"TotalBytes: lib={chd.TotalBytes} chdman={info.LogicalBytes} ✗"); allMatch = false; }

                        if (chd.HunkCount == info.TotalHunks)
                            details.Add($"Hunks: {chd.HunkCount} ✓");
                        else { details.Add($"Hunks: lib={chd.HunkCount} chdman={info.TotalHunks} ✗"); allMatch = false; }

                        var libSha1 = HashUtil.ToHex(chd.Sha1);
                        if (info.Sha1 != null && libSha1 == info.Sha1)
                            details.Add($"SHA1: {libSha1} ✓");
                        else if (info.Sha1 != null)
                        { details.Add($"SHA1: lib={libSha1} chdman={info.Sha1} ✗"); allMatch = false; }
                        else
                            details.Add($"SHA1: {libSha1}");

                        var libDataSha1 = HashUtil.ToHex(chd.RawSha1);
                        if (info.DataSha1 != null && libDataSha1 == info.DataSha1)
                            details.Add($"DataSHA1: {libDataSha1} ✓");
                        else if (info.DataSha1 != null)
                        { details.Add($"DataSHA1: lib={libDataSha1} chdman={info.DataSha1} ✗"); allMatch = false; }
                        else if (libDataSha1 != "(none)" && !HashUtil.IsAllZero(chd.RawSha1!))
                            details.Add($"DataSHA1: {libDataSha1}");

                        result.SubTests.Add(new SubTestResult
                        {
                            TestName = "Header vs chdman",
                            Status = allMatch ? TestStatus.Passed : TestStatus.Failed,
                            Detail = string.Join("\n", details),
                            ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                        });
                    }
                }
                else
                {
                    result.SubTests.Add(new SubTestResult
                    {
                        TestName = "Header vs chdman",
                        Status = TestStatus.Failed,
                        Detail = $"Failed to open with CHDSharp: {openErr}",
                        ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                    });
                }
            }
            else
            {
                result.SubTests.Add(new SubTestResult
                {
                    TestName = "Header vs chdman",
                    Status = TestStatus.Failed,
                    Detail = "chdman info command failed.",
                    ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                });
            }
        }
        else
        {
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Header vs chdman",
                Status = TestStatus.Skipped,
                Detail = "chdman.exe not available."
            });
        }

        // Test 3: Deep CheckFile verification
        Report(progress, entry.FileName, fileIndex + 1, totalFiles, "Deep Verify",
            "Decompressing all hunks with CRC validation...");
        t1Sw.Restart();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
            var cfErr = Chd.CheckFile(fs, entry.FileName, true, out var ver, out var sha1, out _);
            t1Sw.Stop();
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Deep Verification",
                Status = cfErr == ChdError.Chderrnone ? TestStatus.Passed : TestStatus.Failed,
                Detail = cfErr == ChdError.Chderrnone
                    ? $"V{ver}, SHA1={HashUtil.ToHex(sha1)}"
                    : $"Error: {cfErr}",
                ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
            });
        }
        catch (Exception ex)
        {
            t1Sw.Stop();
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Deep Verification",
                Status = TestStatus.Failed,
                Detail = $"Exception: {ex.Message}",
                ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
            });
        }

        // Test 4: chdman verify
        if (chdman != null && chdman.Available)
        {
            Report(progress, entry.FileName, fileIndex + 1, totalFiles, "chdman Verify",
                "Running chdman verify...");
            t1Sw.Restart();
            var chdmanOk = chdman.Verify(path);
            t1Sw.Stop();
            result.SubTests.Add(new SubTestResult
            {
                TestName = "chdman Verify",
                Status = chdmanOk ? TestStatus.Passed : TestStatus.Failed,
                Detail = chdmanOk ? "chdman verify passed." : "chdman verify failed.",
                ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
            });
        }
        else
        {
            result.SubTests.Add(new SubTestResult
            {
                TestName = "chdman Verify",
                Status = TestStatus.Skipped,
                Detail = "chdman.exe not available."
            });
        }

        // Test 5: Full SHA1 read
        Report(progress, entry.FileName, fileIndex + 1, totalFiles, "Full SHA1",
            "Reading entire decompressed image and computing SHA1...");
        t1Sw.Restart();
        var openErr2 = ChdFile.Open(path, out var chd2);
        if (openErr2 == ChdError.Chderrnone && chd2 != null)
        {
            using (chd2)
            {
                var rawSha1 = chd2.RawSha1;
                if (rawSha1 == null || HashUtil.IsAllZero(rawSha1))
                {
                    result.SubTests.Add(new SubTestResult
                    {
                        TestName = "Full SHA1",
                        Status = TestStatus.Skipped,
                        Detail = "No raw SHA1 in header (V1/V2).",
                        ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                    });
                }
                else
                {
                    var computed = ComputeFullImageSha1(chd2);
                    var hexExpected = HashUtil.ToHex(rawSha1);
                    var match = computed == hexExpected;
                    t1Sw.Stop();
                    result.SubTests.Add(new SubTestResult
                    {
                        TestName = "Full SHA1",
                        Status = match ? TestStatus.Passed : TestStatus.Failed,
                        Detail = match
                            ? $"{hexExpected} ✓ ({(double)chd2.TotalBytes / (1024.0 * 1024):F1} MB)"
                            : $"Expected={hexExpected} Computed={computed} ✗",
                        ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                    });
                }
            }
        }
        else
        {
            t1Sw.Stop();
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Full SHA1",
                Status = TestStatus.Failed,
                Detail = $"Failed to open file: {openErr2}",
                ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
            });
        }

        // Test 6: Random Access (only for small files / files with chdman)
        if (chdman != null && chdman.Available && entry.IsSmall)
        {
            RunRandomAccessTest(entry, chdman, progress, fileIndex, totalFiles, result);
        }
        else if (!entry.IsSmall)
        {
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Random Access",
                Status = TestStatus.Skipped,
                Detail = "File >= 1 GB, skipped to keep runtime reasonable."
            });
        }
        else
        {
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Random Access",
                Status = TestStatus.Skipped,
                Detail = "chdman.exe not available."
            });
        }

        result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
        Log.Information("[{Status}] {File} ({Time:N1}s)",
            result.AllPassed ? "PASS" : "FAIL", entry.FileName, result.ElapsedSeconds);

        return result;
    }

    private void RunRandomAccessTest(
        ChdFileEntry entry,
        ChdmanWrapper chdman,
        IProgress<TestProgress>? progress,
        int fileIndex,
        int totalFiles,
        PerFileResult result)
    {
        Report(progress, entry.FileName, fileIndex + 1, totalFiles, "Random Access",
            "Testing 7 byte ranges vs chdman extractraw...");

        var tSw = Stopwatch.StartNew();
        var openErr = ChdFile.Open(entry.FilePath, out var chd);
        if (openErr != ChdError.Chderrnone || chd == null)
        {
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Random Access",
                Status = TestStatus.Failed,
                Detail = $"Failed to open: {openErr}",
                ElapsedSeconds = tSw.Elapsed.TotalSeconds
            });
            return;
        }

        using (chd)
        {
            var hb = chd.HunkBytes;
            var total = chd.TotalBytes;
            var hunkCount = chd.HunkCount;

            if (hunkCount < 2)
            {
                result.SubTests.Add(new SubTestResult
                {
                    TestName = "Random Access",
                    Status = TestStatus.Skipped,
                    Detail = "Need at least 2 hunks.",
                    ElapsedSeconds = tSw.Elapsed.TotalSeconds
                });
                return;
            }

            (ulong offset, int length, string desc)[] ranges =
            [
                (0, Math.Min((int)hb, (int)total), "first hunk"),
                (hb, Math.Min((int)hb, (int)(total - hb)), "second hunk"),
                (hb / 2, (int)hb, "cross-hunk boundary"),
                (total - Math.Min(hb, total), (int)Math.Min(hb, total), "last hunk"),
                (17, 97, "small unaligned"),
                (hb / 3, 7, "tiny unaligned"),
                (total > 50 ? total - 50 : 0, 37, "near end")
            ];

            var details = new List<string>();
            var mismatchCount = 0;

            foreach (var (offset, length, desc) in ranges)
            {
                if (offset + (ulong)length > total)
                    continue;

                try
                {
                    var chdmanBytes = chdman.ExtractRaw(entry.FilePath, offset, (ulong)length);
                    if (chdmanBytes == null || chdmanBytes.Length != length)
                    {
                        details.Add($"{desc}: chdman extract failed");
                        mismatchCount++;
                        continue;
                    }

                    var csharpBytes = new byte[length];
                    var err = chd.Read(offset, csharpBytes, 0, length);
                    if (err != ChdError.Chderrnone)
                    {
                        details.Add($"{desc}: Read error {err}");
                        mismatchCount++;
                        continue;
                    }

                    if (chdmanBytes.AsSpan().SequenceEqual(csharpBytes))
                    {
                        details.Add($"{desc} ({offset}+{length}): OK");
                    }
                    else
                    {
                        details.Add($"{desc} ({offset}+{length}): MISMATCH");
                        mismatchCount++;
                    }
                }
                catch (Exception ex)
                {
                    details.Add($"{desc}: exception - {ex.Message}");
                    mismatchCount++;
                }
            }

            tSw.Stop();
            result.SubTests.Add(new SubTestResult
            {
                TestName = "Random Access",
                Status = mismatchCount == 0 ? TestStatus.Passed : TestStatus.Failed,
                Detail = string.Join("\n", details),
                ElapsedSeconds = tSw.Elapsed.TotalSeconds
            });
        }
    }

    private static string ComputeFullImageSha1(ChdFile chd)
    {
        using var sha1 = SHA1.Create();
        var buf = new byte[chd.HunkBytes];
        var remaining = chd.TotalBytes;
        ulong offset = 0;

        while (remaining > 0)
        {
            var chunk = (int)Math.Min((ulong)buf.Length, remaining);
            var err = chd.Read(offset, buf, 0, chunk);
            if (err != ChdError.Chderrnone)
                return $"ERROR at offset {offset}: {err}";

            sha1.TransformBlock(buf, 0, chunk, null, 0);
            offset += (ulong)chunk;
            remaining -= (ulong)chunk;
        }

        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return HashUtil.ToHex(sha1.Hash!);
    }

    private static void Report(IProgress<TestProgress>? progress, string file, int index, int total,
        string test, string status)
    {
        progress?.Report(new TestProgress(file, index, total, test, status));
        Log.Debug("[{Index}/{Total}] {File} - {Test}: {Status}", index, total, file, test, status);
    }
}
