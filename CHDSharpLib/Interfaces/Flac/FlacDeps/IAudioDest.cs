using CHDSharp.Models.Flac.FlacDeps;

namespace CHDSharp.Interfaces.Flac.FlacDeps;

/// <summary>
/// Represents an audio output destination that accepts sample data for writing.
/// </summary>
internal interface IAudioDest
{
    /// <summary>
    /// Gets the file path of the output destination.
    /// </summary>
    string Path { get; }
    /// <summary>
    /// Sets the final sample count after encoding is complete.
    /// </summary>
    long FinalSampleCount { set; }

    /// <summary>
    /// Writes an audio buffer to the destination.
    /// </summary>
    /// <param name="buffer">The audio buffer to write.</param>
    void Write(AudioBuffer buffer);
    /// <summary>
    /// Finalizes and closes the output destination.
    /// </summary>
    void Close();
    /// <summary>
    /// Deletes the output destination file.
    /// </summary>
    void Delete();
}
