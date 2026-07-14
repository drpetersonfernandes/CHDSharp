namespace CHDSharp.Flac.FlacDeps;

/// <summary>
/// Represents an audio source that provides samples for decoding.
/// </summary>
public interface IAudioSource
{
    /// <summary>
    /// Gets the decoder settings used to open this source.
    /// </summary>
    IAudioDecoderSettings Settings { get; }

    /// <summary>
    /// Gets the PCM configuration of the audio source.
    /// </summary>
    AudioPCMConfig PCM { get; }
    /// <summary>
    /// Gets the path to the audio file.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Gets the total duration of the audio source.
    /// </summary>
    TimeSpan Duration { get; }
    /// <summary>
    /// Gets the total length of the audio source in samples.
    /// </summary>
    long Length { get; }
    /// <summary>
    /// Gets or sets the current playback position in samples.
    /// </summary>
    long Position { get; set; }
    /// <summary>
    /// Gets the number of samples remaining from the current position.
    /// </summary>
    long Remaining { get; }

    /// <summary>
    /// Reads audio data into the specified buffer.
    /// </summary>
    /// <param name="buffer">The audio buffer to fill.</param>
    /// <param name="maxLength">The maximum number of samples to read.</param>
    /// <returns>The number of samples actually read.</returns>
    int Read(AudioBuffer buffer, int maxLength);
    /// <summary>
    /// Closes the audio source and releases resources.
    /// </summary>
    void Close();
}

/// <summary>
/// Represents a single audio title with metadata such as chapters, codec, and language.
/// </summary>
/// <summary>
/// Represents a single audio title with metadata such as chapters, codec, and language.
/// </summary>
public interface IAudioTitle
{
    /// <summary>
    /// Gets the list of chapter time positions.
    /// </summary>
    List<TimeSpan> Chapters { get; }
    /// <summary>
    /// Gets the PCM configuration for this title.
    /// </summary>
    AudioPCMConfig PCM { get; }
    /// <summary>
    /// Gets the codec name for this title.
    /// </summary>
    string Codec { get; }
    /// <summary>
    /// Gets the language of this title.
    /// </summary>
    string Language { get; }
    /// <summary>
    /// Gets the stream identifier for this title.
    /// </summary>
    int StreamId { get; }
    //IAudioSource Open { get; }
}

/// <summary>
/// Represents a collection of audio titles.
/// </summary>
public interface IAudioTitleSet
{
    /// <summary>
    /// Gets the list of audio titles in this set.
    /// </summary>
    List<IAudioTitle> AudioTitles { get; }
}

/// <summary>
/// Extension methods for <see cref="IAudioTitle"/>.
/// </summary>
public static class IAudioTitleExtensions
{
    /// <summary>
    /// Gets the total duration of the audio title, determined by its last chapter position.
    /// </summary>
    /// <param name="title">The audio title.</param>
    /// <returns>The total duration as a <see cref="TimeSpan"/>.</returns>
    public static TimeSpan GetDuration(this IAudioTitle title)
    {
        var chapters = title.Chapters;
        return chapters[chapters.Count - 1];
    }


    /// <summary>
    /// Gets a human-readable string representation of the sample rate (e.g., "44.1KHz").
    /// </summary>
    /// <param name="title">The audio title.</param>
    /// <returns>A formatted sample rate string.</returns>
    public static string GetRateString(this IAudioTitle title)
    {
        var sr = title.PCM.SampleRate;
        if (sr % 1000 == 0) return $"{sr / 1000}KHz";
        if (sr % 100 == 0) return $"{sr / 100}.{sr / 100 % 10}KHz";

        return $"{sr}Hz";
    }

    /// <summary>
    /// Gets a human-readable string representation of the audio format (e.g., "mono", "stereo", "multi-channel").
    /// </summary>
    /// <param name="title">The audio title.</param>
    /// <returns>A formatted channel format string.</returns>
    public static string GetFormatString(this IAudioTitle title)
    {
        switch (title.PCM.ChannelCount)
        {
            case 1: return "mono";
            case 2: return "stereo";
            default: return "multi-channel";
        }
    }
}

/// <summary>
/// Default implementation of <see cref="IAudioTitle"/> that wraps a single <see cref="IAudioSource"/>.
/// </summary>
public class SingleAudioTitle : IAudioTitle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SingleAudioTitle"/> class.
    /// </summary>
    /// <param name="source">The audio source to wrap.</param>
    public SingleAudioTitle(IAudioSource source) { this.source = source; }
    /// <summary>
    /// Gets the chapters, starting at zero and ending at the source duration.
    /// </summary>
    public List<TimeSpan> Chapters => [TimeSpan.Zero, source.Duration];
    /// <summary>
    /// Gets the PCM configuration from the source.
    /// </summary>
    public AudioPCMConfig PCM => source.PCM;
    /// <summary>
    /// Gets the codec name from the source settings extension.
    /// </summary>
    public string Codec => source.Settings.Extension;
    /// <summary>
    /// Gets the language (empty by default).
    /// </summary>
    public string Language => "";
    /// <summary>
    /// Gets the stream identifier (always 0).
    /// </summary>
    public int StreamId => 0;
    IAudioSource source;
}

/// <summary>
/// Default implementation of <see cref="IAudioTitleSet"/> that wraps a single <see cref="IAudioSource"/> as one title.
/// </summary>
public class SingleAudioTitleSet : IAudioTitleSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SingleAudioTitleSet"/> class.
    /// </summary>
    /// <param name="source">The audio source to wrap.</param>
    public SingleAudioTitleSet(IAudioSource source) { this.source = source; }
    /// <summary>
    /// Gets the list containing a single audio title created from the source.
    /// </summary>
    public List<IAudioTitle> AudioTitles => [new SingleAudioTitle(source)];
    IAudioSource source;
}