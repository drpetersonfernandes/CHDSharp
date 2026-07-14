using System.ComponentModel;
using CHDSharp.Models.Flac.FlacDeps;

namespace CHDSharp.Interfaces.Flac.FlacDeps;

/// <summary>
/// Settings for an audio decoder instance.
/// </summary>
public interface IAudioDecoderSettings
{
    /// <summary>
    /// Gets the human-readable name of the decoder.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the file extension associated with the decoder.
    /// </summary>
    string Extension { get; }

    /// <summary>
    /// Gets the <see cref="Type"/> of the decoder implementation.
    /// </summary>
    Type DecoderType { get; }

    /// <summary>
    /// Gets the priority of the decoder (lower values indicate higher priority).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Creates a deep copy of the settings.
    /// </summary>
    IAudioDecoderSettings Clone();
}

/// <summary>
/// Extension methods for <see cref="IAudioDecoderSettings"/>.
/// </summary>
public static class IAudioDecoderSettingsExtensions
{
    /// <summary>
    /// Determines whether the settings have any properties with browsable attributes.
    /// </summary>
    /// <param name="settings">The decoder settings to inspect.</param>
    /// <returns><c>true</c> if at least one property has a browsable attribute; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Resets all properties of the settings to their default values.
    /// </summary>
    /// <param name="settings">The decoder settings to initialize.</param>
    public static void Init(this IAudioDecoderSettings settings)
    {
        // Iterate through each property and call ResetValue()
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(settings))
            property.ResetValue(settings);
    }

    /// <summary>
    /// Opens an audio source using the specified settings and file path.
    /// </summary>
    /// <param name="settings">The decoder settings used to create the source.</param>
    /// <param name="path">The path to the audio file.</param>
    /// <param name="IO">An optional stream to use for reading (if supported by the decoder).</param>
    /// <returns>A new <see cref="IAudioSource"/> instance created by the decoder.</returns>
    public static IAudioSource Open(this IAudioDecoderSettings settings, string path, Stream IO = null)
    {
        return Activator.CreateInstance(settings.DecoderType, settings, path, IO) as IAudioSource;
    }
}
