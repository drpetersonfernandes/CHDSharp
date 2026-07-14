using System.ComponentModel;
using CHDSharp.Flac.FlacDeps;

//using Newtonsoft.Json;

namespace CHDSharp.Flac;

/// <summary>
/// FLAC decoder settings implementing <see cref="IAudioDecoderSettings"/>.
/// Configured for the "cuetools" FLAC decoder with a priority of 2.
/// </summary>
//[JsonObject(MemberSerialization.OptIn)]
public class DecoderSettings : IAudioDecoderSettings
{
    #region IAudioDecoderSettings implementation
    [Browsable(false)]
    /// <summary>
    /// Gets the file extension associated with FLAC files ("flac").
    /// </summary>
    public string Extension => "flac";

    [Browsable(false)]
    /// <summary>
    /// Gets the name of this decoder implementation ("cuetools").
    /// </summary>
    public string Name => "cuetools";

    [Browsable(false)]
    /// <summary>
    /// Gets the <see cref="Type"/> of the decoder class (<see cref="AudioDecoder"/>).
    /// </summary>
    public Type DecoderType => typeof(AudioDecoder);

    [Browsable(false)]
    /// <summary>
    /// Gets the priority of this decoder relative to other implementations. Higher priority decoders are preferred.
    /// </summary>
    public int Priority => 2;

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