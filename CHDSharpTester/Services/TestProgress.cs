namespace CHDSharpTester.Services;

/// <summary>Reports the current progress of a test session, including the current file, test, and completion status.</summary>
/// <param name="CurrentFile">The name of the file currently being tested.</param>
/// <param name="FileIndex">The 1-based index of the current file.</param>
/// <param name="TotalFiles">The total number of files in the session.</param>
/// <param name="CurrentTest">The name of the sub-test currently executing.</param>
/// <param name="StatusText">A human-readable status message for the current operation.</param>
/// <param name="IsComplete">Whether the entire session has finished.</param>
public record TestProgress(
    string CurrentFile,
    int FileIndex,
    int TotalFiles,
    string CurrentTest,
    string StatusText,
    bool IsComplete = false
);
