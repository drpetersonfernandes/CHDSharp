namespace CHDSharpTester.Models;

/// <summary>Holds the aggregated test results for a single CHD file, including per-test outcomes and timing.</summary>
public class PerFileResult
{
    /// <summary>Gets or sets the display name of the tested file.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the full path of the tested file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable size of the tested file.</summary>
    public string FileSize { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of sub-test results executed against this file.</summary>
    public List<SubTestResult> SubTests { get; set; } = [];

    /// <summary>Gets the count of sub-tests that passed.</summary>
    public int Passed => SubTests.Count(t => t.Status == TestStatus.Passed);

    /// <summary>Gets the count of sub-tests that failed.</summary>
    public int Failed => SubTests.Count(t => t.Status == TestStatus.Failed);

    /// <summary>Gets the count of sub-tests that were skipped.</summary>
    public int Skipped => SubTests.Count(t => t.Status == TestStatus.Skipped);

    /// <summary>Gets whether all executed sub-tests passed (at least one test passed and none failed).</summary>
    public bool AllPassed => Failed == 0 && Passed > 0;

    /// <summary>Gets or sets the total elapsed time for all tests on this file, in seconds.</summary>
    public double ElapsedSeconds { get; set; }
}

/// <summary>Represents the result of a single sub-test within a file's test suite.</summary>
public class SubTestResult
{
    /// <summary>Gets or sets the name of the sub-test.</summary>
    public string TestName { get; set; } = string.Empty;

    /// <summary>Gets or sets the outcome status of this sub-test.</summary>
    public TestStatus Status { get; set; }

    /// <summary>Gets or sets additional detail or diagnostic information about the test result.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>Gets or sets the elapsed time for this sub-test, in seconds.</summary>
    public double ElapsedSeconds { get; set; }
}

/// <summary>Represents the outcome status of a test or sub-test.</summary>
public enum TestStatus
{
    /// <summary>The test completed successfully.</summary>
    Passed,

    /// <summary>The test did not pass verification.</summary>
    Failed,

    /// <summary>The test was not executed (e.g., a required tool or condition was unavailable).</summary>
    Skipped
}
