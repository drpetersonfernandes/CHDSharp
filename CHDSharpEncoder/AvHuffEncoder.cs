namespace CHDSharpEncoder;

/// <summary>
/// Port of MAME's <c>avhuff_encoder</c> (src/lib/util/avhuff.cpp): the laserdisc A/V codec
/// ('avhu'). Each frame is assembled into a raw 'chav' block (12-byte header, optional
/// metadata, planar big-endian 16-bit audio, big-endian YUY2 video) and compressed as
/// delta-RLE Huffman video + per-channel mono FLAC audio.
///
/// Compressed frame layout (all values big-endian):
/// +00 metasize(1) +01 channels(1) +02 samples(2) +04 width(2) +06 height(2)
/// +08 audio tree size (0xFFFF = FLAC) +0A.. per-channel stream sizes,
/// then metadata, audio streams, and the Huffman-coded video bitstream.
/// </summary>
internal sealed class AvHuffEncoder
{
    private const int RleAlphabetSize = 256 + 16;
    private const int MaxBits = 16;

    private readonly DeltaRleEncoder _yContext = new();
    private readonly DeltaRleEncoder _cbContext = new();
    private readonly DeltaRleEncoder _crContext = new();

    /// <summary>Raw ('chav') data size for one frame: header + metadata + audio + video.</summary>
    public static uint RawDataSize(uint width, uint height, uint channels, uint numSamples) =>
        12 + channels * numSamples * 2 + width * height * 2;

    /// <summary>
    /// Assembles a raw 'chav' datastream from decoded pieces (MAME's
    /// <c>avhuff_encoder::assemble_data</c>). The video is supplied pre-serialized in the
    /// final big-endian YUY2 byte order (Cb,Y,Cr,Y pixel pairs), which is what MAME's
    /// <c>put_u16be</c> loop produces from its native bitmap.
    /// </summary>
    /// <param name="buffer">Destination buffer; sized exactly to the raw frame.</param>
    /// <param name="video">Video bytes in final YUY2 order (<c>width * height * 2</c> bytes).</param>
    /// <param name="width">Video width in pixels (must be even).</param>
    /// <param name="height">Video height in lines.</param>
    /// <param name="channels">Number of audio channels.</param>
    /// <param name="numSamples">Samples per channel in this frame.</param>
    /// <param name="samples">Planar audio: <paramref name="samples"/>[channel][sample].</param>
    public static void AssembleData(Span<byte> buffer, ReadOnlySpan<byte> video, int width, int height,
        int channels, int numSamples, ReadOnlySpan<short[]> samples)
    {
        if (buffer.Length != 12 + channels * numSamples * 2 + width * height * 2)
            throw new ArgumentException("Buffer size does not match the frame geometry", nameof(buffer));

        buffer[0] = (byte)'c';
        buffer[1] = (byte)'h';
        buffer[2] = (byte)'a';
        buffer[3] = (byte)'v';
        buffer[4] = 0; // metasize (createld never stores inline metadata)
        buffer[5] = (byte)channels;
        PutU16Be(buffer.Slice(6), (ushort)numSamples);
        PutU16Be(buffer.Slice(8), (ushort)width);
        PutU16Be(buffer.Slice(10), (ushort)height);

        var dest = buffer.Slice(12);

        // copy the audio streams, channel-planar, big-endian
        for (int ch = 0; ch < channels; ch++)
        {
            var plane = samples[ch];
            for (int i = 0; i < numSamples; i++)
            {
                dest[0] = (byte)((ushort)plane[i] >> 8);
                dest[1] = (byte)plane[i];
                dest = dest.Slice(2);
            }
        }

        // copy the video data (already in serialized byte order)
        video.CopyTo(dest);
    }

    /// <summary>
    /// Encodes a raw 'chav' block into a compressed stream (MAME's
    /// <c>avhuff_encoder::encode_data</c>). Returns the compressed length.
    /// </summary>
    public int EncodeData(ReadOnlySpan<byte> source, Span<byte> dest)
    {
        if (source.Length < 12 || source[0] != (byte)'c' || source[1] != (byte)'h' || source[2] != (byte)'a' || source[3] != (byte)'v')
            throw new InvalidDataException("AVHuff source does not start with a 'chav' header");

        uint metaSize = source[4];
        uint channels = source[5];
        uint samples = ReadU16Be(source.Slice(6));
        uint width = ReadU16Be(source.Slice(8));
        uint height = ReadU16Be(source.Slice(10));
        var body = source.Slice(12);

        dest[0] = (byte)metaSize;
        dest[1] = (byte)channels;
        PutU16Be(dest.Slice(2), (ushort)samples);
        PutU16Be(dest.Slice(4), (ushort)width);
        PutU16Be(dest.Slice(6), (ushort)height);

        int dstOffs = 10 + 2 * (int)channels;

        // copy the metadata first
        if (metaSize > 0)
        {
            body.Slice(0, (int)metaSize).CopyTo(dest.Slice(dstOffs));
            body = body.Slice((int)metaSize);
            dstOffs += (int)metaSize;
        }

        // encode the audio channels
        if (channels > 0)
        {
            EncodeAudio(body, (int)channels, (int)samples, dest, dstOffs);

            // advance past the audio data (tree size 0xFFFF means FLAC: no tree bytes stored)
            uint treeSize = ReadU16Be(dest.Slice(8));
            if (treeSize != 0xFFFF)
                dstOffs += (int)treeSize;
            for (int ch = 0; ch < channels; ch++)
                dstOffs += ReadU16Be(dest.Slice(10 + 2 * ch));
        }
        else
        {
            dest[8] = 0;
            dest[9] = 0;
        }

        // encode the video data
        if (width > 0 && height > 0)
        {
            body = body.Slice((int)(channels * samples * 2));
            dstOffs += EncodeVideoLossless(body, (int)width, (int)height, dest, dstOffs);
        }

        return dstOffs;
    }

    /// <summary>
    /// Encodes the audio channels as one mono FLAC stream per channel (MAME's
    /// <c>encode_audio</c> with AVHUFF_USE_FLAC): tree-size marker 0xFFFF at dest[8],
    /// then each channel's FLAC frames written into <paramref name="dest"/> starting at
    /// <paramref name="dstOffs"/>, capped at <c>samples * 2</c> bytes like MAME's
    /// <c>flac_encoder::reset(dest, samples * 2)</c> (bytes beyond the cap are dropped but
    /// still counted in the recorded stream size).
    /// </summary>
    private void EncodeAudio(ReadOnlySpan<byte> source, int channels, int samples, Span<byte> dest, int dstOffs)
    {
        // set huffman tree size to 0xffff to indicate FLAC
        dest[8] = 0xFF;
        dest[9] = 0xFF;

        var flacOut = new byte[samples * 2 + 64];
        var pcm = new short[samples];

        for (int ch = 0; ch < channels; ch++)
        {
            // read this channel's planar big-endian samples
            for (int i = 0; i < samples; i++)
            {
                int off = (ch * samples + i) * 2;
                pcm[i] = (short)((source[off] << 8) | source[off + 1]);
            }

            var encoder = new Flac.LibFlacEncoder(samples, channels: 1, sampleRate: 48000);
            int length = encoder.Encode(flacOut, pcm.AsSpan(0, samples));

            // record the size of this channel's stream (full logical length, even when the
            // tail was dropped by the cap — matching MAME's m_compressed_offset accounting)
            int cursize = Math.Min(length, ushort.MaxValue);
            PutU16Be(dest.Slice(10 + 2 * ch), (ushort)cursize);

            // copy into the destination, dropping bytes beyond the samples*2 cap
            int cap = samples * 2;
            int store = Math.Min(length, cap);
            if (dstOffs + store <= dest.Length)
            {
                flacOut.AsSpan(0, store).CopyTo(dest.Slice(dstOffs));
            }
            else if (dstOffs < dest.Length)
            {
                flacOut.AsSpan(0, Math.Min(store, dest.Length - dstOffs)).CopyTo(dest.Slice(dstOffs));
            }

            dstOffs += cursize;
        }
    }

    /// <summary>
    /// Lossless video encoding: delta-RLE histogramming of the Y/Cb/Cr planes, RLE-coded
    /// tree export, then Huffman coding of the interleaved symbols (MAME's
    /// <c>encode_video_lossless</c>). The bitstream is capped at <c>width * height * 2</c>
    /// bytes like MAME's <c>bitstream_out bitbuf(dest, width * height * 2)</c>: writes beyond
    /// the cap are dropped but still counted in the returned length. Returns that length.
    /// </summary>
    private int EncodeVideoLossless(ReadOnlySpan<byte> source, int width, int height, Span<byte> dest, int dstOffs)
    {
        int videoRegionSize = width * height * 2;

        // set up the output; first byte is 0x80 to indicate lossless encoding
        var scratch = new byte[videoRegionSize];
        var bitbuf = new BitStreamOut(scratch, 0, scratch.Length);
        bitbuf.Write(0x80, 8);

        // compute the histograms for the data (Y at even offsets; Cb/Cr interleaved 4-wide)
        _yContext.RleAndHistoBitmap(source, 0, width, 2, height);
        _cbContext.RleAndHistoBitmap(source, 1, width / 2, 4, height);
        _crContext.RleAndHistoBitmap(source, 3, width / 2, 4, height);

        // export the trees to the data stream
        _yContext.ExportTreeRle(bitbuf);
        bitbuf.Flush();
        _cbContext.ExportTreeRle(bitbuf);
        bitbuf.Flush();
        _crContext.ExportTreeRle(bitbuf);
        bitbuf.Flush();

        // encode the data using the trees (Y,Cb,Y,Cr per pixel pair)
        int yPos = 0, cbPos = 0, crPos = 0;
        for (int sy = 0; sy < height; sy++)
        {
            _yContext.FlushRle();
            _cbContext.FlushRle();
            _crContext.FlushRle();
            for (int sx = 0; sx < width / 2; sx++)
            {
                _yContext.EncodeOne(bitbuf, ref yPos);
                _cbContext.EncodeOne(bitbuf, ref cbPos);
                _yContext.EncodeOne(bitbuf, ref yPos);
                _crContext.EncodeOne(bitbuf, ref crPos);
            }
        }

        int compLength = bitbuf.Flush();
        int store = Math.Min(compLength, Math.Min(scratch.Length, Math.Max(dest.Length - dstOffs, 0)));
        scratch.AsSpan(0, store).CopyTo(dest.Slice(dstOffs));
        return compLength;
    }

    private static ushort ReadU16Be(ReadOnlySpan<byte> data) => (ushort)((data[0] << 8) | data[1]);

    private static void PutU16Be(Span<byte> dest, ushort value)
    {
        dest[0] = (byte)(value >> 8);
        dest[1] = (byte)value;
    }

    /// <summary>Number of RLE repetitions encoded by a given symbol code (avhuff.cpp:82).</summary>
    internal static int CodeToRleCount(int code)
    {
        if (code == 0x00)
            return 1;
        if (code <= 0x107)
            return 8 + (code - 0x100);
        return 16 << (code - 0x108);
    }

    /// <summary>Largest RLE count ≤ <paramref name="rleCount"/>, as a symbol code (avhuff.cpp:98).</summary>
    internal static int RleCountToCode(int rleCount)
    {
        if (rleCount >= 2048)
            return 0x10f;
        if (rleCount >= 1024)
            return 0x10e;
        if (rleCount >= 512)
            return 0x10d;
        if (rleCount >= 256)
            return 0x10c;
        if (rleCount >= 128)
            return 0x10b;
        if (rleCount >= 64)
            return 0x10a;
        if (rleCount >= 32)
            return 0x109;
        if (rleCount >= 16)
            return 0x108;
        if (rleCount >= 8)
            return 0x100 + (rleCount - 8);
        return 0x00;
    }

    /// <summary>
    /// Delta-RLE video-plane encoder (MAME's <c>deltarle_encoder</c>): histograms delta/RLE
    /// symbols over one YUY2 plane, builds a canonical Huffman tree, and later emits the
    /// symbols while expanding runs.
    /// </summary>
    internal sealed class DeltaRleEncoder
    {
        private int _rleCount;
        private readonly HuffmanEncoder _encoder = new(RleAlphabetSize, MaxBits);
        private ushort[] _rleBuffer = new ushort[1024];
        private int _rleLength;

        /// <summary>
        /// RLE-compresses and histograms one plane (MAME's <c>rle_and_histo_bitmap</c>).
        /// The delta chain persists across rows; zero-runs that reach a row end with at
        /// least 8 repeats are maximized to a single end-of-row code.
        /// </summary>
        /// <param name="source">The raw 'chav' video bytes.</param>
        /// <param name="start">Byte offset of this plane within <paramref name="source"/>.</param>
        /// <param name="itemsPerRow">Items per row (pixels).</param>
        /// <param name="itemAdvance">Bytes between consecutive items.</param>
        /// <param name="rowCount">Number of rows.</param>
        public void RleAndHistoBitmap(ReadOnlySpan<byte> source, int start, int itemsPerRow, int itemAdvance, int rowCount)
        {
            if (_rleBuffer.Length < itemsPerRow * rowCount)
                _rleBuffer = new ushort[itemsPerRow * rowCount];

            _rleLength = itemsPerRow * rowCount;
            int destPos = 0;

            _encoder.ResetHistogram();
            int prevData = 0;
            int rowStart = start;
            for (int row = 0; row < rowCount; row++)
            {
                int srcPos = rowStart;
                int end = rowStart + itemsPerRow * itemAdvance;
                while (srcPos < end)
                {
                    // fetch current data (uint8 wrap-around delta)
                    int curDelta = (source[srcPos] - prevData) & 0xFF;
                    prevData = source[srcPos];

                    if (curDelta == 0)
                    {
                        // 0 deltas scan forward for a count
                        int zeroCount = 1;
                        int scan = srcPos + itemAdvance;
                        while (scan < end && source[scan] == prevData)
                        {
                            zeroCount++;
                            scan += itemAdvance;
                        }

                        // if we hit the end of a row, maximize the count
                        if (scan >= end && zeroCount >= 8)
                            zeroCount = 100000;

                        // encode the maximal count we can
                        int rleCode = RleCountToCode(zeroCount);
                        _rleBuffer[destPos++] = (ushort)rleCode;
                        _encoder.CountSymbol((uint)rleCode);

                        // advance past the run (plus this item's own stride)
                        srcPos += itemAdvance + (CodeToRleCount(rleCode) - 1) * itemAdvance;
                    }
                    else
                    {
                        // otherwise, encode the actual data
                        _rleBuffer[destPos++] = (ushort)curDelta;
                        _encoder.CountSymbol((uint)curDelta);
                        srcPos += itemAdvance;
                    }
                }

                // advance to the next row (a maximizing run may have overshot: clamp back)
                rowStart = end;
            }

            _encoder.BuildTree();
        }

        /// <summary>Clears a pending run so the next <see cref="EncodeOne"/> reads a fresh symbol.</summary>
        public void FlushRle() => _rleCount = 0;

        /// <summary>Emits the next symbol, silently consuming an active RLE run.</summary>
        public void EncodeOne(BitStreamOut bitbuf, ref int rlePos)
        {
            if (_rleCount != 0)
            {
                _rleCount--;
                return;
            }

            ushort data = _rleBuffer[rlePos++];
            _encoder.Encode(bitbuf, data);
            if (data >= 0x100)
                _rleCount = CodeToRleCount(data) - 1;
        }

        /// <summary>Writes the Huffman tree in RLE form (MAME's <c>export_tree_rle</c>).</summary>
        public void ExportTreeRle(BitStreamOut bitbuf) => _encoder.ExportTreeRle(bitbuf);
    }
}
