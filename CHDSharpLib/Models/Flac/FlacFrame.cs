namespace CHDSharp.Models.Flac;

/// <summary>
/// Represents a FLAC audio frame with its header, subframe data, and decoding state.
/// This class uses unsafe pointers for window buffer access.
/// </summary>
public unsafe class FlacFrame
{
    /// <summary>
    /// The block size (number of samples) for this frame.
    /// </summary>
    public int blocksize;
    /// <summary>
    /// Block size codes from the frame header. <c>bs_code0</c> is the primary code;
    /// <c>bs_code1</c> provides additional info for custom block sizes.
    /// </summary>
    public int bs_code0, bs_code1;
    /// <summary>
    /// Channel assignment mode for this frame.
    /// </summary>
    public ChannelMode ch_mode;
    /// <summary>
    /// CRC-8 checksum of the frame header.
    /// </summary>
    public byte crc8;
    /// <summary>
    /// Array of subframe processing information, one per channel.
    /// </summary>
    public FlacSubframeInfo[] subframes;
    /// <summary>
    /// Frame number (sample number of the first sample in this frame).
    /// </summary>
    public int frame_number;
    /// <summary>
    /// Temporary subframe used during encoding.
    /// </summary>
    public FlacSubframe current;
    /// <summary>
    /// Pointer to a floating-point window buffer for LPC analysis.
    /// </summary>
    public float* window_buffer;
    /// <summary>
    /// Number of segments (for streaming FLAC).
    /// </summary>
    public int nSeg = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlacFrame"/> class with the specified number of subframes.
    /// </summary>
    /// <param name="subframesCount">Number of audio channels (subframes) in this frame.</param>
    public FlacFrame(int subframesCount)
    {
        subframes = new FlacSubframeInfo[subframesCount];
        for (var ch = 0; ch < subframesCount; ch++)
        {
            subframes[ch] = new FlacSubframeInfo();
        }

        current = new FlacSubframe();
    }
}
