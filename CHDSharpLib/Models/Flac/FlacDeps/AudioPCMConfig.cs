namespace CHDSharp.Models.Flac.FlacDeps;

/// <summary>
/// Represents the configuration of an audio PCM stream, including sample rate, bit depth, channel count, and speaker layout.
/// </summary>
public class AudioPcmConfig
{
    /// <summary>
    /// Flags representing speaker positions and predefined speaker configurations.
    /// </summary>
    [Flags]
    public enum SpeakerConfig
    {
#pragma warning disable CA1069 // Duplicate enum values are intentional flags aliases
        Speakerfrontleft = 0x1,
        Speakerfrontright = 0x2,
        Speakerfrontcenter = 0x4,
        Speakerlowfrequency = 0x8,
        Speakerbackleft = 0x10,
        Speakerbackright = 0x20,
        Speakerfrontleftofcenter = 0x40,
        Speakerfrontrightofcenter = 0x80,
        Speakerbackcenter = 0x100,
        Speakersideleft = 0x200,
        Speakersideright = 0x400,
        Speakertopcenter = 0x800,
        Speakertopfrontleft = 0x1000,
        Speakertopfrontcenter = 0x2000,
        Speakertopfrontright = 0x4000,
        Speakertopbackleft = 0x8000,
        Speakertopbackcenter = 0x10000,
        Speakertopbackright = 0x20000,

        Directout = 0,
        Ksaudiospeakermono = Speakerfrontcenter,
        Ksaudiospeakerstereo = Speakerfrontleft | Speakerfrontright,
        Ksaudiospeakerquad = Speakerfrontleft | Speakerfrontright | Speakerbackleft | Speakerbackright,
        Ksaudiospeakersurround = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerbackcenter,
        Ksaudiospeaker5Point1 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerlowfrequency | Speakerbackleft | Speakerbackright,
        Ksaudiospeaker5Point1Surround = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerlowfrequency | Speakersideleft | Speakersideright,
        Ksaudiospeaker7Point1 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerlowfrequency | Speakerbackleft | Speakerbackright | Speakerfrontleftofcenter | Speakerfrontrightofcenter,
        Ksaudiospeaker7Point1Surround = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerlowfrequency | Speakerbackleft | Speakerbackright | Speakersideleft | Speakersideright,

        Dvdaudiogr10 = Speakerfrontcenter,
        Dvdaudiogr11 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr12 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr13 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr14 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr15 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr16 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr17 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr18 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr19 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr110 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr111 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr112 = Speakerfrontleft | Speakerfrontright,
        Dvdaudiogr113 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        Dvdaudiogr114 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        Dvdaudiogr115 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        Dvdaudiogr116 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        Dvdaudiogr117 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        Dvdaudiogr118 = Speakerfrontleft | Speakerfrontright | Speakerbackleft | Speakerbackright,
        Dvdaudiogr119 = Speakerfrontleft | Speakerfrontright | Speakerbackleft | Speakerbackright,
        Dvdaudiogr120 = Speakerfrontleft | Speakerfrontright | Speakerbackleft | Speakerbackright,

        Dvdaudiogr20 = 0,
        Dvdaudiogr21 = 0,
        Dvdaudiogr22 = Speakerbackcenter,
        Dvdaudiogr23 = Speakerbackleft | Speakerbackright,
        Dvdaudiogr24 = Speakerlowfrequency,
        Dvdaudiogr25 = Speakerlowfrequency | Speakerbackcenter,
        Dvdaudiogr26 = Speakerlowfrequency | Speakerbackleft | Speakerbackright,
        Dvdaudiogr27 = Speakerfrontcenter,
        Dvdaudiogr28 = Speakerfrontcenter | Speakerbackcenter,
        Dvdaudiogr29 = Speakerfrontcenter | Speakerbackleft | Speakerbackright,
        Dvdaudiogr210 = Speakerfrontcenter | Speakerlowfrequency,
        Dvdaudiogr211 = Speakerfrontcenter | Speakerlowfrequency | Speakerbackcenter,
        Dvdaudiogr212 = Speakerfrontcenter | Speakerlowfrequency | Speakerbackleft | Speakerbackright,
        Dvdaudiogr213 = Speakerbackcenter,
        Dvdaudiogr214 = Speakerbackleft | Speakerbackright,
        Dvdaudiogr215 = Speakerlowfrequency,
        Dvdaudiogr216 = Speakerlowfrequency | Speakerbackcenter,
        Dvdaudiogr217 = Speakerlowfrequency | Speakerbackleft | Speakerbackright,
        Dvdaudiogr218 = Speakerlowfrequency,
        Dvdaudiogr219 = Speakerfrontcenter,
        Dvdaudiogr220 = Speakerfrontcenter | Speakerlowfrequency
#pragma warning restore CA1069
    }

    /// <summary>
    /// Gets the number of bits per sample.
    /// </summary>
    public int BitsPerSample { get; }

    /// <summary>
    /// Gets the number of channels.
    /// </summary>
    public int ChannelCount { get; }

    /// <summary>
    /// Gets the sample rate in Hz.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// Gets the block alignment in bytes (calculated from channel count and bits per sample).
    /// </summary>
    public int BlockAlign => ChannelCount * ((BitsPerSample + 7) / 8);

    /// <summary>
    /// Gets the speaker channel mask.
    /// </summary>
    public SpeakerConfig ChannelMask { get; }

    /// <summary>
    /// Gets a value indicating whether the configuration matches Red Book audio CD format (16-bit, 2-channel, 44100 Hz).
    /// </summary>
    public bool IsRedBook => BitsPerSample == 16 && ChannelCount == 2 && SampleRate == 44100;

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
                return SpeakerConfig.Ksaudiospeakermono;
            case 2:
                return SpeakerConfig.Ksaudiospeakerstereo;
            case 3:
                return SpeakerConfig.Ksaudiospeakerstereo | SpeakerConfig.Speakerlowfrequency;
            case 4:
                return SpeakerConfig.Ksaudiospeakerquad;
            case 5:
                //return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1 & ~SpeakerConfig.SPEAKER_LOW_FREQUENCY;
                return SpeakerConfig.Ksaudiospeaker5Point1Surround & ~SpeakerConfig.Speakerlowfrequency;
            case 6:
                //return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1;
                return SpeakerConfig.Ksaudiospeaker5Point1Surround;
            case 7:
                return SpeakerConfig.Ksaudiospeaker5Point1Surround | SpeakerConfig.Speakerbackcenter;
            case 8:
                return SpeakerConfig.Ksaudiospeaker7Point1Surround;
        }

        return (SpeakerConfig)((1 << channelCount) - 1);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPcmConfig"/> class.
    /// </summary>
    /// <param name="bitsPerSample">The number of bits per sample.</param>
    /// <param name="channelCount">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate in Hz.</param>
    /// <param name="channelMask">The speaker configuration mask. If <see cref="SpeakerConfig.Directout"/>, a default mask is assigned based on channel count.</param>
    public AudioPcmConfig(int bitsPerSample, int channelCount, int sampleRate, SpeakerConfig channelMask = SpeakerConfig.Directout)
    {
        BitsPerSample = bitsPerSample;
        ChannelCount = channelCount;
        SampleRate = sampleRate;
        ChannelMask = channelMask == 0 ? GetDefaultChannelMask(channelCount) : channelMask;
    }
}
