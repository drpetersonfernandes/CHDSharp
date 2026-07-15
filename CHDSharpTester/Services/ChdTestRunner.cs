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
            catch
            {
                // ignored
            }
        }

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            progress?.Report(new TestProgress(file.FileName, i + 1, files.Count,
                "Starting", $"Testing {file.FileName}..."));

            var result = await Task.Run(() => TestSingleFile(file, chdman, progress, i, files.Count));
            session.FileResults.Add(result);
        }

        // Session-level tests: Zstd codec
        if (chdman is { Available: true })
        {
            await Task.Run(() => RunZstdCodecTests(files, chdman, progress, session));
        }

        // Session-level tests: Parent chain
        if (chdman is { Available: true })
        {
            await Task.Run(() => RunParentChainTests(files, chdman, progress, session));
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
        if (chdman is { Available: true })
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
                        {
                            details.Add($"Version: V{chd.Version} ✓");
                        }
                        else { details.Add($"Version: lib={chd.Version} chdman={info.Version} ✗");
                            allMatch = false; }

                        if (chd.HunkBytes == info.HunkBytes)
                        {
                            details.Add($"HunkBytes: {chd.HunkBytes} ✓");
                        }
                        else { details.Add($"HunkBytes: lib={chd.HunkBytes} chdman={info.HunkBytes} ✗");
                            allMatch = false; }

                        if (chd.TotalBytes == info.LogicalBytes)
                        {
                            details.Add($"TotalBytes: {chd.TotalBytes} ✓");
                        }
                        else { details.Add($"TotalBytes: lib={chd.TotalBytes} chdman={info.LogicalBytes} ✗");
                            allMatch = false; }

                        if (chd.HunkCount == info.TotalHunks)
                        {
                            details.Add($"Hunks: {chd.HunkCount} ✓");
                        }
                        else { details.Add($"Hunks: lib={chd.HunkCount} chdman={info.TotalHunks} ✗");
                            allMatch = false; }

                        var libSha1 = HashUtil.ToHex(chd.Sha1);
                        if (info.Sha1 != null && libSha1 == info.Sha1)
                        {
                            details.Add($"SHA1: {libSha1} ✓");
                        }
                        else if (info.Sha1 != null)
                        { details.Add($"SHA1: lib={libSha1} chdman={info.Sha1} ✗");
                            allMatch = false; }
                        else
                        {
                            details.Add($"SHA1: {libSha1}");
                        }

                        var libDataSha1 = HashUtil.ToHex(chd.RawSha1);
                        if (info.DataSha1 != null && libDataSha1 == info.DataSha1)
                        {
                            details.Add($"DataSHA1: {libDataSha1} ✓");
                        }
                        else if (info.DataSha1 != null)
                        { details.Add($"DataSHA1: lib={libDataSha1} chdman={info.DataSha1} ✗");
                            allMatch = false; }
                        else if (libDataSha1 != "(none)" && !HashUtil.IsAllZero(chd.RawSha1))
                        {
                            details.Add($"DataSHA1: {libDataSha1}");
                        }

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
        if (chdman is { Available: true })
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
                            ? $"{hexExpected} ✓ ({chd2.TotalBytes / (1024.0 * 1024):F1} MB)"
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
        if (chdman is { Available: true } && entry.IsSmall)
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

        // Tests 7-14: ReadHunk / Read API tests
        RunReadHunkApiTests(entry, progress, fileIndex, totalFiles, result);

        result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
        Log.Information("[{Status}] {File} ({Time:N1}s)",
            result.AllPassed ? "PASS" : "FAIL", entry.FileName, result.ElapsedSeconds);

        return result;
    }

    private static void RunRandomAccessTest(
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

    // ── ReadHunk / Read API tests (per-file) ─────────────────────────────

    private static void RunReadHunkApiTests(
        ChdFileEntry entry,
        IProgress<TestProgress>? progress,
        int fileIndex,
        int totalFiles,
        PerFileResult result)
    {
        Report(progress, entry.FileName, fileIndex + 1, totalFiles, "ReadHunk API",
            "Testing ReadHunk/Read bounds, determinism, consistency...");

        var tSw = Stopwatch.StartNew();
        var detail = new List<string>();
        var failures = 0;

        var openErr = ChdFile.Open(entry.FilePath, out var chd);
        if (openErr != ChdError.Chderrnone || chd == null)
        {
            result.SubTests.Add(new SubTestResult
            {
                TestName = "ReadHunk API Tests",
                Status = TestStatus.Failed,
                Detail = $"Failed to open: {openErr}",
                ElapsedSeconds = tSw.Elapsed.TotalSeconds
            });
            return;
        }

        using (chd)
        {
            var hb = chd.HunkBytes;
            var hc = chd.HunkCount;
            var total = chd.TotalBytes;

            // Test: ReadHunk out of range
            var buf = new byte[hb];
            var err = chd.ReadHunk(hc, buf);
            if (err == ChdError.Chderrhunkoutofrange)
            {
                detail.Add("ReadHunk out of range: OK");
            }
            else
            {
                detail.Add($"ReadHunk out of range: expected ChderrHunkOutOfRange, got {err}");
                failures++;
            }

            // Test: ReadHunk undersized buffer
            var tooSmall = new byte[hb - 1];
            err = chd.ReadHunk(0, tooSmall);
            if (err == ChdError.Chderrinvalidparameter)
            {
                detail.Add("ReadHunk undersized buffer: OK");
            }
            else
            {
                detail.Add($"ReadHunk undersized buffer: expected ChderrInvalidParameter, got {err}");
                failures++;
            }

            // Test: ReadHunk first/middle/last succeed
            var hunkBuf = new byte[hb];
            var hunkPass = true;
            foreach (var h in new[] { 0u, hc / 2, hc - 1 })
            {
                err = chd.ReadHunk(h, hunkBuf);
                if (err != ChdError.Chderrnone)
                {
                    detail.Add($"ReadHunk hunk {h}: failed with {err}");
                    hunkPass = false;
                    failures++;
                }
            }
            if (hunkPass)
            {
                detail.Add("ReadHunk first/middle/last: OK");
            }

            // Test: ReadHunk determinism (same hunk twice)
            var a = new byte[hb];
            var b = new byte[hb];
            chd.ReadHunk(0, a);
            chd.ReadHunk(0, b);
            if (a.AsSpan().SequenceEqual(b))
            {
                detail.Add("ReadHunk determinism: OK");
            }
            else
            {
                detail.Add("ReadHunk determinism: MISMATCH");
                failures++;
            }

            // Test: Read matches ReadHunk for first hunk
            var viaHunk = new byte[hb];
            chd.ReadHunk(0, viaHunk);
            var firstLen = (int)Math.Min(hb, total);
            var viaRead = new byte[firstLen];
            err = chd.Read(0, viaRead, 0, firstLen);
            if (err == ChdError.Chderrnone)
            {
                var match = true;
                for (var i = 0; i < firstLen; i++)
                {
                    if (viaHunk[i] != viaRead[i])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    detail.Add("Read vs ReadHunk first hunk: OK");
                }
                else
                {
                    detail.Add("Read vs ReadHunk first hunk: MISMATCH");
                    failures++;
                }
            }
            else
            {
                detail.Add($"Read vs ReadHunk first hunk: Read error {err}");
                failures++;
            }

            // Test: Read across hunk boundary matches concatenated hunks
            if (hc >= 2 && total >= (ulong)hb * 2)
            {
                var h0 = new byte[hb];
                var h1 = new byte[hb];
                chd.ReadHunk(0, h0);
                chd.ReadHunk(1, h1);
                var half = (int)(hb / 2);
                var len = (int)hb;
                var window = new byte[len];
                err = chd.Read((ulong)half, window, 0, len);
                if (err == ChdError.Chderrnone)
                {
                    var crossOk = true;
                    for (var i = 0; i < half && crossOk; i++)
                        if (h0[half + i] != window[i])
                        {
                            crossOk = false;
                        }

                    for (var i = 0; i < len - half && crossOk; i++)
                        if (h1[i] != window[half + i])
                        {
                            crossOk = false;
                        }

                    if (crossOk)
                    {
                        detail.Add("Read cross-hunk boundary: OK");
                    }
                    else
                    {
                        detail.Add("Read cross-hunk boundary: MISMATCH");
                        failures++;
                    }
                }
                else
                {
                    detail.Add($"Read cross-hunk boundary: Read error {err}");
                    failures++;
                }
            }
            else
            {
                detail.Add("Read cross-hunk boundary: skipped (< 2 full hunks)");
            }

            // Test: Read beyond end returns invalid parameter
            if (total >= 8)
            {
                var endBuf = new byte[16];
                err = chd.Read(total - 8, endBuf, 0, 16);
                if (err == ChdError.Chderrinvalidparameter)
                {
                    detail.Add("Read beyond end: OK");
                }
                else
                {
                    detail.Add($"Read beyond end: expected ChderrInvalidParameter, got {err}");
                    failures++;
                }
            }
            else
            {
                detail.Add("Read beyond end: skipped (image too small)");
            }

            // Test: Header properties are consistent
            var headerIssues = new List<string>();
            if (chd.Version is < 1 or > 5)
                headerIssues.Add($"Version={chd.Version} (expected 1-5)");
            if (chd.HunkBytes == 0)
                headerIssues.Add("HunkBytes=0");
            if (chd.HunkCount == 0)
                headerIssues.Add("HunkCount=0");
            if ((ulong)chd.HunkCount * chd.HunkBytes < chd.TotalBytes)
                headerIssues.Add($"Hunks*HunkBytes ({(ulong)chd.HunkCount * chd.HunkBytes}) < TotalBytes ({chd.TotalBytes})");
            if (headerIssues.Count == 0)
            {
                detail.Add($"Header properties: V{chd.Version}, {chd.HunkBytes} hunk bytes, {chd.HunkCount} hunks, {chd.TotalBytes} total: OK");
            }
            else
            {
                detail.Add($"Header properties: {string.Join("; ", headerIssues)}");
                failures++;
            }
        }

        tSw.Stop();
        result.SubTests.Add(new SubTestResult
        {
            TestName = "ReadHunk API Tests",
            Status = failures == 0 ? TestStatus.Passed : TestStatus.Failed,
            Detail = string.Join("\n", detail),
            ElapsedSeconds = tSw.Elapsed.TotalSeconds
        });
    }

    // ── Zstd codec tests (session-level) ──────────────────────────────────

    private static void RunZstdCodecTests(
        List<ChdFileEntry> files,
        ChdmanWrapper chdman,
        IProgress<TestProgress>? progress,
        TestSessionResult session)
    {
        var presentFiles = files
            .Where(f => File.Exists(f.FilePath))
            .OrderBy(f => new FileInfo(f.FilePath).Length)
            .ToList();
        if (presentFiles.Count == 0)
            return;

        var cdSource = presentFiles.FirstOrDefault(f =>
        {
            var e = ChdFile.Open(f.FilePath, out var c);
            if (e != ChdError.Chderrnone) return false;

            using (c) { return c != null && c.HunkBytes % 2448 == 0; }
        });
        if (cdSource == null)
        {
            session.FileResults.Add(new PerFileResult
            {
                FileName = "[Codec] Zstd",
                FilePath = "(none)",
                FileSize = "-",
                SubTests =
                [
                    new SubTestResult
                    {
                        TestName = "Zstd Codec Tests",
                        Status = TestStatus.Skipped,
                        Detail = "No CD-type source CHD available for recompression."
                    }
                ]
            });
            return;
        }

        progress?.Report(new TestProgress("[Codec]", files.Count + 1, files.Count + 1,
            "Zstd Tests", "Running Zstd/cdzs codec tests..."));

        var tempDir = CreateTempDir();
        try
        {
            var srcRawSha1 = RawSha1(cdSource.FilePath);

            // cdzs
            var cdzsPath = Path.Combine(tempDir, "cdzs.chd");
            PerFileResult cdzsResult;
            if (chdman.Copy(cdSource.FilePath, cdzsPath, "cdzs"))
            {
                var sw = Stopwatch.StartNew();
                var subTests = new List<SubTestResult>();

                var t1Sw = Stopwatch.StartNew();
                var openErr = ChdFile.Open(cdzsPath, out var cdzsChd);
                t1Sw.Stop();
                if (openErr == ChdError.Chderrnone && cdzsChd != null)
                {
                    using (cdzsChd)
                    {
                        var computed = ComputeFullImageSha1(cdzsChd);
                        subTests.Add(new SubTestResult
                        {
                            TestName = "cdzs decode",
                            Status = computed == srcRawSha1 ? TestStatus.Passed : TestStatus.Failed,
                            Detail = computed == srcRawSha1
                                ? $"SHA1={computed} ✓"
                                : $"Expected={srcRawSha1} Computed={computed} ✗",
                            ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                        });
                    }
                }
                else
                {
                    subTests.Add(new SubTestResult
                    {
                        TestName = "cdzs decode",
                        Status = TestStatus.Failed,
                        Detail = $"Failed to open cdzs CHD: {openErr}",
                        ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                    });
                }

                t1Sw.Restart();
                using (var fs = File.OpenRead(cdzsPath))
                {
                    var cfErr = Chd.CheckFile(fs, "cdzs.chd", true, out var ver, out var sha1, out _);
                    t1Sw.Stop();
                    subTests.Add(new SubTestResult
                    {
                        TestName = "cdzs CheckFile",
                        Status = cfErr == ChdError.Chderrnone ? TestStatus.Passed : TestStatus.Failed,
                        Detail = cfErr == ChdError.Chderrnone
                            ? $"V{ver}, SHA1={HashUtil.ToHex(sha1)}"
                            : $"Error: {cfErr}",
                        ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                    });
                }

                sw.Stop();
                cdzsResult = new PerFileResult
                {
                    FileName = "[Codec] cdzs",
                    FilePath = cdzsPath,
                    FileSize = new FileInfo(cdzsPath).Length switch
                    {
                        < 1024 * 1024 => $"{new FileInfo(cdzsPath).Length / 1024.0:F1} KB",
                        _ => $"{new FileInfo(cdzsPath).Length / (1024.0 * 1024):F1} MB"
                    },
                    SubTests = subTests,
                    ElapsedSeconds = sw.Elapsed.TotalSeconds
                };
            }
            else
            {
                cdzsResult = new PerFileResult
                {
                    FileName = "[Codec] cdzs",
                    FilePath = "(none)",
                    FileSize = "-",
                    SubTests =
                    [
                        new SubTestResult
                        {
                            TestName = "cdzs Codec Tests",
                            Status = TestStatus.Skipped,
                            Detail = "chdman copy to cdzs failed."
                        }
                    ]
                };
            }
            session.FileResults.Add(cdzsResult);

            // zstd — use any openable file (not necessarily CD-type)
            var zstdSource = presentFiles.FirstOrDefault(f => File.Exists(f.FilePath));
            if (zstdSource != null)
            {
                var zstdPath = Path.Combine(tempDir, "zstd.chd");
                var zstdRawSha1 = RawSha1(zstdSource.FilePath);
                PerFileResult zstdResult;
                if (chdman.Copy(zstdSource.FilePath, zstdPath, "zstd"))
                {
                    var sw = Stopwatch.StartNew();
                    var subTests = new List<SubTestResult>();

                    var t1Sw = Stopwatch.StartNew();
                    var openErr = ChdFile.Open(zstdPath, out var zstdChd);
                    t1Sw.Stop();
                    if (openErr == ChdError.Chderrnone && zstdChd != null)
                    {
                        using (zstdChd)
                        {
                            var computed = ComputeFullImageSha1(zstdChd);
                            subTests.Add(new SubTestResult
                            {
                                TestName = "zstd decode",
                                Status = computed == zstdRawSha1 ? TestStatus.Passed : TestStatus.Failed,
                                Detail = computed == zstdRawSha1
                                    ? $"SHA1={computed} ✓"
                                    : $"Expected={zstdRawSha1} Computed={computed} ✗",
                                ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                            });
                        }
                    }
                    else
                    {
                        subTests.Add(new SubTestResult
                        {
                            TestName = "zstd decode",
                            Status = TestStatus.Failed,
                            Detail = $"Failed to open zstd CHD: {openErr}",
                            ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                        });
                    }

                    t1Sw.Restart();
                    using (var fs = File.OpenRead(zstdPath))
                    {
                        var cfErr = Chd.CheckFile(fs, "zstd.chd", true, out var ver, out var sha1, out _);
                        t1Sw.Stop();
                        subTests.Add(new SubTestResult
                        {
                            TestName = "zstd CheckFile",
                            Status = cfErr == ChdError.Chderrnone ? TestStatus.Passed : TestStatus.Failed,
                            Detail = cfErr == ChdError.Chderrnone
                                ? $"V{ver}, SHA1={HashUtil.ToHex(sha1)}"
                                : $"Error: {cfErr}",
                            ElapsedSeconds = t1Sw.Elapsed.TotalSeconds
                        });
                    }

                    sw.Stop();
                    zstdResult = new PerFileResult
                    {
                        FileName = "[Codec] zstd",
                        FilePath = zstdPath,
                        FileSize = new FileInfo(zstdPath).Length switch
                        {
                            < 1024 * 1024 => $"{new FileInfo(zstdPath).Length / 1024.0:F1} KB",
                            _ => $"{new FileInfo(zstdPath).Length / (1024.0 * 1024):F1} MB"
                        },
                        SubTests = subTests,
                        ElapsedSeconds = sw.Elapsed.TotalSeconds
                    };
                }
                else
                {
                    zstdResult = new PerFileResult
                    {
                        FileName = "[Codec] zstd",
                        FilePath = "(none)",
                        FileSize = "-",
                        SubTests =
                        [
                            new SubTestResult
                            {
                                TestName = "zstd Codec Tests",
                                Status = TestStatus.Skipped,
                                Detail = "chdman copy to zstd failed."
                            }
                        ]
                    };
                }
                session.FileResults.Add(zstdResult);
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch { /* best effort */ }
        }
    }

    // ── Parent chain tests (session-level) ────────────────────────────────

    private static void RunParentChainTests(
        List<ChdFileEntry> files,
        ChdmanWrapper chdman,
        IProgress<TestProgress>? progress,
        TestSessionResult session)
    {
        var candidates = files
            .Where(f => File.Exists(f.FilePath))
            .OrderBy(f => new FileInfo(f.FilePath).Length)
            .ToList();

        // Need at least one CD-type CHD for the source
        var source = candidates.FirstOrDefault(f =>
        {
            var e = ChdFile.Open(f.FilePath, out var c);
            if (e != ChdError.Chderrnone) return false;

            using (c) { return c != null && c.HunkBytes % 2448 == 0; }
        });
        if (source == null)
        {
            session.FileResults.Add(new PerFileResult
            {
                FileName = "[Chain] Parent/Child",
                FilePath = "(none)",
                FileSize = "-",
                SubTests =
                [
                    new SubTestResult
                    {
                        TestName = "Parent Chain Tests",
                        Status = TestStatus.Skipped,
                        Detail = "No CD-type source CHD available."
                    }
                ]
            });
            return;
        }

        progress?.Report(new TestProgress("[Chain]", files.Count + 2, files.Count + 2,
            "Parent Chain", "Running parent/child chain tests..."));

        var tempDir = CreateTempDir();
        try
        {
            var parentPath = Path.Combine(tempDir, "parent.chd");
            var childPath = Path.Combine(tempDir, "child.chd");
            var wrongParentPath = Path.Combine(tempDir, "wrongparent.chd");

            // Build parent/child pair
            var rParent = chdman.CopyVerbose(source.FilePath, parentPath, "cdzl,cdfl");
            var rChild = chdman.CopyVerbose(source.FilePath, childPath, "cdzl,cdfl", parentPath);
            if (!File.Exists(parentPath) || !File.Exists(childPath))
            {
                session.FileResults.Add(new PerFileResult
                {
                    FileName = "[Chain] Parent/Child",
                    FilePath = "(none)",
                    FileSize = "-",
                    SubTests =
                    [
                        new SubTestResult
                        {
                            TestName = "Parent Chain Tests",
                            Status = TestStatus.Skipped,
                            Detail = $"chdman failed to build parent/child set. " +
                                     $"parent(exit={rParent.ExitCode}) child(exit={rChild.ExitCode})"
                        }
                    ]
                });
                return;
            }

            // Wrong parent: best-effort find any other file that recompresses
            var hasWrongParent = false;
            foreach (var other in candidates.Where(f => f != source))
            {
                if (chdman.Copy(other.FilePath, wrongParentPath, "cdzl,cdfl"))
                {
                    hasWrongParent = true;
                    break;
                }
            }

            var srcRawSha1 = RawSha1(source.FilePath);
            var sw = Stopwatch.StartNew();
            var subTests = new List<SubTestResult>();

            // Test: Child without parent requires parent
            var tSw = Stopwatch.StartNew();
            var openErr = ChdFile.Open(childPath, out var childNoParent);
            childNoParent?.Dispose();
            tSw.Stop();
            subTests.Add(new SubTestResult
            {
                TestName = "Child without parent",
                Status = openErr == ChdError.Chderrrequiresparent ? TestStatus.Passed : TestStatus.Failed,
                Detail = openErr == ChdError.Chderrrequiresparent
                    ? "ChderrRequiresParent ✓"
                    : $"Expected ChderrRequiresParent, got {openErr}",
                ElapsedSeconds = tSw.Elapsed.TotalSeconds
            });

            // Test: Child with correct parent opens
            tSw.Restart();
            openErr = ChdFile.Open(childPath, parentPath, out var childOk);
            tSw.Stop();
            if (openErr == ChdError.Chderrnone && childOk != null)
            {
                using (childOk)
                {
                    subTests.Add(new SubTestResult
                    {
                        TestName = "Child with correct parent",
                        Status = TestStatus.Passed,
                        Detail = "Opened successfully ✓",
                        ElapsedSeconds = tSw.Elapsed.TotalSeconds
                    });
                }
            }
            else
            {
                childOk?.Dispose();
                subTests.Add(new SubTestResult
                {
                    TestName = "Child with correct parent",
                    Status = TestStatus.Failed,
                    Detail = $"Failed: {openErr}",
                    ElapsedSeconds = tSw.Elapsed.TotalSeconds
                });
            }

            // Test: Child with wrong parent returns invalid parent
            if (hasWrongParent)
            {
                tSw.Restart();
                openErr = ChdFile.Open(childPath, wrongParentPath, out var childWrong);
                childWrong?.Dispose();
                tSw.Stop();
                subTests.Add(new SubTestResult
                {
                    TestName = "Child with wrong parent",
                    Status = openErr == ChdError.Chderrinvalidparent ? TestStatus.Passed : TestStatus.Failed,
                    Detail = openErr == ChdError.Chderrinvalidparent
                        ? "ChderrInvalidParent ✓"
                        : $"Expected ChderrInvalidParent, got {openErr}",
                    ElapsedSeconds = tSw.Elapsed.TotalSeconds
                });
            }
            else
            {
                subTests.Add(new SubTestResult
                {
                    TestName = "Child with wrong parent",
                    Status = TestStatus.Skipped,
                    Detail = "No alternate CHD available as wrong parent."
                });
            }

            // Test: Full read through chain matches source SHA1
            tSw.Restart();
            openErr = ChdFile.Open(childPath, parentPath, out var childForRead);
            if (openErr == ChdError.Chderrnone && childForRead != null)
            {
                using (childForRead)
                {
                    var computed = ComputeFullImageSha1(childForRead);
                    tSw.Stop();
                    subTests.Add(new SubTestResult
                    {
                        TestName = "Child full read matches source",
                        Status = computed == srcRawSha1 ? TestStatus.Passed : TestStatus.Failed,
                        Detail = computed == srcRawSha1
                            ? $"SHA1={computed} ✓"
                            : $"Expected={srcRawSha1} Computed={computed} ✗",
                        ElapsedSeconds = tSw.Elapsed.TotalSeconds
                    });
                }
            }
            else
            {
                childForRead?.Dispose();
                tSw.Stop();
                subTests.Add(new SubTestResult
                {
                    TestName = "Child full read matches source",
                    Status = TestStatus.Failed,
                    Detail = $"Failed to open child with parent: {openErr}",
                    ElapsedSeconds = tSw.Elapsed.TotalSeconds
                });
            }

            // Test: CheckFileWithParent succeeds
            tSw.Restart();
            var cfErr = Chd.CheckFileWithParent(childPath, parentPath,
                out var cfVer, out var cfSha1, out _);
            tSw.Stop();
            subTests.Add(new SubTestResult
            {
                TestName = "CheckFileWithParent",
                Status = cfErr == ChdError.Chderrnone ? TestStatus.Passed : TestStatus.Failed,
                Detail = cfErr == ChdError.Chderrnone
                    ? $"V{cfVer}, SHA1={HashUtil.ToHex(cfSha1)} ✓"
                    : $"Error: {cfErr}",
                ElapsedSeconds = tSw.Elapsed.TotalSeconds
            });

            // Test: chdman agrees child verifies with parent
            tSw.Restart();
            var chdmanOk = chdman.Verify(childPath, parentPath);
            tSw.Stop();
            subTests.Add(new SubTestResult
            {
                TestName = "chdman verify with parent",
                Status = chdmanOk ? TestStatus.Passed : TestStatus.Failed,
                Detail = chdmanOk ? "chdman verify passed ✓" : "chdman verify failed",
                ElapsedSeconds = tSw.Elapsed.TotalSeconds
            });

            sw.Stop();
            session.FileResults.Add(new PerFileResult
            {
                FileName = "[Chain] Parent/Child",
                FilePath = $"{parentPath}, {childPath}",
                FileSize = $"{new FileInfo(parentPath).Length / (1024.0 * 1024):F1} MB + {new FileInfo(childPath).Length / (1024.0 * 1024):F1} MB",
                SubTests = subTests,
                ElapsedSeconds = sw.Elapsed.TotalSeconds
            });
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch { /* best effort */ }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CHDSharpTester_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string? RawSha1(string path)
    {
        if (ChdFile.Open(path, out var c) != ChdError.Chderrnone)
            return null;
        if (c == null)
            return null;

        using (c)
        {
            return HashUtil.ToHex(c.RawSha1);
        }
    }
}
