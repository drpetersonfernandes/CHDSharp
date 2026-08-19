using BenchmarkDotNet.Running;
using CHDSharpBench.Chdman;

namespace CHDSharpBench;

public static class Program
{
    /// <summary>
    /// CHDSharp benchmark suite (Phase 7.4). Run without arguments to benchmark all groups;
    /// pass BenchmarkDotNet filters (e.g. <c>*Decode*</c>) to select, or use
    /// <c>--chdman &lt;chdman.exe&gt; [--corpus &lt;dir&gt;]</c> to run the external chdman
    /// comparison harness. The corpus directory defaults to <c>CHDSharpTest/TestData</c>
    /// (repo layout); override with <c>--corpus &lt;dir&gt;</c>.
    /// </summary>
    public static void Main(string[] args)
    {
        try
        {
            var corpusDir = Corpus.ResolveDefault();
            string? chdmanExe = null;

            // Strip our own switches before handing the rest to BenchmarkDotNet.
            var runnerArgs = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--corpus" when i + 1 < args.Length:
                        corpusDir = args[++i];
                        break;
                    case "--chdman":
                        chdmanExe = i + 1 < args.Length ? args[++i] : "chdman.exe";
                        break;
                    default:
                        if (args[i].StartsWith("--corpus:", StringComparison.Ordinal))
                        {
                            corpusDir = args[i]["--corpus:".Length..];
                        }
                        else if (args[i].StartsWith("--chdman=", StringComparison.Ordinal))
                        {
                            chdmanExe = args[i]["--chdman=".Length..];
                        }
                        else
                            runnerArgs.Add(args[i]);

                        break;
                }
            }

            if (chdmanExe != null)
            {
                if (!File.Exists(chdmanExe))
                {
                    Console.Error.WriteLine($"chdman executable not found: '{chdmanExe}'");
                    return;
                }

                ChdmanComparer.Run(chdmanExe, corpusDir, runnerArgs);
                return;
            }

            Corpus.Configure(corpusDir);
            if (runnerArgs.Count == 0)
            {
                // No filter: run everything (all four groups) instead of prompting.
                runnerArgs.Add("--filter");
                runnerArgs.Add("*");
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(runnerArgs.ToArray());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }
    }
}