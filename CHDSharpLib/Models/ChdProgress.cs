namespace CHDSharp.Models;

/// <summary>
/// Progress snapshot reported by long-running CHD operations. Pass an
/// <see cref="IProgress{T}"/> of this type to <see cref="CHDSharp.Chd.CheckFile(Stream,string,bool,IProgress{CHDSharp.Models.ChdProgress}?,System.Threading.CancellationToken)"/>,
/// <see cref="CHDSharp.Chd.CheckFileWithParent(string,string?,IProgress{CHDSharp.Models.ChdProgress}?,System.Threading.CancellationToken)"/>,
/// <see cref="CHDSharp.ChdFile.ReadAllBytes(out byte[],IProgress{CHDSharp.Models.ChdProgress}?,System.Threading.CancellationToken)"/>,
/// <see cref="CHDSharp.ChdFile.EnumerateHunks(IProgress{CHDSharp.Models.ChdProgress}?)"/>, or
/// <see cref="CHDSharp.ChdFile.ExtractToDirectory(string,string,IProgress{CHDSharp.Models.ChdProgress}?,System.Threading.CancellationToken)"/>
/// to receive a report after every decompressed hunk. Callers commonly wrap this in
/// <c>new Progress&lt;ChdProgress&gt;(...)></c> for UI binding or logging.
/// </summary>
/// <example>
/// <code>
/// var progress = new Progress&lt;ChdProgress&gt;(p =>
///     Console.WriteLine($"{p.Percent:F0}% — {p.BytesProcessed:N0}/{p.TotalBytes:N0} bytes, {p.Elapsed.TotalSeconds:F1}s"));
///
/// var result = Chd.CheckFile(File.OpenRead("game.chd"), "game.chd", deepCheck: true, progress);
/// </code>
/// </example>
public sealed record ChdProgress
{
    /// <summary>
    /// Number of hunks processed so far (1-based; equals <see cref="TotalHunks"/> when the
    /// operation has finished). Zero-based hunk indices are <c>CurrentHunk - 1</c>.
    /// </summary>
    public long CurrentHunk { get; }

    /// <summary>Total number of hunks in the image (<c>0</c> for a degenerate/empty image).</summary>
    public long TotalHunks { get; }

    /// <summary>Number of decompressed image bytes processed so far.</summary>
    public long BytesProcessed { get; }

    /// <summary>Total decompressed size of the image in bytes.</summary>
    public long TotalBytes { get; }

    /// <summary>Wall-clock time elapsed since the operation started.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Percentage of hunks completed (<c>0</c>–<c>100</c>).</summary>
    public double Percent => TotalHunks == 0 ? 100.0 : Math.Min(100.0, CurrentHunk * 100.0 / TotalHunks);

    /// <summary>Creates a progress snapshot with the given values.</summary>
    /// <param name="currentHunk">Number of hunks processed so far (1-based).</param>
    /// <param name="totalHunks">Total number of hunks in the image.</param>
    /// <param name="bytesProcessed">Number of decompressed bytes processed so far.</param>
    /// <param name="totalBytes">Total decompressed size of the image.</param>
    /// <param name="elapsed">Wall-clock time elapsed since the operation started.</param>
    public ChdProgress(long currentHunk, long totalHunks, long bytesProcessed, long totalBytes, TimeSpan elapsed)
    {
        CurrentHunk = currentHunk;
        TotalHunks = totalHunks;
        BytesProcessed = bytesProcessed;
        TotalBytes = totalBytes;
        Elapsed = elapsed;
    }

    /// <summary>Returns a human-readable summary such as "42/100 hunks, 12.5% (5.2s)".</summary>
    public override string ToString()
    {
        return $"{CurrentHunk}/{TotalHunks} hunks, {Percent:F1}% ({Elapsed.TotalSeconds:F1}s)";
    }
}