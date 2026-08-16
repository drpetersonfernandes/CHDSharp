namespace CHDSharpEncoder.Flac;

/// <summary>
/// Minimal FLAC frame encoder producing standard FLAC frames without a stream header
/// (no fLaC marker, no STREAMINFO) — exactly what MAME's cdfl codec stores. Fixed
/// blocking, per-channel CONSTANT / FIXED (orders 0-4 with Rice-coded residuals) or
/// VERBATIM subframes, with wasted-bits detection. Frames are CRC-8/CRC-16 protected
/// and decodable by any FLAC decoder (validated against CHDSharpLib and chdman).
/// </summary>
internal static class FlacFrameEncoder
{
    private static readonly int[] BlocksizeCodes = { 0, 192, 576, 1152, 2304, 4608, 0, 0, 256, 512, 1024, 2048, 4096, 8192, 16384 };

    private const int MaxFixedOrder = 4;

/// <summary>
/// Encodes interleaved little-endian signed samples as a sequence of FLAC frames.
/// </summary>
/// <param name="output">Destination buffer; must be large enough for the worst case (verbatim).</param>
/// <param name="interleavedLeSamples">Interleaved little-endian sample bytes.</param>
/// <param name="sampleRate">Sample rate in Hz (only 44100 is supported).</param>
/// <param name="channels">Channel count (only 2 is supported).</param>
/// <param name="bitsPerSample">Bits per sample (only 16 is supported).</param>
/// <param name="blockSize">Samples per frame. Defaults to 2352 (MAME's cdfl blocksize =
/// 4 CD sectors), which MAME's flac decoder requires to match its custom STREAMINFO.</param>
/// <returns>The number of bytes written to <paramref name="output"/>.</returns>
    public static int Encode(byte[] output, ReadOnlySpan<byte> interleavedLeSamples,
        int sampleRate = 44100, int channels = 2, int bitsPerSample = 16, int blockSize = 2352)
    {
        if (sampleRate != 44100)
            throw new ArgumentException("Only 44100 Hz is supported");
        if (channels != 2)
            throw new ArgumentException("Only 2 channels are supported");
        if (bitsPerSample != 16)
            throw new ArgumentException("Only 16 bits per sample are supported");
        if (blockSize < 1 || blockSize > 65535)
            throw new ArgumentException($"Invalid block size {blockSize}");

        int bytesPerSample = bitsPerSample / 8;
        int totalSamples = interleavedLeSamples.Length / (bytesPerSample * channels);
        int frameCount = (totalSamples + blockSize - 1) / blockSize;

        // worst case: verbatim subframes, all frames
        var writer = new FlacBitWriter(blockSize * channels * bitsPerSample / 8 + 64);
        var frameBuffer = new byte[blockSize * channels * bitsPerSample / 8 + 64];
        var channelSamples = new int[2][];
        for (int c = 0; c < channels; c++)
            channelSamples[c] = new int[blockSize];

        int outputPos = 0;
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            int blockStart = frameIndex * blockSize;
            int samplesInBlock = Math.Min(blockSize, totalSamples - blockStart);
            int bytesInBlock = samplesInBlock * bytesPerSample * channels;

            for (int c = 0; c < channels; c++)
            {
                var dest = channelSamples[c];
                for (int i = 0; i < samplesInBlock; i++)
                {
                    int src = (blockStart + i) * bytesPerSample * channels + c * bytesPerSample;
                    dest[i] = (short)(interleavedLeSamples[src] | (interleavedLeSamples[src + 1] << 8));
                }
            }

            int frameLen = EncodeFrame(writer, frameBuffer, channelSamples, samplesInBlock, frameIndex, blockSize);
            Array.Copy(frameBuffer, 0, output, outputPos, frameLen);
            outputPos += frameLen;
        }

        return outputPos;
    }

    private static int EncodeFrame(FlacBitWriter writer, byte[] frameBuffer,
        int[][] channelSamples, int samplesInBlock, int frameNumber, int blockSize)
    {
        writer.Reset();
        WriteFrameHeader(writer, frameNumber, blockSize);

        var header = writer.ToArray();
        byte crc8 = FlacCrc.ComputeCrc8(header);
        writer.WriteBits(crc8, 8);

        for (int c = 0; c < channelSamples.Length; c++)
            WriteSubframe(writer, channelSamples[c], samplesInBlock, 16);

        int frameLen = writer.CopyTo(frameBuffer);
        ushort crc16 = FlacCrc.ComputeCrc16(frameBuffer.AsSpan(0, frameLen));
        frameBuffer[frameLen] = (byte)(crc16 >> 8);
        frameBuffer[frameLen + 1] = (byte)crc16;
        return frameLen + 2;
    }

    private static void WriteFrameHeader(FlacBitWriter writer, int frameNumber, int blockSize)
    {
        // sync (14 bits) + reserved 0
        writer.WriteBits(0b11111111111110, 14);
        writer.WriteBit(0);

        // fixed blocking strategy
        writer.WriteBit(0);

        // blocksize code: table match or 16-bit custom
        int bsCode = Array.IndexOf(BlocksizeCodes, blockSize);
        if (bsCode >= 0)
        {
            writer.WriteBits((uint)bsCode, 4);
        }
        else
        {
            writer.WriteBits(7, 4); // custom, 16-bit value (written after the frame number)
        }

        // sample rate code 9 = 44100 Hz (codes: 4=8k, 8=32k, 9=44.1k, 10=48k...)
        writer.WriteBits(9, 4);

        // channel assignment 1 = left+right independent
        writer.WriteBits(1, 4);

        // sample size code 4 = 16 bits
        writer.WriteBits(4, 3);

        // reserved
        writer.WriteBit(0);

        // UTF-8 coded frame number
        if (frameNumber < 128)
        {
            writer.WriteBits((uint)frameNumber, 8);
        }
        else
        {
            writer.WriteBits(0xC0u | (uint)(frameNumber >> 6), 8);
            writer.WriteBits(0x80u | (uint)(frameNumber & 0x3F), 8);
        }

        // custom block size value comes after the frame number, before the CRC-8
        if (bsCode < 0)
        {
            writer.WriteBits((uint)(blockSize - 1), 16);
        }
    }

    private static void WriteSubframe(FlacBitWriter writer, int[] samples, int count, int bitsPerSample)
    {
        // detect wasted bits: common trailing zero bits across all samples
        int wasted = 0;
        while (wasted < 15 && wasted < bitsPerSample - 1 && AllEven(samples, count))
        {
            for (int i = 0; i < count; i++)
                samples[i] >>= 1;
            wasted++;
        }
        int effectiveBps = bitsPerSample - wasted;
        uint mask = (1u << effectiveBps) - 1;

        // subframe header
        writer.WriteBit(0); // zero bit
        if (IsConstant(samples, count))
        {
            writer.WriteBits(0, 6); // CONSTANT
            WriteWastedBits(writer, wasted);
            writer.WriteBits((uint)samples[0] & mask, effectiveBps);
            return;
        }

        // choose the best fixed order or verbatim by estimated encoded size
        int bestOrder = -1;
        long bestBits = long.MaxValue;
        int bestK = 0;
        var residual = new int[count];

        for (int order = 0; order <= MaxFixedOrder && order < count; order++)
        {
            ComputeResidual(samples, count, order, residual);
            int k = ChooseRiceParameter(residual, count, order);
            long bits = (long)order * effectiveBps + 2 + 4 + 4; // warmup + method + porder + rice param
            for (int i = order; i < count; i++)
            {
                uint folded = ((uint)residual[i] << 1) ^ (uint)(residual[i] >> 31);
                bits += (folded >> k) + 1 + k;
            }
            if (bits < bestBits)
            {
                bestBits = bits;
                bestOrder = order;
                bestK = k;
            }
        }

        long verbatimBits = (long)count * effectiveBps;
        if (bestOrder < 0 || bestBits >= verbatimBits)
        {
            // VERBATIM
            writer.WriteBits(1, 6);
            WriteWastedBits(writer, wasted);
            for (int i = 0; i < count; i++)
                writer.WriteBits((uint)samples[i] & mask, effectiveBps);
            return;
        }

        // FIXED order
        writer.WriteBits((uint)(8 + bestOrder), 6);
        WriteWastedBits(writer, wasted);

        ComputeResidual(samples, count, bestOrder, residual);

        // warmup samples
        for (int i = 0; i < bestOrder; i++)
            writer.WriteBits((uint)samples[i] & mask, effectiveBps);

        // residual: rice partition coding (method 0), single partition (order 0);
        // the rice parameter never reaches the escape code (15) because
        // ChooseRiceParameter caps k at 14
        writer.WriteBits(0, 2); // coding method
        writer.WriteBits(0, 4); // partition order
        writer.WriteBits((uint)bestK, 4);
        for (int i = bestOrder; i < count; i++)
            writer.WriteRiceSigned(bestK, residual[i]);
    }

    private static void WriteWastedBits(FlacBitWriter writer, int wasted)
    {
        if (wasted == 0)
        {
            writer.WriteBit(0);
        }
        else
        {
            writer.WriteBit(1);
            writer.WriteUnary(wasted - 1);
        }
    }

    private static bool AllEven(int[] samples, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if ((samples[i] & 1) != 0)
                return false;
        }
        return true;
    }

    private static bool IsConstant(int[] samples, int count)
    {
        for (int i = 1; i < count; i++)
        {
            if (samples[i] != samples[0])
                return false;
        }
        return true;
    }

    /// <summary>Computes the FIXED-predictor residual of the given order.</summary>
    private static void ComputeResidual(int[] samples, int count, int order, int[] residual)
    {
        switch (order)
        {
            case 0:
                for (int i = 0; i < count; i++)
                    residual[i] = samples[i];
                break;
            case 1:
                residual[0] = samples[0];
                for (int i = 1; i < count; i++)
                    residual[i] = samples[i] - samples[i - 1];
                break;
            case 2:
                residual[0] = samples[0];
                residual[1] = samples[1] - samples[0];
                for (int i = 2; i < count; i++)
                    residual[i] = samples[i] - 2 * samples[i - 1] + samples[i - 2];
                break;
            case 3:
                residual[0] = samples[0];
                residual[1] = samples[1] - samples[0];
                residual[2] = samples[2] - 2 * samples[1] + samples[0];
                for (int i = 3; i < count; i++)
                    residual[i] = samples[i] - 3 * samples[i - 1] + 3 * samples[i - 2] - samples[i - 3];
                break;
            case 4:
                residual[0] = samples[0];
                residual[1] = samples[1] - samples[0];
                residual[2] = samples[2] - 2 * samples[1] + samples[0];
                residual[3] = samples[3] - 3 * samples[2] + 3 * samples[1] - samples[0];
                for (int i = 4; i < count; i++)
                    residual[i] = samples[i] - 4 * samples[i - 1] + 6 * samples[i - 2] - 4 * samples[i - 3] + samples[i - 4];
                break;
        }
    }

    private static int ChooseRiceParameter(int[] residual, int count, int order)
    {
        long sum = 0;
        for (int i = order; i < count; i++)
        {
            sum += Math.Abs((long)residual[i]);
        }

        long mean = (count - order) > 0 ? sum / (count - order) : 0;
        int k = 0;
        while ((1L << (k + 1)) <= mean && k < 14)
        {
            k++;
        }
        return k;
    }
}