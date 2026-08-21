namespace CHDSharpEncoder;

/// <summary>
/// Port of MAME's <c>vbiparse</c> (src/lib/util/vbiparse.cpp): parses Philips codes and the
/// "white flag" from laserdisc VBI lines, and packs the per-frame results into the 16-byte
/// form stored in 'AVLD' metadata. Input is a YUY2 frame in serialized byte order; each
/// 16-bit pixel is read little-endian and shifted right by <c>sourceShift</c> (= 8 selects
/// the luma byte), exactly like MAME reading its native bitmap.
/// </summary>
public static class VbiParse
{
    /// <summary>Size of one packed VBI record (VBI_PACKED_BYTES).</summary>
    public const int PackedBytes = 16;

    private const int MaxSourceWidth = 1024;
    private const int MaxClockDiff = 3;

    /// <summary>Parsed VBI metadata for one frame.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public struct VbiMetadata
    {
        /// <summary>White flag: on or off.</summary>
        public uint White;

        /// <summary>Line 16 code.</summary>
        public uint Line16;

        /// <summary>Line 17 code.</summary>
        public uint Line17;

        /// <summary>Line 18 code.</summary>
        public uint Line18;

        /// <summary>Most plausible value from lines 17/18.</summary>
        public uint Line1718;
    }

    /// <summary>
    /// Parses everything from a video frame (MAME's <c>vbi_parse_all</c>): the white flag
    /// from line 11 and Manchester codes from lines 16/17/18, then reconciles lines 17/18.
    /// </summary>
    /// <param name="source">Frame bytes (YUY2 order).</param>
    /// <param name="sourceRowPixels">Row stride in pixels.</param>
    /// <param name="sourceWidth">Visible width in pixels.</param>
    /// <param name="sourceShift">Right-shift selecting the sample byte within each 16-bit pixel.</param>
    public static VbiMetadata ParseAll(byte[] source, int sourceRowPixels, int sourceWidth, int sourceShift)
    {
        var vbi = new VbiMetadata();
        var bits0 = new uint[24];
        var bits1 = new uint[24];

        // get the white flag
        vbi.White = ParseWhiteFlag(source, 11 * sourceRowPixels * 2, sourceWidth, sourceShift) ? 1u : 0u;

        // parse line 16
        if (ParseManchesterCode(source, 16 * sourceRowPixels * 2, sourceWidth, sourceShift, 24, bits0) == 24)
            for (int bitNum = 0; bitNum < 24; bitNum++)
                vbi.Line16 = (vbi.Line16 << 1) | (bits0[bitNum] & 1);

        // parse line 17
        if (ParseManchesterCode(source, 17 * sourceRowPixels * 2, sourceWidth, sourceShift, 24, bits0) == 24)
            for (int bitNum = 0; bitNum < 24; bitNum++)
                vbi.Line17 = (vbi.Line17 << 1) | (bits0[bitNum] & 1);

        // parse line 18
        if (ParseManchesterCode(source, 18 * sourceRowPixels * 2, sourceWidth, sourceShift, 24, bits1) == 24)
            for (int bitNum = 0; bitNum < 24; bitNum++)
                vbi.Line18 = (vbi.Line18 << 1) | (bits1[bitNum] & 1);

        // pick the best out of lines 17/18
        if (vbi.Line17 == 0)
            vbi.Line1718 = vbi.Line18;
        else if (vbi.Line18 == 0)
            vbi.Line1718 = vbi.Line17;
        else if (vbi.Line17 == vbi.Line18)
            vbi.Line1718 = vbi.Line17;
        else
        {
            // if both are frame numbers, and one is not valid BCD, pick the other
            const uint cavMask = 0xf00000, cavCode = 0xf00000;
            if ((vbi.Line17 & cavMask) == cavCode && (vbi.Line18 & cavMask) == cavCode)
            {
                if ((vbi.Line17 & 0xf000) > 0x9000 || (vbi.Line17 & 0xf00) > 0x900 || (vbi.Line17 & 0xf0) > 0x90 || (vbi.Line17 & 0xf) > 0x9)
                    vbi.Line1718 = vbi.Line18;
                else if ((vbi.Line18 & 0xf000) > 0x9000 || (vbi.Line18 & 0xf00) > 0x900 || (vbi.Line18 & 0xf0) > 0x90 || (vbi.Line18 & 0xf) > 0x9)
                    vbi.Line1718 = vbi.Line17;
            }

            // if still nothing, scan through the bits and pick the ones with the most confidence
            if (vbi.Line1718 == 0)
                for (int bitNum = 0; bitNum < 24; bitNum++)
                    vbi.Line1718 = (vbi.Line1718 << 1) | (bits0[bitNum] > bits1[bitNum] ? (bits0[bitNum] & 1) : (bits1[bitNum] & 1));
        }

        return vbi;
    }

    /// <summary>
    /// Packs the VBI data down into the 16-byte storage form (MAME's
    /// <c>vbi_metadata_pack</c>): u24be frame number, white flag, then four u24be codes.
    /// </summary>
    public static void MetadataPack(Span<byte> dest, uint frameNum, in VbiMetadata vbi)
    {
        PutU24Be(dest.Slice(0), frameNum);
        dest[3] = (byte)vbi.White;
        PutU24Be(dest.Slice(4), vbi.Line16);
        PutU24Be(dest.Slice(7), vbi.Line17);
        PutU24Be(dest.Slice(10), vbi.Line18);
        PutU24Be(dest.Slice(13), vbi.Line1718);
    }

    /// <summary>
    /// Parses a Manchester code from a line of video data (MAME's
    /// <c>vbi_parse_manchester_code</c>). Returns the number of bits extracted (0 on failure);
    /// each result entry holds (confidence &lt;&lt; 1) | bit.
    /// </summary>
    public static int ParseManchesterCode(byte[] source, int sourceOffsetBytes, int sourceWidth, int sourceShift,
        int expectedBits, uint[] result)
    {
        if (sourceWidth > MaxSourceWidth)
            return 0;

        var srcAbs = new byte[MaxSourceWidth];

        // find highs and lows in the line
        int min = 0xff, max = 0x00;
        for (int x = 0; x < sourceWidth; x++)
        {
            int rawSrc = Sample(source, sourceOffsetBytes, x, sourceShift);
            min = Math.Min(min, rawSrc);
            max = Math.Max(max, rawSrc);
        }

        // bail if the line is all black or all white
        if (max < 0x80 || min > 0x80)
            return 0;

        // determine the midpoint and then set the thresholds to be halfway
        int mid = (min + max) / 2;
        min = mid - (mid - min) / 2;
        max = mid + (max - mid) / 2;

        // convert the source into absolute high/low
        int srcAbsVal = Sample(source, sourceOffsetBytes, 0, sourceShift) > mid ? 1 : 0;
        for (int x = 0; x < sourceWidth; x++)
        {
            int rawSrc = Sample(source, sourceOffsetBytes, x, sourceShift);
            if (rawSrc >= max)
                srcAbsVal = 1;
            else if (rawSrc <= min)
                srcAbsVal = 0;
            srcAbs[x] = (byte)srcAbsVal;
        }

        // find the first transition; this is assumed to be the middle of the first bit
        int firstEdge = -1;
        for (int x = 0; x < sourceWidth - 1; x++)
        {
            if (srcAbs[x] != srcAbs[x + 1])
            {
                firstEdge = x;
                break;
            }
        }

        if (firstEdge < 0)
            return 0;

        // now scan to find a clock that has a nearby transition on each beat
        double bestClock = 0;
        int bestErr = 1000;
        for (double clock = (double)sourceWidth / (double)expectedBits; clock >= 2.0; clock -= 1.0 / (double)expectedBits)
        {
            int error = 0;

            // scan for all the expected bits
            int x2;
            for (x2 = 1; x2 < expectedBits; x2++)
            {
                int curBit = firstEdge + (int)((double)x2 * clock);
                int offBy;

                // look for a match that is off by an amount up to the maximum
                for (offBy = 0; offBy <= MaxClockDiff; offBy++)
                    if (srcAbs[curBit + offBy + 0] != srcAbs[curBit + offBy + 1] || srcAbs[curBit - offBy + 0] != srcAbs[curBit - offBy + 1])
                        break;

                // if we never found the edge, fail immediately
                if (offBy > MaxClockDiff)
                    break;

                // only continue if we're still in the running
                error += offBy;
                if (error >= bestErr)
                    break;
            }

            // if we got to the end, this is the best candidate so far
            if (x2 == expectedBits)
            {
                bestErr = error;
                bestClock = clock;
            }
        }

        // if nobody matched, fail
        if (bestClock == 0)
            return 0;

        // now extract the bits
        for (int x = 0; x < expectedBits; x++)
        {
            int leftStart = firstEdge + (int)Math.Ceiling(((double)x - 0.5) * bestClock);
            int leftEnd = firstEdge + (int)Math.Floor((double)x * bestClock);
            int rightStart = firstEdge + (int)Math.Ceiling((double)x * bestClock);
            int rightEnd = firstEdge + (int)Math.Floor(((double)x + 0.5) * bestClock);

            // compute left and right average values
            int leftAvg = 0;
            for (int tx = leftStart; tx <= leftEnd; tx++)
                leftAvg += Sample(source, sourceOffsetBytes, tx, sourceShift) - mid;
            int leftAbs = leftAvg >= 0 ? 1 : 0;
            leftAvg = Math.Abs(leftAvg);

            int rightAvg = 0;
            for (int tx = rightStart; tx <= rightEnd; tx++)
                rightAvg += Sample(source, sourceOffsetBytes, tx, sourceShift) - mid;
            int rightAbs = rightAvg >= 0 ? 1 : 0;
            rightAvg = Math.Abs(rightAvg);

            // all bits should be marked by transitions; fail if we don't get one
            if (leftAbs == rightAbs)
                return 0;

            // store the bit and its confidence level
            int confidence = leftAvg + rightAvg;
            result[x] = (uint)((leftAbs < rightAbs ? 1 : 0) | (confidence << 1));
        }

        return expectedBits;
    }

    /// <summary>
    /// Computes the "white flag" from a line of video data (MAME's
    /// <c>vbi_parse_white_flag</c>): true when the histogram peak sits above 90% of the
    /// noise-trimmed dynamic range.
    /// </summary>
    public static bool ParseWhiteFlag(byte[] source, int sourceOffsetBytes, int sourceWidth, int sourceShift)
    {
        var histo = new int[256];

        // compute a histogram of values
        for (int x = 0; x < sourceWidth; x++)
            histo[Sample(source, sourceOffsetBytes, x, sourceShift)]++;

        // remove the lowest 1% of the values to account for noise and determine the minimum
        int subtract = sourceWidth / 100;
        int minVal;
        for (minVal = 0; minVal < 255; minVal++)
            if ((subtract -= histo[minVal]) < 0)
                break;

        // remove the highest 1% of the values to account for noise and determine the maximum
        subtract = sourceWidth / 100;
        int maxVal;
        for (maxVal = 255; maxVal > 0; maxVal--)
            if ((subtract -= histo[maxVal]) < 0)
                break;

        // ignore if we have no dynamic range
        if (maxVal - minVal < 10)
            return false;

        // determine where the peak is
        int peakVal = 0;
        for (int x = 1; x < 256; x++)
            if (histo[x] > histo[peakVal])
                peakVal = x;

        // return true if it is above the 90% mark
        return peakVal > minVal + 9 * (maxVal - minVal) / 10;
    }

    private static int Sample(byte[] source, int baseOffset, int pixelIndex, int shift)
    {
        int off = baseOffset + pixelIndex * 2;
        int value = source[off] | (source[off + 1] << 8);
        return value >> shift;
    }

    private static void PutU24Be(Span<byte> dest, uint value)
    {
        dest[0] = (byte)(value >> 16);
        dest[1] = (byte)(value >> 8);
        dest[2] = (byte)value;
    }
}
