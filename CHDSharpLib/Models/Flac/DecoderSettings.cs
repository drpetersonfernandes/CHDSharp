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

    [Browsable(false)] public string Extension => "flac";

    [Browsable(false)] public string Name => "cuetools";

    [Browsable(false)] public Type DecoderType => typeof(AudioDecoder);

    [Browsable(false)] public int Priority => 2;

    /// <summary>
    /// Creates a shallow copy of the decoder settings.
    /// </summary>
    /// <returns>A new <see cref="IAudioDecoderSettings"/> instance with the same values.</returns>
    public IAudioDecoderSettings Clone()
    {
        return MemberwiseClone() as IAudioDecoderSettings;
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
