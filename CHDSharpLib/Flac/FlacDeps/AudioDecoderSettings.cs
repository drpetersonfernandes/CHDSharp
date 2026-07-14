using System.ComponentModel;

namespace CHDSharp.Flac.FlacDeps;

public interface IAudioDecoderSettings
{
    string Name { get; }

    string Extension { get; }

    Type DecoderType { get; }

    int Priority { get; }

    IAudioDecoderSettings Clone();
}

public static class IAudioDecoderSettingsExtensions
{
    public static bool HasBrowsableAttributes(this IAudioDecoderSettings settings)
    {
        var hasBrowsable = false;
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(settings))
        {
            var isBrowsable = true;
            foreach (var attribute in property.Attributes)
            {
                var browsable = attribute as BrowsableAttribute;
                isBrowsable &= browsable == null || browsable.Browsable;
            }
            hasBrowsable |= isBrowsable;
        }
        return hasBrowsable;
    }

    public static void Init(this IAudioDecoderSettings settings)
    {
        // Iterate through each property and call ResetValue()
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(settings))
            property.ResetValue(settings);
    }

    public static IAudioSource Open(this IAudioDecoderSettings settings, string path, Stream IO = null)
    {
        return Activator.CreateInstance(settings.DecoderType, settings, path, IO) as IAudioSource;
    }
}