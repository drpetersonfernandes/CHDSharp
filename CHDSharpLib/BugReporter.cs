using System.Runtime.CompilerServices;
using CHDSharp.Models;
using Microsoft.Extensions.Logging;

namespace CHDSharp;

/// <summary>Manages automatic bug reporting. Reports decompression and codec errors to the bug report API (rate-limited, deduplicated). Set <see cref="Enabled"/> to <c>false</c> to disable.</summary>
public static class BugReporter
{
    private static readonly ILogger Log = ChdLogger.GetLogger(nameof(BugReporter));

    private static readonly Action<ILogger, string, Exception?> LogSendFailed =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1), "BugReporter send failed: {Error}");

    private static readonly BugReportApiSender Sender = new();

    private static readonly object Lock = new();

    private static string _lastReportKey = "";
    private static DateTime _lastReportTime = DateTime.MinValue;

    private const int RateLimitSeconds = 6;    // 10 req/min = 1 per 6 seconds
    private const int DedupWindowMinutes = 5;

    /// <summary>Set to <c>false</c> to disable automatic bug reporting. Default is <c>true</c>.</summary>
    public static bool Enabled { get; set; } = true;

    internal static void TryReport(ChdError error, string? fileName, uint? version,
        Exception? exception, [CallerMemberName] string? callerMethod = null)
    {
        if (!Enabled)
            return;

        if (!ShouldReport(error))
            return;

        var fileTag = fileName ?? "(unknown)";
        var dedupKey = $"{error}|{fileTag}";

        if (!TryAcquireSlot(dedupKey))
            return;

        BuildAndSend(error, fileTag, version, exception, callerMethod);
    }

    private static bool ShouldReport(ChdError error)
    {
        return error switch
        {
            ChdError.Chderrdecompressionerror => true,
            ChdError.Chderrinvaliddata => true,
            ChdError.Chderrcodecerror => true,
            _ => false
        };
    }

    private static bool TryAcquireSlot(string dedupKey)
    {
        lock (Lock)
        {
            var now = DateTime.UtcNow;

            if (_lastReportKey == dedupKey &&
                (now - _lastReportTime).TotalMinutes < DedupWindowMinutes)
                return false;

            if ((now - _lastReportTime).TotalSeconds < RateLimitSeconds)
                return false;

            _lastReportKey = dedupKey;
            _lastReportTime = now;
            return true;
        }
    }

    private static void BuildAndSend(ChdError error, string fileName, uint? version,
        Exception? exception, string? callerMethod)
    {
        var errorSection = $"""
            === Error Details ===
            File: {fileName}
            CHD Version: {(version.HasValue ? $"V{version}" : "unknown")}
            Error: {error} - {error.GetMessage()}
            Caller: {callerMethod ?? "unknown"}
            """;

        var exceptionSection = exception != null
            ? $"""
                === Exception Details ===
                Type: {exception.GetType().FullName}
                Message: {exception.Message}
                Source: {exception.Source}
                StackTrace:
                {exception}
                """
            : "=== Exception Details ===\n(no exception available)";

        var stackTrace = exception?.ToString();
        if (stackTrace is { Length: > 8000 })
        {
            stackTrace = stackTrace[..7997] + "...";
        }

        var message = BugReportContext.BuildMessage(errorSection, exceptionSection);

        var report = new BugReport(
            message,
            "CHDSharp",
            BugReportContext.LibVersion,
            BugReportContext.UserInfoShort,
            BugReportContext.EnvironmentName,
            stackTrace ?? "(no stack trace)"
        );

        // Fire-and-forget. Never let bug reporting block or throw.
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await Sender.SendAsync(report, cts.Token);
            }
            catch (Exception ex)
            {
                LogSendFailed(Log, ex.Message, null);
            }
        });
    }
}
