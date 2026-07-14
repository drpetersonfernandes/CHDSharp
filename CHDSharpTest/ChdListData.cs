namespace CHDSharp.Tests;

/// <summary>
/// Loads the CHD paths from References\CHD list.txt for data-driven tests.
/// Entries whose files are not present on the current machine are still yielded;
/// the individual test skips gracefully when the file is missing.
/// </summary>
internal static class ChdListData
{
    /// <summary>All CHD files found in the test folder, or from the list file as fallback.</summary>
    public static IReadOnlyList<string> AllPaths()
    {
        var folder = TestPaths.ChdFolder;
        if (folder != null && Directory.Exists(folder))
            return Directory.GetFiles(folder, "*.chd", SearchOption.TopDirectoryOnly)
                .Select(static f => new FileInfo(f))
                .OrderBy(static fi => fi.Length)
                .Select(static fi => fi.FullName)
                .ToList();

        var listFile = TestPaths.ChdListFile;
        if (listFile == null || !File.Exists(listFile))
            return [];

        return File.ReadAllLines(listFile)
            .Select(l => l.Trim().Trim('"'))
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>xUnit MemberData source: one object[] per listed path.</summary>
    public static IEnumerable<object[]> Paths()
    {
        var paths = AllPaths();
        if (paths.Count == 0)
        {
            // Yield a single sentinel so the theory shows up (and skips) even
            // when the list file itself is absent.
            yield return [null];

            yield break;
        }
        foreach (var p in paths)
            yield return [p];
    }

    /// <summary>xUnit MemberData source: only files under 1 GB (for expensive per-range tests).</summary>
    public static IEnumerable<object[]> SmallPaths()
    {
        IReadOnlyList<string> paths = AllPaths()
            .Where(p => new FileInfo(p).Length < 1_000_000_000L)
            .ToList();
        if (paths.Count == 0)
        {
            yield return [null];

            yield break;
        }
        foreach (var p in paths)
            yield return [p];
    }
}
