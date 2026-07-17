using System.Diagnostics;

namespace CHDSharpTestGen;

internal static class ToolRunner
{
    /// <summary>Runs a tool and throws if it fails. Returns captured stdout+stderr.</summary>
    public static string Run(string exe, string args, string workDir)
    {
        Console.WriteLine($"  > {Path.GetFileName(exe)} {args}");
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {exe}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} {args}\nexit {p.ExitCode}\n{stdout}\n{stderr}");

        return stdout + stderr;
    }
}
