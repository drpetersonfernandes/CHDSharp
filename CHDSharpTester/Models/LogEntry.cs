namespace CHDSharpTester.Models;

/// <summary>Represents a single log entry with a message and a timestamp for display in the log panel.</summary>
public class LogEntry
{
    /// <summary>Gets or sets the log message text.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the timestamp of the log entry in HH:mm:ss format.</summary>
    public string Timestamp { get; set; } = string.Empty;
}