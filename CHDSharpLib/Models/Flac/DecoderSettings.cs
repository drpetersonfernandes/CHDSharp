using System.ComponentModel;
using CHDSharp.Flac;
using CHDSharp.Interfaces.Flac.FlacDeps;

namespace CHDSharp.Models.Flac;

/// <summary>
/// FLAC decoder settings implementing <see cref="IAudioDecoderSettings"/>.
/// Configured for the "cuetools" FLAC decoder with a priority of 2.
/// </summary>
public class DecoderSettings : IAudioDecoderSettings
{
    #region IAudioDecoderSettings implementation

    /// <summary>Gets the file extension associated with this decoder ("flac").</summary>
    [Browsable(false)] public string Extension => "flac";

    /// <summary>Gets the human-readable name of this decoder ("cuetools").</summary>
    [Browsable(false)] public string Name => "cuetools";

    /// <summary>Gets the <see cref="Type"/> of the decoder implementation (<see cref="AudioDecoder"/>).</summary>
    [Browsable(false)] public Type DecoderType => typeof(AudioDecoder);

    /// <summary>Gets the priority of this decoder (2). Lower values indicate higher priority.</summary>
    [Browsable(false)] public int Priority => 2;

    /// <summary>
    /// Creates a shallow copy of the decoder settings.
    /// </summary>
    /// <returns>A new <see cref="IAudioDecoderSettings"/> instance with the same values.</returns>
    public IAudioDecoderSettings Clone()
    {
        return (IAudioDecoderSettings)MemberwiseClone();
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="DecoderSettings"/> class.
    /// </summary>
    public DecoderSettings()
    {
        this.Init();
    }
}
