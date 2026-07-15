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
    public int Blocksize;
    /// <summary>
    /// Block size codes from the frame header. <c>bs_code0</c> is the primary code;
    /// <c>bs_code1</c> provides additional info for custom block sizes.
    /// </summary>
    public int BsCode0, BsCode1;
    /// <summary>
    /// Channel assignment mode for this frame.
    /// </summary>
    public ChannelMode ChMode;
    /// <summary>
    /// CRC-8 checksum of the frame header.
    /// </summary>
    public byte Crc8;
    /// <summary>
    /// Array of subframe processing information, one per channel.
    /// </summary>
    public FlacSubframeInfo[] Subframes;
    /// <summary>
    /// Frame number (sample number of the first sample in this frame).
    /// </summary>
    public int FrameNumber;
    /// <summary>
    /// Temporary subframe used during encoding.
    /// </summary>
    public FlacSubframe Current;
    /// <summary>
    /// Pointer to a floating-point window buffer for LPC analysis.
    /// </summary>
    public float* WindowBuffer;
    /// <summary>
    /// Number of segments (for streaming FLAC).
    /// </summary>
    public int NSeg = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlacFrame"/> class with the specified number of subframes.
    /// </summary>
    /// <param name="subframesCount">Number of audio channels (subframes) in this frame.</param>
    public FlacFrame(int subframesCount)
    {
        Subframes = new FlacSubframeInfo[subframesCount];
        for (var ch = 0; ch < subframesCount; ch++)
        {
            Subframes[ch] = new FlacSubframeInfo();
        }

        Current = new FlacSubframe();
    }
}
