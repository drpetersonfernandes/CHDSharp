namespace CHDSharp.Models.Flac.FlacDeps;

/// <summary>
/// Represents the configuration of an audio PCM stream, including sample rate, bit depth, channel count, and speaker layout.
/// </summary>
internal class AudioPcmConfig
{
    /// <summary>
    /// Flags representing speaker positions and predefined speaker configurations.
    /// </summary>
    [Flags]
    public enum SpeakerConfig
    {
#pragma warning disable CA1069 // Duplicate enum values are intentional flags aliases
        /// <summary>Front left speaker.</summary>
        Speakerfrontleft = 0x1,
        /// <summary>Front right speaker.</summary>
        Speakerfrontright = 0x2,
        /// <summary>Front center speaker.</summary>
        Speakerfrontcenter = 0x4,
        /// <summary>Low-frequency effects (subwoofer) channel.</summary>
        Speakerlowfrequency = 0x8,
        /// <summary>Back left surround speaker.</summary>
        Speakerbackleft = 0x10,
        /// <summary>Back right surround speaker.</summary>
        Speakerbackright = 0x20,
        /// <summary>Front left of center speaker.</summary>
        Speakerfrontleftofcenter = 0x40,
        /// <summary>Front right of center speaker.</summary>
        Speakerfrontrightofcenter = 0x80,
        /// <summary>Back center surround speaker.</summary>
        Speakerbackcenter = 0x100,
        /// <summary>Side left surround speaker.</summary>
        Speakersideleft = 0x200,
        /// <summary>Side right surround speaker.</summary>
        Speakersideright = 0x400,
        /// <summary>Top center overhead speaker.</summary>
        Speakertopcenter = 0x800,
        /// <summary>Top front left overhead speaker.</summary>
        Speakertopfrontleft = 0x1000,
        /// <summary>Top front center overhead speaker.</summary>
        Speakertopfrontcenter = 0x2000,
        /// <summary>Top front right overhead speaker.</summary>
        Speakertopfrontright = 0x4000,
        /// <summary>Top back left overhead speaker.</summary>
        Speakertopbackleft = 0x8000,
        /// <summary>Top back center overhead speaker.</summary>
        Speakertopbackcenter = 0x10000,
        /// <summary>Top back right overhead speaker.</summary>
        Speakertopbackright = 0x20000,

        /// <summary>Direct output (no predefined speaker configuration).</summary>
        Directout = 0,
        /// <summary>Mono speaker configuration (front center).</summary>
        Ksaudiospeakermono = Speakerfrontcenter,
        /// <summary>Stereo speaker configuration (front left + front right).</summary>
        Ksaudiospeakerstereo = Speakerfrontleft | Speakerfrontright,
        /// <summary>Quadraphonic speaker configuration (front left/right + back left/right).</summary>
        Ksaudiospeakerquad = Speakerfrontleft | Speakerfrontright | Speakerbackleft | Speakerbackright,
        /// <summary>Surround speaker configuration (front left/right/center + back center).</summary>
        Ksaudiospeakersurround = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerbackcenter,
        /// <summary>5.1 surround speaker configuration (front left/right/center + LFE + back left/right).</summary>
        Ksaudiospeaker5Point1 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerlowfrequency | Speakerbackleft | Speakerbackright,
        /// <summary>5.1 surround speaker configuration using side speakers (front left/right/center + LFE + side left/right).</summary>
        Ksaudiospeaker5Point1Surround = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerlowfrequency | Speakersideleft | Speakersideright,
        /// <summary>7.1 surround speaker configuration (front left/right/center + LFE + back left/right + front left/right of center).</summary>
        Ksaudiospeaker7Point1 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerlowfrequency | Speakerbackleft | Speakerbackright | Speakerfrontleftofcenter | Speakerfrontrightofcenter,
        /// <summary>7.1 surround speaker configuration using side speakers (front left/right/center + LFE + back left/right + side left/right).</summary>
        Ksaudiospeaker7Point1Surround = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter | Speakerlowfrequency | Speakerbackleft | Speakerbackright | Speakersideleft | Speakersideright,

        /// <summary>DVD Audio channel group 1, channel 0 (front center).</summary>
        Dvdaudiogr10 = Speakerfrontcenter,
        /// <summary>DVD Audio channel group 1, channel 1 (front left + front right).</summary>
        Dvdaudiogr11 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 2 (front left + front right).</summary>
        Dvdaudiogr12 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 3 (front left + front right).</summary>
        Dvdaudiogr13 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 4 (front left + front right).</summary>
        Dvdaudiogr14 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 5 (front left + front right).</summary>
        Dvdaudiogr15 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 6 (front left + front right).</summary>
        Dvdaudiogr16 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 7 (front left + front right).</summary>
        Dvdaudiogr17 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 8 (front left + front right).</summary>
        Dvdaudiogr18 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 9 (front left + front right).</summary>
        Dvdaudiogr19 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 10 (front left + front right).</summary>
        Dvdaudiogr110 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 11 (front left + front right).</summary>
        Dvdaudiogr111 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 12 (front left + front right).</summary>
        Dvdaudiogr112 = Speakerfrontleft | Speakerfrontright,
        /// <summary>DVD Audio channel group 1, channel 13 (front left/right + front center).</summary>
        Dvdaudiogr113 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        /// <summary>DVD Audio channel group 1, channel 14 (front left/right + front center).</summary>
        Dvdaudiogr114 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        /// <summary>DVD Audio channel group 1, channel 15 (front left/right + front center).</summary>
        Dvdaudiogr115 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        /// <summary>DVD Audio channel group 1, channel 16 (front left/right + front center).</summary>
        Dvdaudiogr116 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        /// <summary>DVD Audio channel group 1, channel 17 (front left/right + front center).</summary>
        Dvdaudiogr117 = Speakerfrontleft | Speakerfrontright | Speakerfrontcenter,
        /// <summary>DVD Audio channel group 1, channel 18 (front left/right + back left/right).</summary>
        Dvdaudiogr118 = Speakerfrontleft | Speakerfrontright | Speakerbackleft | Speakerbackright,
        /// <summary>DVD Audio channel group 1, channel 19 (front left/right + back left/right).</summary>
        Dvdaudiogr119 = Speakerfrontleft | Speakerfrontright | Speakerbackleft | Speakerbackright,
        /// <summary>DVD Audio channel group 1, channel 20 (front left/right + back left/right).</summary>
        Dvdaudiogr120 = Speakerfrontleft | Speakerfrontright | Speakerbackleft | Speakerbackright,

        /// <summary>DVD Audio channel group 2, channel 0 (silent).</summary>
        Dvdaudiogr20 = 0,
        /// <summary>DVD Audio channel group 2, channel 1 (silent).</summary>
        Dvdaudiogr21 = 0,
        /// <summary>DVD Audio channel group 2, channel 2 (back center).</summary>
        Dvdaudiogr22 = Speakerbackcenter,
        /// <summary>DVD Audio channel group 2, channel 3 (back left + back right).</summary>
        Dvdaudiogr23 = Speakerbackleft | Speakerbackright,
        /// <summary>DVD Audio channel group 2, channel 4 (LFE).</summary>
        Dvdaudiogr24 = Speakerlowfrequency,
        /// <summary>DVD Audio channel group 2, channel 5 (LFE + back center).</summary>
        Dvdaudiogr25 = Speakerlowfrequency | Speakerbackcenter,
        /// <summary>DVD Audio channel group 2, channel 6 (LFE + back left/right).</summary>
        Dvdaudiogr26 = Speakerlowfrequency | Speakerbackleft | Speakerbackright,
        /// <summary>DVD Audio channel group 2, channel 7 (front center).</summary>
        Dvdaudiogr27 = Speakerfrontcenter,
        /// <summary>DVD Audio channel group 2, channel 8 (front center + back center).</summary>
        Dvdaudiogr28 = Speakerfrontcenter | Speakerbackcenter,
        /// <summary>DVD Audio channel group 2, channel 9 (front center + back left/right).</summary>
        Dvdaudiogr29 = Speakerfrontcenter | Speakerbackleft | Speakerbackright,
        /// <summary>DVD Audio channel group 2, channel 10 (front center + LFE).</summary>
        Dvdaudiogr210 = Speakerfrontcenter | Speakerlowfrequency,
        /// <summary>DVD Audio channel group 2, channel 11 (front center + LFE + back center).</summary>
        Dvdaudiogr211 = Speakerfrontcenter | Speakerlowfrequency | Speakerbackcenter,
        /// <summary>DVD Audio channel group 2, channel 12 (front center + LFE + back left/right).</summary>
        Dvdaudiogr212 = Speakerfrontcenter | Speakerlowfrequency | Speakerbackleft | Speakerbackright,
        /// <summary>DVD Audio channel group 2, channel 13 (back center).</summary>
        Dvdaudiogr213 = Speakerbackcenter,
        /// <summary>DVD Audio channel group 2, channel 14 (back left + back right).</summary>
        Dvdaudiogr214 = Speakerbackleft | Speakerbackright,
        /// <summary>DVD Audio channel group 2, channel 15 (LFE).</summary>
        Dvdaudiogr215 = Speakerlowfrequency,
        /// <summary>DVD Audio channel group 2, channel 16 (LFE + back center).</summary>
        Dvdaudiogr216 = Speakerlowfrequency | Speakerbackcenter,
        /// <summary>DVD Audio channel group 2, channel 17 (LFE + back left/right).</summary>
        Dvdaudiogr217 = Speakerlowfrequency | Speakerbackleft | Speakerbackright,
        /// <summary>DVD Audio channel group 2, channel 18 (LFE).</summary>
        Dvdaudiogr218 = Speakerlowfrequency,
        /// <summary>DVD Audio channel group 2, channel 19 (front center).</summary>
        Dvdaudiogr219 = Speakerfrontcenter,
        /// <summary>DVD Audio channel group 2, channel 20 (front center + LFE).</summary>
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
                return SpeakerConfig.Ksaudiospeaker5Point1Surround & ~SpeakerConfig.Speakerlowfrequency;
            case 6:
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
