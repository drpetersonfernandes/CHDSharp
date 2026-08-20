using System.Diagnostics;

namespace CHDSharpBattleTest;

/// <summary>
/// CHDSharp battle test: exhaustively cross-checks the CHDSharpLib decoder and the
/// CHDSharpEncoder encoder against MAME's chdman.exe on a deterministic corpus of raw
/// and CD images. Produces a report and an exit code (0 = all passed).
/// </summary>
internal static class Program
{
    private static void PrintUsage()
    {
        Console.WriteLine("""
            CHDSharpBattleTest — battle-test CHDSharp decode/encode against chdman.exe

            Usage: CHDSharpBattleTest [options]

              --chdman <path>   chdman executable (default: repo-root chdman.exe or PATH)
              --out <dir>       artifact + report root (default: <repo>/TestResults/battle)
              --quick           reduced corpus (faster smoke battle)
              --seed <n>        RNG seed for the corpus (default 1337)
              --no-keep         delete artifacts at the end when everything passed
              --help            show this help

            Exit code: 0 when every check passed, 1 when any failed, 2 on usage errors.
            """);
    }

    private static int Main(string[] args)
    {
        string? chdmanPath = null;
        string? outDir = null;
        var quick = false;
        var noKeep = false;
        var seed = 1337;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--chdman" when i + 1 < args.Length:
                    chdmanPath = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outDir = args[++i];
                    break;
                case "--quick":
                    quick = true;
                    break;
                case "--no-keep":
                    noKeep = true;
                    break;
                case "--seed" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out seed))
                    {
                        Console.Error.WriteLine($"Invalid seed: {args[i]}");
                        return 2;
                    }

                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    PrintUsage();
                    return 2;
            }
        }

        chdmanPath ??= ResolveChdmanPath();
        if (chdmanPath == null || !File.Exists(chdmanPath))
        {
            Console.Error.WriteLine("chdman.exe not found. Pass --chdman <path> or put chdman.exe in the repo root.");
            return 2;
        }

        var sw = Stopwatch.StartNew();
        var harness = new BattleHarness(chdmanPath, outDir, seed, quick);
        try
        {
            var failed = harness.Run();
            sw.Stop();
            Console.WriteLine();
            harness.PrintSummary();
            Console.WriteLine($"Battle finished in {sw.Elapsed.TotalSeconds:N1}s. Result: {(failed == 0 ? "ALL PASSED" : $"{failed} FAILED")}");

            if (failed == 0 && noKeep)
                harness.Cleanup();

            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Battle aborted: {ex}");
            return 1;
        }
    }

    private static string? ResolveChdmanPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, exeName);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        var fromEnv = Environment.GetEnvironmentVariable("CHDMAN_PATH");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        var fromPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in fromPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(p, exeName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}