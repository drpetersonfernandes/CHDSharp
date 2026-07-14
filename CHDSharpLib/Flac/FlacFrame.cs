using CHDSharp.Flac.FlacDeps;

namespace CHDSharp.Flac;

unsafe public class FlacFrame
{
    public int blocksize;
    public int bs_code0, bs_code1;
    public ChannelMode ch_mode;
    public byte crc8;
    public FlacSubframeInfo[] subframes;
    public int frame_number;
    public FlacSubframe current;
    public float* window_buffer;
    public int nSeg = 0;

    public FlacFrame(int subframes_count)
    {
        subframes = new FlacSubframeInfo[subframes_count];
        for (var ch = 0; ch < subframes_count; ch++)
        {
            subframes[ch] = new FlacSubframeInfo();
        }

        current = new FlacSubframe();
    }
}