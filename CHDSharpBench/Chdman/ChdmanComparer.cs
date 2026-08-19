using System.Diagnostics;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpBench.Chdman;

/// <summary>
/// External-tool comparison harness (Phase 7.4): runs the stock <c>chdman.exe</c> from MAME
/// side-by-side with the library on the same corpus and a synthetic image, then prints a
/// wall-clock comparison table (median of N runs). Usage:
/// <c>CHDSharpBench --chdman &lt;path-to-chdman.exe&gt; [--corpus &lt;dir&gt;] [--codecs zlib,zstd,...] [--size-mb N] [--runs N]</c>.
/// BenchmarkDotNet covers precise in-process timings; this harness adds the cross-tool
/// comparison the doc asks for (chdman vs. CHDSharp on identical inputs).
/// </summary>
public static class ChdmanComparer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private static readonly string[] AllCodecs =
        ["zlib", "zstd", "lzma", "huff", "flac", "none", "cdzl", "cdlz", "cdzs", "cdfl"];

    public static void Run(string chdmanExe, string corpusDir, IReadOnlyList<string> args)
    {
        Corpus.Configure(corpusDir);

        var codecs = ParseCodecs(args);
        var sizeMb = ParseIntArg(args, "--size-mb=", "--size-mb", 64);
        var runs = ParseIntArg(args, "--runs=", "--runs", 3);

        var outDir = Path.Combine(Path.GetTempPath(), $"chdbench_cmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            Console.WriteLine($"chdman      : {Path.GetFullPath(chdmanExe)}");
            Console.WriteLine($"corpus      : {Corpus.Dir}");
            Console.WriteLine($"synthetic   : {sizeMb} MiB, codecs [{string.Join(", ", codecs)}], median of {runs} runs");
            Console.WriteLine();

            var failures = new List<string>();
            var rows = new List<(string Target, string ChdmanMs, string LibMs)>();

            Console.WriteLine("-- VERIFY pass (corpus CHDs; median ms) --");
            foreach (var file in Corpus.ChdFiles().Where(Corpus.IsExpectedOk))
            {
                var name = Path.GetFileName(file);
                var parent = Corpus.ParentFor(file);
                var argsChild = parent != null ? " -ip \"" + parent + "\"" : "";

                var chdmanMs = Time(() => RunVerifyChd(chdmanExe, file, argsChild), runs, failures);
                var libMs = Time(() => RunLibVerify(file, parent), runs, failures);
                rows.Add((name, Ms(chdmanMs), Ms(libMs)));
            }

            Console.WriteLine();
            Console.WriteLine("-- ENCODE pass (synthetic image; ms) --");
            var bin = Path.Combine(outDir, "image.bin");
            File.WriteAllBytes(bin, CreateImage(sizeMb * 1024 * 1024));
            foreach (var codec in codecs)
            {
                if (!AllCodecs.Contains(codec, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"skipping unknown codec '{codec}' (supported: {string.Join(", ", AllCodecs)})");
                    continue;
                }

                var chdmanOut = Path.Combine(outDir, $"chdman_{codec}.chd");
                var libOut = Path.Combine(outDir, $"lib_{codec}.chd");

                var chdmanMs = Time(() => RunChdmanCreate(chdmanExe, codec, bin, chdmanOut), runs, failures);
                var libMs = Time(() => RunLibEncode(bin, libOut, codec), runs, failures);
                rows.Add(("createhd -c " + codec, Ms(chdmanMs), Ms(libMs)));
            }

            Console.WriteLine();
            PrintTable(rows);
            Console.WriteLine();
            if (failures.Count > 0)
            {
                Console.WriteLine("Warnings:");
                foreach (var f in failures.Distinct(StringComparer.Ordinal))
                    Console.WriteLine("  ! " + f);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(outDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Runs an action <c>runs</c> times; returns the median elapsed ms. Exceptions are recorded
    /// in <paramref name="failures"/> and the median of whatever completed is returned.</summary>
    private static double Time(Action action, int runs, List<string> failures)
    {
        var samples = new List<double>(runs);
        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failures.Add(ex.Message.Split('\n', 2)[0]);
                samples.Add(double.NaN);
                continue;
            }
            finally
            {
                sw.Stop();
            }

            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        var valid = samples.Where(d => !double.IsNaN(d)).ToList();
        if (valid.Count == 0)
            return 0;

        valid.Sort();
        return valid.Count % 2 == 1
            ? valid[valid.Count / 2]
            : (valid[valid.Count / 2 - 1] + valid[valid.Count / 2]) / 2.0;
    }

    private static void RunVerifyChd(string chdmanExe, string file, string extraArgs)
    {
        var rc = RunProcess(chdmanExe, $"verify -i \"{file}\"{extraArgs}");
        if (rc != 0)
            throw new InvalidOperationException($"chdman verify {Path.GetFileName(file)} exit {rc}");
    }

    private static void RunLibVerify(string file, string? parent)
    {
        var result = CHDSharp.Chd.CheckFileWithParent(file, parent);
        if (result.Error != ChdError.Chderrnone)
            throw new InvalidOperationException($"library verify {Path.GetFileName(file)}: {result.Error}");
    }

    private static void RunChdmanCreate(string chdmanExe, string codec, string bin, string outChd)
    {
        var rc = RunProcess(chdmanExe, $"createhd -c {codec} -i \"{bin}\" -o \"{outChd}\" -f");
        if (rc != 0)
            throw new InvalidOperationException($"chdman createhd -c {codec} exit {rc}");
    }

    private static void RunLibEncode(string bin, string outChd, string codec)
    {
        var tags = ChdCodecs.ParseCodecTags(codec);
        var isCd = codec is "cdzl" or "cdlz" or "cdzs" or "cdfl";
        var hunkBytes = isCd ? CdConstants.FramesPerHunk * CdConstants.FrameSize : 4096u;
        var unitBytes = isCd ? CdConstants.FrameSize : 512u;
        using var src = File.OpenRead(bin);
        ChdEncoder.EncodeRaw(src, outChd, hunkBytes, unitBytes, tags);
    }

    private static int RunProcess(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("process start failed");
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        if (!proc.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            throw new InvalidOperationException($"'{Path.GetFileName(exePath)} {arguments}' timed out after {Timeout}");
        }

        return proc.ExitCode;
    }

    /// <summary>Deterministic mixed-content image (same generator the encode benchmarks use):
    /// ~50% pseudo-random, ~50% arithmetic runs.</summary>
    private static byte[] CreateImage(int sizeBytes)
    {
        var rng = new Random(0x5EED);
        var data = new byte[sizeBytes];
        rng.NextBytes(data);
        var half = sizeBytes / 2;
        for (var i = half; i < sizeBytes; i++)
        {
            data[i] = (byte)((i / 97) & 0xFF);
        }

        return data;
    }

    private static string Ms(double ms)
    {
        return ms >= 999 ? $"{ms / 1000.0:0.000} s" : $"{ms:0.0} ms";
    }

    private static void PrintTable(List<(string name, string chdman, string lib)> rows)
    {
        var widthName = Math.Max(rows.Count == 0 ? 12 : rows.Max(r => r.name.Length), 12);
        var widthCm = Math.Max(rows.Count == 0 ? 12 : rows.Max(r => r.chdman.Length), 12);
        var widthLib = Math.Max(rows.Count == 0 ? 12 : rows.Max(r => r.lib.Length), 12);
        var rule = new string('-', widthName + widthCm + widthLib + 12);
        Console.WriteLine(rule);
        Console.WriteLine($"  {"target".PadRight(widthName)}{"chdman".PadLeft(widthCm + 2)}{"library".PadLeft(widthLib + 2)}");
        Console.WriteLine(rule);
        foreach (var (name, chdman, lib) in rows)
        {
            Console.WriteLine($"  {name.PadRight(widthName)}{(" " + chdman).PadLeft(widthCm + 2)}{(" " + lib).PadLeft(widthLib + 2)}");
        }

        Console.WriteLine(rule);
    }

    private static string[] ParseCodecs(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--codec=", StringComparison.Ordinal))
                return args[i]["--codec=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (string.Equals(args[i], "--codecs", StringComparison.Ordinal) && i + 1 < args.Count)
                return args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return ["zlib", "zstd"];
    }

    private static int ParseIntArg(IReadOnlyList<string> args, string prefix, string flag, int dflt)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if ((args[i].StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(args[i][prefix.Length..], out var n) && n > 0) || (string.Equals(args[i], flag, StringComparison.Ordinal) && i + 1 < args.Count && int.TryParse(args[i + 1], out n) && n > 0))
                return n;
        }

        return dflt;
    }
}