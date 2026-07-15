using System.Reflection;
using System.Runtime.InteropServices;

namespace CHDSharp;

internal static class BugReportContext
{
    internal static readonly string LibVersion;
    internal static readonly string EnvironmentSection;
    internal static readonly string UserInfoShort;
    internal static readonly string EnvironmentName;

    static BugReportContext()
    {
        LibVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

#if DEBUG
        EnvironmentName = "Debug";
#else
        EnvironmentName = "Release";
#endif

        var osDesc = RuntimeInformation.OSDescription;
        var arch = RuntimeInformation.OSArchitecture.ToString();
        var bitness = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        var procCount = Environment.ProcessorCount;
        var baseDir = AppContext.BaseDirectory;
        var tempPath = Path.GetTempPath();

        UserInfoShort = $"{osDesc} {arch}";

        EnvironmentSection = $"""
            === Environment Details ===
            Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            Application Name: CHDSharp
            Application Version: {LibVersion}
            OS Version: {osDesc}
            Architecture: {arch}
            Bitness: {bitness}
            Windows Version: {osDesc}
            Processor Count: {procCount}
            Base Directory: {baseDir}
            Temp Path: {tempPath}
            """;
    }

    internal static string BuildMessage(string errorSection, string exceptionSection)
    {
        var msg = $"""
            {EnvironmentSection}

            {errorSection}

            {exceptionSection}
            """;

        if (msg.Length > 4000)
        {
            msg = msg[..3997] + "...";
        }

        return msg;
    }
}
