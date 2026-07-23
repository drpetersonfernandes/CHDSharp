using System.Diagnostics;

namespace CHDSharp.Tests;

public sealed class CliIntegrationTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

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
                var slnRoot = baseDir.Substring(0, testBinIdx);
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
    public void Toc_command_produces_toc_for_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        var (exitCode, output) = RunCli("--toc", $"\"{path}\"");

        Assert.Equal(0, exitCode);
        // Serilog debug output may prepend metadata dump; check the actual TOC content
        Assert.Contains("CD-ROM", output);
        Assert.Contains("MODE1/2048", output);
        Assert.Contains("AUDIO", output);
        Assert.Contains("Track", output);
    }

    [Fact]
    public void Toc_command_for_raw_returns_no_tracks_message()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        var (exitCode, output) = RunCli("--toc", $"\"{path}\"");

        Assert.Equal(0, exitCode);
        Assert.Contains("No CD/GD-ROM track metadata", output);
    }

    [Fact]
    public void Cue_command_produces_cue_for_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        var (exitCode, output) = RunCli("--cue", $"\"{path}\"");

        Assert.Equal(0, exitCode);
        Assert.Contains("TRACK 01 MODE1/2048", output);
        Assert.Contains("TRACK 02 AUDIO", output);
        Assert.Contains("INDEX 01 00:00:00", output);
    }

    [Fact]
    public void Cue_command_accepts_custom_bin_name()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        var (exitCode, output) = RunCli("--cue", $"\"{path}\"", "custom.bin");

        Assert.Equal(0, exitCode);
        Assert.Contains("FILE \"custom.bin\"", output);
    }

    [Fact]
    public void Cue_command_for_raw_fails_gracefully()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        var (exitCode, output) = RunCli("--cue", $"\"{path}\"");

        Assert.Equal(0, exitCode);
        Assert.Contains("CUE generation failed", output);
    }

    [Fact]
    public void Classify_command_returns_cd_for_cd()
    {
        var path = Path.Combine(TestDataDir, "v5_cd_default.chd");
        var (exitCode, output) = RunCli("--classify", $"\"{path}\"");

        Assert.Equal(0, exitCode);
        Assert.Contains("cd", output);
    }

    [Fact]
    public void Classify_command_returns_unknown_for_raw()
    {
        var path = Path.Combine(TestDataDir, "v5_zlib.chd");
        var (exitCode, output) = RunCli("--classify", $"\"{path}\"");

        Assert.Equal(0, exitCode);
        Assert.Contains("unknown/raw", output);
    }

    [Fact]
    public void Classify_command_reports_file_not_found()
    {
        var path = Path.Combine(TestDataDir, "nonexistent.chd");
        var (exitCode, output) = RunCli("--classify", $"\"{path}\"");

        Assert.Equal(0, exitCode);
        Assert.Contains("Classify failed", output);
    }

    [Fact]
    public void Usage_shows_new_commands()
    {
        var (exitCode, output) = RunCli();

        Assert.Equal(0, exitCode);
        Assert.Contains("--toc", output);
        Assert.Contains("--cue", output);
        Assert.Contains("--classify", output);
    }
}
