namespace CHDSharpTester.Models;

/// <summary>Represents the aggregated results of an entire test session across all tested CHD files.</summary>
public class TestSessionResult
{
    /// <summary>Gets or sets the per-file results for all files in the session.</summary>
    public List<PerFileResult> FileResults { get; set; } = [];

    /// <summary>Gets the total number of files tested in the session.</summary>
    public int TotalFiles => FileResults.Count;

    /// <summary>Gets the number of files that passed all their sub-tests.</summary>
    public int PassedFiles => FileResults.Count(r => r.AllPassed);

    /// <summary>Gets the number of files that had at least one failing sub-test or no passing tests.</summary>
    public int FailedFiles => FileResults.Count(r => r.Failed > 0 || r.Passed == 0);

    /// <summary>Gets the number of files where all sub-tests were skipped.</summary>
    public int SkippedFiles => FileResults.Count(r => r is { Skipped: > 0, Passed: 0, Failed: 0 });

    /// <summary>Gets the total number of sub-tests executed across all files (excluding skipped).</summary>
    public int TotalSubTests => FileResults.Sum(r => r.SubTests.Count(t => t.Status != TestStatus.Skipped));

    /// <summary>Gets the total number of sub-tests that passed across all files.</summary>
    public int PassedSubTests => FileResults.Sum(r => r.Passed);

    /// <summary>Gets the total number of sub-tests that failed across all files.</summary>
    public int FailedSubTests => FileResults.Sum(r => r.Failed);

    /// <summary>Gets the total number of sub-tests that were skipped across all files.</summary>
    public int SkippedSubTests => FileResults.Sum(r => r.Skipped);

    /// <summary>Gets the total elapsed time for the entire session, in seconds.</summary>
    public double TotalElapsedSeconds => FileResults.Sum(r => r.ElapsedSeconds);
}
