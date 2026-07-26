using System.Diagnostics;

namespace CHDSharpTestGen;

internal static class ToolRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

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

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        if (!p.WaitForExit((int)DefaultTimeout.TotalMilliseconds))
        {
            try
            {
                p.Kill(true);
            }
            catch
            {
                // ignored
            }

            throw new InvalidOperationException($"{Path.GetFileName(exe)} timed out after {DefaultTimeout.TotalMinutes} minutes");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} {args}\nexit {p.ExitCode}\n{stdout}\n{stderr}");

        return stdout + stderr;
    }
}
