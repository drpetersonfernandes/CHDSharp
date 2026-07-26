using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace CHDSharpTester.Models;

/// <summary>Represents a single CHD file selected for testing, with derived convenience properties for display.</summary>
public class ChdFileEntry : INotifyPropertyChanged
{
    private string _filePath = string.Empty;

    /// <summary>Gets or sets the full path to the CHD file on disk.</summary>
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (!string.Equals(_filePath, value, StringComparison.Ordinal))
            {
                _filePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(FileSize));
            }
        }
    }

    /// <summary>Gets the file name (without directory) derived from <see cref="FilePath"/>.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Gets a human-readable file size string derived from the file's length on disk.</summary>
    public string FileSize
    {
        get
        {
            try
            {
                var fi = new FileInfo(FilePath);
                return fi.Length switch
                {
                    < 1024 => $"{fi.Length} B",
                    < 1024 * 1024 => $"{fi.Length / 1024.0:F1} KB",
                    < 1024L * 1024 * 1024 => $"{fi.Length / (1024.0 * 1024):F1} MB",
                    _ => $"{fi.Length / (1024.0 * 1024 * 1024):F2} GB"
                };
            }
            catch (FileNotFoundException)
            {
                return "N/A";
            }
            catch (IOException)
            {
                return "N/A";
            }
        }
    }

    /// <summary>Occurs when a property value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises the <see cref="PropertyChanged"/> event.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
