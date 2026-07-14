using System.ComponentModel;
using CHDSharp.Flac.FlacDeps;

//using Newtonsoft.Json;

namespace CHDSharp.Flac;

//[JsonObject(MemberSerialization.OptIn)]
public class DecoderSettings : IAudioDecoderSettings
{
    #region IAudioDecoderSettings implementation
    [Browsable(false)]
    public string Extension => "flac";

    [Browsable(false)]
    public string Name => "cuetools";

    [Browsable(false)]
    public Type DecoderType => typeof(AudioDecoder);

    [Browsable(false)]
    public int Priority => 2;

    public IAudioDecoderSettings Clone()
    {
        return MemberwiseClone() as IAudioDecoderSettings;
    }
    #endregion

    public DecoderSettings()
    {
        this.Init();
    }
}