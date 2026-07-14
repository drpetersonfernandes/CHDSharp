namespace CHDSharp.Models.Flac;

/// <summary>
/// Stereo encoding mode for FLAC frames.
/// </summary>
public enum ChannelMode
{
    /// <summary>Not stereo (mono).</summary>
    NotStereo = 0,

    /// <summary>Independent left/right channels.</summary>
    LeftRight = 1,

    /// <summary>Left channel and side difference (left minus right).</summary>
    LeftSide = 8,

    /// <summary>Side difference (left minus right) and right channel.</summary>
    RightSide = 9,

    /// <summary>Mid and side channels.</summary>
    MidSide = 10
}
