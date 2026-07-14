namespace CHDSharp.Flac.FlacDeps;

/// <summary>
/// Represents the configuration of an audio PCM stream, including sample rate, bit depth, channel count, and speaker layout.
/// </summary>
public class AudioPCMConfig
{
    /// <summary>
    /// Flags representing speaker positions and predefined speaker configurations.
    /// </summary>
    public enum SpeakerConfig
    {
        SPEAKER_FRONT_LEFT = 0x1,
        SPEAKER_FRONT_RIGHT = 0x2,
        SPEAKER_FRONT_CENTER = 0x4,
        SPEAKER_LOW_FREQUENCY = 0x8,
        SPEAKER_BACK_LEFT = 0x10,
        SPEAKER_BACK_RIGHT = 0x20,
        SPEAKER_FRONT_LEFT_OF_CENTER = 0x40,
        SPEAKER_FRONT_RIGHT_OF_CENTER = 0x80,
        SPEAKER_BACK_CENTER = 0x100,
        SPEAKER_SIDE_LEFT = 0x200,
        SPEAKER_SIDE_RIGHT = 0x400,
        SPEAKER_TOP_CENTER = 0x800,
        SPEAKER_TOP_FRONT_LEFT = 0x1000,
        SPEAKER_TOP_FRONT_CENTER = 0x2000,
        SPEAKER_TOP_FRONT_RIGHT = 0x4000,
        SPEAKER_TOP_BACK_LEFT = 0x8000,
        SPEAKER_TOP_BACK_CENTER = 0x10000,
        SPEAKER_TOP_BACK_RIGHT = 0x20000,

        DIRECTOUT = 0,
        KSAUDIO_SPEAKER_MONO = SPEAKER_FRONT_CENTER,
        KSAUDIO_SPEAKER_STEREO = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        KSAUDIO_SPEAKER_QUAD = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        KSAUDIO_SPEAKER_SURROUND = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER | SPEAKER_BACK_CENTER,
        KSAUDIO_SPEAKER_5POINT1 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        KSAUDIO_SPEAKER_5POINT1_SURROUND = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY | SPEAKER_SIDE_LEFT | SPEAKER_SIDE_RIGHT,
        KSAUDIO_SPEAKER_7POINT1 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT | SPEAKER_FRONT_LEFT_OF_CENTER | SPEAKER_FRONT_RIGHT_OF_CENTER,
        KSAUDIO_SPEAKER_7POINT1_SURROUND = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT | SPEAKER_SIDE_LEFT | SPEAKER_SIDE_RIGHT,

        DVDAUDIO_GR1_0 = SPEAKER_FRONT_CENTER,
        DVDAUDIO_GR1_1 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_2 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_3 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_4 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_5 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_6 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_7 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_8 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_9 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_10 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_11 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_12 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
        DVDAUDIO_GR1_13 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER,
        DVDAUDIO_GR1_14 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER,
        DVDAUDIO_GR1_15 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER,
        DVDAUDIO_GR1_16 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER,
        DVDAUDIO_GR1_17 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER,
        DVDAUDIO_GR1_18 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        DVDAUDIO_GR1_19 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        DVDAUDIO_GR1_20 = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,

        DVDAUDIO_GR2_0 = 0,
        DVDAUDIO_GR2_1 = 0,
        DVDAUDIO_GR2_2 = SPEAKER_BACK_CENTER,
        DVDAUDIO_GR2_3 = SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        DVDAUDIO_GR2_4 = SPEAKER_LOW_FREQUENCY,
        DVDAUDIO_GR2_5 = SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_CENTER,
        DVDAUDIO_GR2_6 = SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        DVDAUDIO_GR2_7 = SPEAKER_FRONT_CENTER,
        DVDAUDIO_GR2_8 = SPEAKER_FRONT_CENTER | SPEAKER_BACK_CENTER,
        DVDAUDIO_GR2_9 = SPEAKER_FRONT_CENTER | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        DVDAUDIO_GR2_10 = SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY,
        DVDAUDIO_GR2_11 = SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_CENTER,
        DVDAUDIO_GR2_12 = SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        DVDAUDIO_GR2_13 = SPEAKER_BACK_CENTER,
        DVDAUDIO_GR2_14 = SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        DVDAUDIO_GR2_15 = SPEAKER_LOW_FREQUENCY,
        DVDAUDIO_GR2_16 = SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_CENTER,
        DVDAUDIO_GR2_17 = SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT,
        DVDAUDIO_GR2_18 = SPEAKER_LOW_FREQUENCY,
        DVDAUDIO_GR2_19 = SPEAKER_FRONT_CENTER,
        DVDAUDIO_GR2_20 = SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY,
    }

    private int _bitsPerSample;
    private int _channelCount;
    private int _sampleRate;
    private SpeakerConfig _channelMask;

    /// <summary>
    /// Gets the number of bits per sample.
    /// </summary>
    public int BitsPerSample { get { return _bitsPerSample; } }
    /// <summary>
    /// Gets the number of channels.
    /// </summary>
    public int ChannelCount { get { return _channelCount; } }
    /// <summary>
    /// Gets the sample rate in Hz.
    /// </summary>
    public int SampleRate { get { return _sampleRate; } }
    /// <summary>
    /// Gets the block alignment in bytes (calculated from channel count and bits per sample).
    /// </summary>
    public int BlockAlign { get { return _channelCount * ((_bitsPerSample + 7) / 8); } }
    /// <summary>
    /// Gets the speaker channel mask.
    /// </summary>
    public SpeakerConfig ChannelMask { get { return _channelMask; } }
    /// <summary>
    /// Gets a value indicating whether the configuration matches Red Book audio CD format (16-bit, 2-channel, 44100 Hz).
    /// </summary>
    public bool IsRedBook { get { return _bitsPerSample == 16 && _channelCount == 2 && _sampleRate == 44100; } }
    /// <summary>
    /// Counts the number of set bits in the speaker mask, which represents the number of channels.
    /// </summary>
    /// <param name="mask">The speaker configuration mask.</param>
    /// <returns>The number of channels represented by the mask.</returns>
    public static int ChannelsInMask(SpeakerConfig mask)
    {
        var count = 0;
        while (mask != 0)
        {
            count++;
            mask &= mask - 1;
        }
        return count;
    }

    /// <summary>
    /// Gets the default speaker configuration mask for a given channel count.
    /// </summary>
    /// <param name="channelCount">The number of audio channels.</param>
    /// <returns>A <see cref="SpeakerConfig"/> value representing the default speaker layout.</returns>
    public static SpeakerConfig GetDefaultChannelMask(int channelCount)
    {
        switch (channelCount)
        {
            case 1:
                return SpeakerConfig.KSAUDIO_SPEAKER_MONO;
            case 2:
                return SpeakerConfig.KSAUDIO_SPEAKER_STEREO;
            case 3:
                return SpeakerConfig.KSAUDIO_SPEAKER_STEREO | SpeakerConfig.SPEAKER_LOW_FREQUENCY;
            case 4:
                return SpeakerConfig.KSAUDIO_SPEAKER_QUAD;
            case 5:
                //return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1 & ~SpeakerConfig.SPEAKER_LOW_FREQUENCY;
                return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1_SURROUND & ~SpeakerConfig.SPEAKER_LOW_FREQUENCY;
            case 6:
                //return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1;
                return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1_SURROUND;
            case 7:
                return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1_SURROUND | SpeakerConfig.SPEAKER_BACK_CENTER;
            case 8:
                return SpeakerConfig.KSAUDIO_SPEAKER_7POINT1_SURROUND;
        }
        return (SpeakerConfig)((1 << channelCount) - 1);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPCMConfig"/> class.
    /// </summary>
    /// <param name="bitsPerSample">The number of bits per sample.</param>
    /// <param name="channelCount">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate in Hz.</param>
    /// <param name="channelMask">The speaker configuration mask. If <see cref="SpeakerConfig.DIRECTOUT"/>, a default mask is assigned based on channel count.</param>
    public AudioPCMConfig(int bitsPerSample, int channelCount, int sampleRate, SpeakerConfig channelMask = SpeakerConfig.DIRECTOUT)
    {
        _bitsPerSample = bitsPerSample;
        _channelCount = channelCount;
        _sampleRate = sampleRate;
        _channelMask = channelMask == 0 ? GetDefaultChannelMask(channelCount) : channelMask;
    }
}