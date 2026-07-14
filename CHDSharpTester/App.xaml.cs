using System.IO;
using System.Windows;
using Serilog;

namespace CHDSharpTester;

/// <summary>The WPF application entry point. Configures Serilog logging on startup and flushes on exit.</summary>
public partial class App : Application
{
    /// <summary>Configures Serilog file and debug logging when the application starts.</summary>
    /// <param name="e">The startup event arguments.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CHDSharpTester", "logs", "chdsharp-tester-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("CHDSharpTester started");
    }

    /// <summary>Flushes and closes the Serilog logger when the application exits.</summary>
    /// <param name="e">The exit event arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("CHDSharpTester exiting");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
