using System.ComponentModel;

namespace CHDSharp.Interfaces.Flac.FlacDeps;

/// <summary>
/// Settings for an audio decoder instance.
/// </summary>
internal interface IAudioDecoderSettings
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
internal static class AudioDecoderSettingsExtensions
{
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
}
