using System.IO;
using System.Windows;
using Serilog;

namespace CHDSharpTester;

public partial class App : Application
{
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

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("CHDSharpTester exiting");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
