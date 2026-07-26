using System.Diagnostics;

namespace CHDSharp.Tests;

public class CliAdditionalTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static string CliPath
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            var testBinIdx = baseDir.IndexOf(
                Path.Combine("CHDSharpTest", "bin"),
                StringComparison.OrdinalIgnoreCase);
            if (testBinIdx >= 0)
            {
                var slnRoot = baseDir[..testBinIdx];
                return Path.Combine(slnRoot, "CHDSharpCli", "bin", "Debug",
                    "net10.0", "CHDSharpCli.dll");
            }
            return Path.Combine(AppContext.BaseDirectory, "CHDSharpCli.dll");
        }
    }

    private static (int exitCode, string output) RunCli(params string[] args)
    {
        var escapedArgs = string.Join(" ",
            args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        var argString = $"\"{CliPath}\" {escapedArgs}";

        var psi = new ProcessStartInfo("dotnet", argString)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        proc!.WaitForExit(30000);
        var output = proc.StandardOutput.ReadToEnd() + "\n" + proc.StandardError.ReadToEnd();
        return (proc.ExitCode, output);
    }

    [Fact]
    public void Random_requires_file_path()
    {
        var (exitCode, output) = RunCli("--random");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void List_requires_file_path()
    {
        var (exitCode, output) = RunCli("--list");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parent_requires_two_paths()
    {
        var (exitCode, output) = RunCli("--parent", "child.chd");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Toc_requires_file_path()
    {
        var (exitCode, output) = RunCli("--toc");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cue_requires_file_path()
    {
        var (exitCode, output) = RunCli("--cue");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_requires_file_path()
    {
        var (exitCode, output) = RunCli("--classify");
        Assert.Contains("requires", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Toc_output_contains_track_info()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var (exitCode, output) = RunCli("--toc", $"\"{path}\"");
        Assert.Equal(0, exitCode);
        Assert.Contains("Track", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cue_output_contains_index()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var (exitCode, output) = RunCli("--cue", $"\"{path}\"");
        Assert.Equal(0, exitCode);
        Assert.Contains("INDEX 01", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_for_cd_returns_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        if (!File.Exists(path)) return;

        var (exitCode, output) = RunCli("--classify", $"\"{path}\"");
        Assert.Equal(0, exitCode);
        Assert.Contains("cd", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_for_nonexistent_reports_failure()
    {
        var (exitCode, output) = RunCli("--classify", @"Z:\no\such\file.chd");
        Assert.Equal(0, exitCode);
        Assert.Contains("Classify failed", output, StringComparison.Ordinal);
    }
}
