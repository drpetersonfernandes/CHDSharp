namespace CHDSharp.Models.Flac.FlacDeps;

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
        SPEAKERFRONTLEFT = 0x1,
        SPEAKERFRONTRIGHT = 0x2,
        SPEAKERFRONTCENTER = 0x4,
        SPEAKERLOWFREQUENCY = 0x8,
        SPEAKERBACKLEFT = 0x10,
        SPEAKERBACKRIGHT = 0x20,
        SPEAKERFRONTLEFTOFCENTER = 0x40,
        SPEAKERFRONTRIGHTOFCENTER = 0x80,
        SPEAKERBACKCENTER = 0x100,
        SPEAKERSIDELEFT = 0x200,
        SPEAKERSIDERIGHT = 0x400,
        SPEAKERTOPCENTER = 0x800,
        SPEAKERTOPFRONTLEFT = 0x1000,
        SPEAKERTOPFRONTCENTER = 0x2000,
        SPEAKERTOPFRONTRIGHT = 0x4000,
        SPEAKERTOPBACKLEFT = 0x8000,
        SPEAKERTOPBACKCENTER = 0x10000,
        SPEAKERTOPBACKRIGHT = 0x20000,

        DIRECTOUT = 0,
        KSAUDIOSPEAKERMONO = SPEAKERFRONTCENTER,
        KSAUDIOSPEAKERSTEREO = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        KSAUDIOSPEAKERQUAD = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        KSAUDIOSPEAKERSURROUND = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER | SPEAKERBACKCENTER,
        KSAUDIOSPEAKER5POINT1 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER | SPEAKERLOWFREQUENCY | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        KSAUDIOSPEAKER5POINT1SURROUND = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER | SPEAKERLOWFREQUENCY | SPEAKERSIDELEFT | SPEAKERSIDERIGHT,
        KSAUDIOSPEAKER7POINT1 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER | SPEAKERLOWFREQUENCY | SPEAKERBACKLEFT | SPEAKERBACKRIGHT | SPEAKERFRONTLEFTOFCENTER | SPEAKERFRONTRIGHTOFCENTER,
        KSAUDIOSPEAKER7POINT1SURROUND = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER | SPEAKERLOWFREQUENCY | SPEAKERBACKLEFT | SPEAKERBACKRIGHT | SPEAKERSIDELEFT | SPEAKERSIDERIGHT,

        DVDAUDIOGR10 = SPEAKERFRONTCENTER,
        DVDAUDIOGR11 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR12 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR13 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR14 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR15 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR16 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR17 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR18 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR19 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR110 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR111 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR112 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT,
        DVDAUDIOGR113 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER,
        DVDAUDIOGR114 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER,
        DVDAUDIOGR115 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER,
        DVDAUDIOGR116 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER,
        DVDAUDIOGR117 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERFRONTCENTER,
        DVDAUDIOGR118 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        DVDAUDIOGR119 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        DVDAUDIOGR120 = SPEAKERFRONTLEFT | SPEAKERFRONTRIGHT | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,

        DVDAUDIOGR20 = 0,
        DVDAUDIOGR21 = 0,
        DVDAUDIOGR22 = SPEAKERBACKCENTER,
        DVDAUDIOGR23 = SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        DVDAUDIOGR24 = SPEAKERLOWFREQUENCY,
        DVDAUDIOGR25 = SPEAKERLOWFREQUENCY | SPEAKERBACKCENTER,
        DVDAUDIOGR26 = SPEAKERLOWFREQUENCY | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        DVDAUDIOGR27 = SPEAKERFRONTCENTER,
        DVDAUDIOGR28 = SPEAKERFRONTCENTER | SPEAKERBACKCENTER,
        DVDAUDIOGR29 = SPEAKERFRONTCENTER | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        DVDAUDIOGR210 = SPEAKERFRONTCENTER | SPEAKERLOWFREQUENCY,
        DVDAUDIOGR211 = SPEAKERFRONTCENTER | SPEAKERLOWFREQUENCY | SPEAKERBACKCENTER,
        DVDAUDIOGR212 = SPEAKERFRONTCENTER | SPEAKERLOWFREQUENCY | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        DVDAUDIOGR213 = SPEAKERBACKCENTER,
        DVDAUDIOGR214 = SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        DVDAUDIOGR215 = SPEAKERLOWFREQUENCY,
        DVDAUDIOGR216 = SPEAKERLOWFREQUENCY | SPEAKERBACKCENTER,
        DVDAUDIOGR217 = SPEAKERLOWFREQUENCY | SPEAKERBACKLEFT | SPEAKERBACKRIGHT,
        DVDAUDIOGR218 = SPEAKERLOWFREQUENCY,
        DVDAUDIOGR219 = SPEAKERFRONTCENTER,
        DVDAUDIOGR220 = SPEAKERFRONTCENTER | SPEAKERLOWFREQUENCY
    }

    private int _bitsPerSample;
    private int _channelCount;
    private int _sampleRate;
    private SpeakerConfig _channelMask;

    /// <summary>
    /// Gets the number of bits per sample.
    /// </summary>
    public int BitsPerSample => _bitsPerSample;

    /// <summary>
    /// Gets the number of channels.
    /// </summary>
    public int ChannelCount => _channelCount;

    /// <summary>
    /// Gets the sample rate in Hz.
    /// </summary>
    public int SampleRate => _sampleRate;

    /// <summary>
    /// Gets the block alignment in bytes (calculated from channel count and bits per sample).
    /// </summary>
    public int BlockAlign => _channelCount * ((_bitsPerSample + 7) / 8);

    /// <summary>
    /// Gets the speaker channel mask.
    /// </summary>
    public SpeakerConfig ChannelMask => _channelMask;

    /// <summary>
    /// Gets a value indicating whether the configuration matches Red Book audio CD format (16-bit, 2-channel, 44100 Hz).
    /// </summary>
    public bool IsRedBook => _bitsPerSample == 16 && _channelCount == 2 && _sampleRate == 44100;

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
                return SpeakerConfig.KSAUDIOSPEAKERMONO;
            case 2:
                return SpeakerConfig.KSAUDIOSPEAKERSTEREO;
            case 3:
                return SpeakerConfig.KSAUDIOSPEAKERSTEREO | SpeakerConfig.SPEAKERLOWFREQUENCY;
            case 4:
                return SpeakerConfig.KSAUDIOSPEAKERQUAD;
            case 5:
                //return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1 & ~SpeakerConfig.SPEAKER_LOW_FREQUENCY;
                return SpeakerConfig.KSAUDIOSPEAKER5POINT1SURROUND & ~SpeakerConfig.SPEAKERLOWFREQUENCY;
            case 6:
                //return SpeakerConfig.KSAUDIO_SPEAKER_5POINT1;
                return SpeakerConfig.KSAUDIOSPEAKER5POINT1SURROUND;
            case 7:
                return SpeakerConfig.KSAUDIOSPEAKER5POINT1SURROUND | SpeakerConfig.SPEAKERBACKCENTER;
            case 8:
                return SpeakerConfig.KSAUDIOSPEAKER7POINT1SURROUND;
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
