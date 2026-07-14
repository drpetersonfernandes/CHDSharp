using CHDSharp.Interfaces.Flac.FlacDeps;

namespace CHDSharp.Models.Flac.FlacDeps;

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

    private readonly IAudioSource source;
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

    private readonly IAudioSource source;
}
