namespace CHDSharp.Tests;

/// <summary>
/// Resolves paths to the repository's `References/` folder (chdman.exe and the
/// CHD list) regardless of the test working directory, and exposes helpers used
/// by the integration tests.
/// </summary>
internal static class TestPaths
{
    private static readonly Lazy<string> _repoRoot = new(FindRepoRoot);

    /// <summary>Repository root (folder containing the .sln), or null if not found.</summary>
    public static string RepoRoot => _repoRoot.Value;

    /// <summary>Full path to chdman.exe.</summary>
    public static string ChdmanExe =>
        @"C:\Users\Peterson\Documents\Sincronizar\source\repos\CSharp_CHDSharp\chdman.exe";

    /// <summary>Folder containing CHD files for integration tests.</summary>
    public static string ChdFolder => @"D:\CHD";

    /// <summary>Full path to the "CHD list.txt" file (may not exist).</summary>
    public static string ChdListFile =>
        RepoRoot == null ? null : Path.Combine(RepoRoot, "References", "CHD list.txt");

    /// <summary>True if chdman.exe is available for cross-checking.</summary>
    public static bool ChdmanAvailable => ChdmanExe != null && File.Exists(ChdmanExe);

    /// <summary>A writable temp directory for building parent/child CHD pairs.</summary>
    public static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CHDSharpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        // Walk up from the test assembly location looking for the solution file.
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "CSharp_CHDSharp.sln")))
                return dir;

            var parent = Directory.GetParent(dir);
            if (parent == null)
                break;

            dir = parent.FullName;
        }

        return null;
    }
}
