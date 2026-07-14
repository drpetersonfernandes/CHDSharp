using System.IO;

namespace CHDSharpTester.Models;

/// <summary>Represents a single CHD file selected for testing, with derived convenience properties for display.</summary>
public class ChdFileEntry
{
    /// <summary>Gets or sets the full path to the CHD file on disk.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Gets the file name (without directory) derived from <see cref="FilePath"/>.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Gets a human-readable file size string derived from the file's length on disk.</summary>
    public string FileSize => new FileInfo(FilePath).Length switch
    {
        < 1024 => $"{new FileInfo(FilePath).Length} B",
        < 1024 * 1024 => $"{new FileInfo(FilePath).Length / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{new FileInfo(FilePath).Length / (1024.0 * 1024):F1} MB",
        _ => $"{new FileInfo(FilePath).Length / (1024.0 * 1024 * 1024):F2} GB"
    };

    /// <summary>Gets whether the file is under 1 GB, suitable for expensive per-range tests.</summary>
    public bool IsSmall => new FileInfo(FilePath).Length < 1_000_000_000L;
}
