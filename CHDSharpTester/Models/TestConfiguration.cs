namespace CHDSharpTester.Models;

/// <summary>Holds the configuration for a test session, including the chdman executable path and the list of files to test.</summary>
public class TestConfiguration
{
    /// <summary>Gets or sets the full path to the chdman executable used for cross-validation.</summary>
    public string ChdmanExePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of CHD file entries to include in the test session.</summary>
    public List<ChdFileEntry> Files { get; set; } = [];
}
