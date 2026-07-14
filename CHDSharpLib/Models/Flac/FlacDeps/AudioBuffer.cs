using CHDSharp.Interfaces.Flac.FlacDeps;

namespace CHDSharp.Models.Flac.FlacDeps;

/// <summary>
/// Represents a buffer for audio sample data, supporting conversion between byte, sample, and float representations across multiple bit depths.
/// </summary>
public class AudioBuffer
{
    #region Static Methods

    /// <summary>
    /// Converts FLAC samples to 16-bit interleaved bytes. Operates on raw pointers.
    /// </summary>
    /// <param name="inSamples">2D array of samples [sampleIndex, channel].</param>
    /// <param name="inSampleOffset">Offset into the samples array to start reading.</param>
    /// <param name="outSamples">Destination byte buffer pointer.</param>
    /// <param name="sampleCount">Number of samples (per channel) to convert.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown if the sample offset exceeds the array bounds.</exception>
    public static unsafe void FLACSamplesToBytes_16(int[,] inSamples, int inSampleOffset,
        byte* outSamples, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.GetLength(0) - inSampleOffset < sampleCount)
            throw new IndexOutOfRangeException();

        fixed (int* pInSamplesFixed = &inSamples[inSampleOffset, 0])
        {
            var pInSamples = pInSamplesFixed;
            var pOutSamples = (short*)outSamples;
            for (var i = 0; i < loopCount; i++)
            {
                pOutSamples[i] = (short)pInSamples[i];
            }
            //*(pOutSamples++) = (short)*(pInSamples++);
        }
    }

    /// <summary>
    /// Converts FLAC samples to 16-bit interleaved bytes into a managed byte array.
    /// </summary>
    /// <param name="inSamples">2D array of samples [sampleIndex, channel].</param>
    /// <param name="inSampleOffset">Offset into the samples array to start reading.</param>
    /// <param name="outSamples">Destination byte array.</param>
    /// <param name="outByteOffset">Offset into the byte array to start writing.</param>
    /// <param name="sampleCount">Number of samples (per channel) to convert.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown if array bounds are exceeded.</exception>
    public static unsafe void FLACSamplesToBytes_16(int[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
            outSamples.Length - outByteOffset < loopCount * 2)
        {
            throw new IndexOutOfRangeException();
        }

        fixed (byte* pOutSamplesFixed = &outSamples[outByteOffset])
            FLACSamplesToBytes_16(inSamples, inSampleOffset, pOutSamplesFixed, sampleCount, channelCount);
    }

    /// <summary>
    /// Converts FLAC samples to 24-bit interleaved bytes into a managed byte array.
    /// </summary>
    /// <param name="inSamples">2D array of samples.</param>
    /// <param name="inSampleOffset">Offset into the samples array.</param>
    /// <param name="outSamples">Destination byte array.</param>
    /// <param name="outByteOffset">Offset into the byte array.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <param name="wastedBits">Number of wasted bits to shift the samples left before conversion.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown if array bounds are exceeded.</exception>
    public static unsafe void FLACSamplesToBytes_24(int[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int wastedBits)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
            outSamples.Length - outByteOffset < loopCount * 3)
        {
            throw new IndexOutOfRangeException();
        }

        fixed (int* pInSamplesFixed = &inSamples[inSampleOffset, 0])
        {
            fixed (byte* pOutSamplesFixed = &outSamples[outByteOffset])
            {
                var pInSamples = pInSamplesFixed;
                var pOutSamples = pOutSamplesFixed;

                for (var i = 0; i < loopCount; i++)
                {
                    var sample_out = (uint)*pInSamples++ << wastedBits;
                    *pOutSamples++ = (byte)(sample_out & 0xFF);
                    sample_out >>= 8;
                    *pOutSamples++ = (byte)(sample_out & 0xFF);
                    sample_out >>= 8;
                    *pOutSamples++ = (byte)(sample_out & 0xFF);
                }
            }
        }
    }

    /// <summary>
    /// Converts floating-point samples to 16-bit integer bytes.
    /// </summary>
    /// <param name="inSamples">2D array of float samples.</param>
    /// <param name="inSampleOffset">Offset into the samples array.</param>
    /// <param name="outSamples">Destination byte array.</param>
    /// <param name="outByteOffset">Offset into the byte array.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown if array bounds are exceeded.</exception>
    public static unsafe void FloatToBytes_16(float[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
            outSamples.Length - outByteOffset < loopCount * 2)
        {
            throw new IndexOutOfRangeException();
        }

        fixed (float* pInSamplesFixed = &inSamples[inSampleOffset, 0])
        {
            fixed (byte* pOutSamplesFixed = &outSamples[outByteOffset])
            {
                var pInSamples = pInSamplesFixed;
                var pOutSamples = (short*)pOutSamplesFixed;

                for (var i = 0; i < loopCount; i++)
                {
                    *pOutSamples++ = (short)(32758 * *pInSamples++);
                }
            }
        }
    }

    /// <summary>
    /// Converts floating-point samples to bytes at the specified bit depth.
    /// </summary>
    /// <param name="inSamples">2D array of float samples.</param>
    /// <param name="inSampleOffset">Offset into the samples array.</param>
    /// <param name="outSamples">Destination byte array.</param>
    /// <param name="outByteOffset">Offset into the byte array.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <param name="bitsPerSample">Target bits per sample (16 or 32).</param>
    /// <exception cref="Exception">Thrown if <paramref name="bitsPerSample"/> is not 16 or 32.</exception>
    public static unsafe void FloatToBytes(float[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int bitsPerSample)
    {
        if (bitsPerSample == 16)
            FloatToBytes_16(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount);
        //else if (bitsPerSample > 16 && bitsPerSample <= 24)
        //    FLACSamplesToBytes_24(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount, 24 - bitsPerSample);
        else if (bitsPerSample == 32)
            Buffer.BlockCopy(inSamples, inSampleOffset * 4 * channelCount, outSamples, outByteOffset, sampleCount * 4 * channelCount);
        else
            throw new Exception("Unsupported bitsPerSample value");
    }

    /// <summary>
    /// Converts FLAC samples to bytes at the specified bit depth into a managed byte array.
    /// </summary>
    /// <param name="inSamples">2D array of samples.</param>
    /// <param name="inSampleOffset">Offset into the samples array.</param>
    /// <param name="outSamples">Destination byte array.</param>
    /// <param name="outByteOffset">Offset into the byte array.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <param name="bitsPerSample">Bits per sample (16 or up to 24).</param>
    /// <exception cref="Exception">Thrown if <paramref name="bitsPerSample"/> is not supported.</exception>
    public static unsafe void FLACSamplesToBytes(int[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int bitsPerSample)
    {
        if (bitsPerSample == 16)
            FLACSamplesToBytes_16(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount);
        else if (bitsPerSample > 16 && bitsPerSample <= 24)
            FLACSamplesToBytes_24(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount, 24 - bitsPerSample);
        else
            throw new Exception("Unsupported bitsPerSample value");
    }

    /// <summary>
    /// Converts FLAC samples to bytes at the specified bit depth. Operates on raw pointers.
    /// </summary>
    /// <param name="inSamples">2D array of samples.</param>
    /// <param name="inSampleOffset">Offset into the samples array.</param>
    /// <param name="outSamples">Destination byte buffer pointer.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <param name="bitsPerSample">Bits per sample (must be 16).</param>
    /// <exception cref="Exception">Thrown if <paramref name="bitsPerSample"/> is not 16.</exception>
    public static unsafe void FLACSamplesToBytes(int[,] inSamples, int inSampleOffset,
        byte* outSamples, int sampleCount, int channelCount, int bitsPerSample)
    {
        if (bitsPerSample == 16)
            FLACSamplesToBytes_16(inSamples, inSampleOffset, outSamples, sampleCount, channelCount);
        else
            throw new Exception("Unsupported bitsPerSample value");
    }

    /// <summary>
    /// Converts 16-bit integer bytes to floating-point samples.
    /// </summary>
    /// <param name="inSamples">Source byte array containing 16-bit PCM data.</param>
    /// <param name="inByteOffset">Offset into the byte array.</param>
    /// <param name="outSamples">Destination 2D float array.</param>
    /// <param name="outSampleOffset">Offset into the float array.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown if array bounds are exceeded.</exception>
    public static unsafe void Bytes16ToFloat(byte[] inSamples, int inByteOffset,
        float[,] outSamples, int outSampleOffset, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.Length - inByteOffset < loopCount * 2 ||
            outSamples.GetLength(0) - outSampleOffset < sampleCount)
            throw new IndexOutOfRangeException();

        fixed (byte* pInSamplesFixed = &inSamples[inByteOffset])
        {
            fixed (float* pOutSamplesFixed = &outSamples[outSampleOffset, 0])
            {
                var pInSamples = (short*)pInSamplesFixed;
                var pOutSamples = pOutSamplesFixed;
                for (var i = 0; i < loopCount; i++)
                {
                    *pOutSamples++ = *pInSamples++ / 32768.0f;
                }
            }
        }
    }

    /// <summary>
    /// Converts 16-bit integer bytes to FLAC integer samples.
    /// </summary>
    /// <param name="inSamples">Source byte array containing 16-bit PCM data.</param>
    /// <param name="inByteOffset">Offset into the byte array.</param>
    /// <param name="outSamples">Destination 2D int sample array.</param>
    /// <param name="outSampleOffset">Offset into the sample array.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown if array bounds are exceeded.</exception>
    public static unsafe void BytesToFLACSamples_16(byte[] inSamples, int inByteOffset,
        int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.Length - inByteOffset < loopCount * 2 ||
            outSamples.GetLength(0) - outSampleOffset < sampleCount)
        {
            throw new IndexOutOfRangeException();
        }

        fixed (byte* pInSamplesFixed = &inSamples[inByteOffset])
        {
            fixed (int* pOutSamplesFixed = &outSamples[outSampleOffset, 0])
            {
                var pInSamples = (short*)pInSamplesFixed;
                var pOutSamples = pOutSamplesFixed;

                for (var i = 0; i < loopCount; i++)
                {
                    *pOutSamples++ = *pInSamples++;
                }
            }
        }
    }

    /// <summary>
    /// Converts 24-bit integer bytes to FLAC integer samples.
    /// </summary>
    /// <param name="inSamples">Source byte array containing 24-bit PCM data.</param>
    /// <param name="inByteOffset">Offset into the byte array.</param>
    /// <param name="outSamples">Destination 2D int sample array.</param>
    /// <param name="outSampleOffset">Offset into the sample array.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <param name="wastedBits">Number of wasted bits to shift right after conversion.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown if array bounds are exceeded.</exception>
    public static unsafe void BytesToFLACSamples_24(byte[] inSamples, int inByteOffset,
        int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount, int wastedBits)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.Length - inByteOffset < loopCount * 3 ||
            outSamples.GetLength(0) - outSampleOffset < sampleCount)
            throw new IndexOutOfRangeException();

        fixed (byte* pInSamplesFixed = &inSamples[inByteOffset])
        {
            fixed (int* pOutSamplesFixed = &outSamples[outSampleOffset, 0])
            {
                var pInSamples = pInSamplesFixed;
                var pOutSamples = pOutSamplesFixed;
                for (var i = 0; i < loopCount; i++)
                {
                    int sample = *pInSamples++;
                    sample += *pInSamples++ << 8;
                    sample += *pInSamples++ << 16;
                    *pOutSamples++ = (sample << 8) >> (8 + wastedBits);
                }
            }
        }
    }

    /// <summary>
    /// Converts integer bytes to FLAC integer samples at the specified bit depth.
    /// </summary>
    /// <param name="inSamples">Source byte array.</param>
    /// <param name="inByteOffset">Offset into the byte array.</param>
    /// <param name="outSamples">Destination 2D int sample array.</param>
    /// <param name="outSampleOffset">Offset into the sample array.</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="channelCount">Number of channels.</param>
    /// <param name="bitsPerSample">Bits per sample (16 or up to 24).</param>
    /// <exception cref="Exception">Thrown if <paramref name="bitsPerSample"/> is not supported.</exception>
    public static unsafe void BytesToFLACSamples(byte[] inSamples, int inByteOffset,
        int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount, int bitsPerSample)
    {
        if (bitsPerSample == 16)
            BytesToFLACSamples_16(inSamples, inByteOffset, outSamples, outSampleOffset, sampleCount, channelCount);
        else if (bitsPerSample > 16 && bitsPerSample <= 24)
            BytesToFLACSamples_24(inSamples, inByteOffset, outSamples, outSampleOffset, sampleCount, channelCount, 24 - bitsPerSample);
        else
            throw new Exception("Unsupported bitsPerSample value");
    }

    #endregion

    private int[,] samples;
    private float[,] fsamples;
    private byte[] bytes;
    private int length;
    private int size;
    private AudioPCMConfig pcm;
    private bool dataInSamples = false;
    private bool dataInBytes = false;
    private bool dataInFloat = false;

    /// <summary>
    /// Gets or sets the number of valid samples in the buffer.
    /// </summary>
    public int Length
    {
        get { return length; }
        set { length = value; }
    }

    /// <summary>
    /// Gets the total capacity of the buffer in samples.
    /// </summary>
    public int Size
    {
        get { return size; }
    }

    /// <summary>
    /// Gets the PCM configuration for this buffer.
    /// </summary>
    public AudioPCMConfig PCM { get { return pcm; } }

    /// <summary>
    /// Gets the length of valid data in bytes.
    /// </summary>
    public int ByteLength
    {
        get
        {
            return length * pcm.BlockAlign;
        }
    }

    /// <summary>
    /// Gets the sample data as a 2D integer array. Converts from bytes on first access if necessary.
    /// </summary>
    public int[,] Samples
    {
        get
        {
            if (samples == null || samples.GetLength(0) < length)
            {
                samples = new int[size, pcm.ChannelCount];
            }

            if (!dataInSamples && dataInBytes && length != 0)
                BytesToFLACSamples(bytes, 0, samples, 0, length, pcm.ChannelCount, pcm.BitsPerSample);
            dataInSamples = true;
            return samples;
        }
    }

    /// <summary>
    /// Gets the sample data as a 2D floating-point array. Converts from bytes on first access if necessary.
    /// </summary>
    public float[,] Float
    {
        get
        {
            if (fsamples == null || fsamples.GetLength(0) < length)
            {
                fsamples = new float[size, pcm.ChannelCount];
            }

            if (!dataInFloat && dataInBytes && length != 0)
            {
                if (pcm.BitsPerSample == 16)
                    Bytes16ToFloat(bytes, 0, fsamples, 0, length, pcm.ChannelCount);
                //else if (pcm.BitsPerSample > 16 && PCM.BitsPerSample <= 24)
                //    BytesToFLACSamples_24(bytes, 0, fsamples, 0, length, pcm.ChannelCount, 24 - pcm.BitsPerSample);
                else if (pcm.BitsPerSample == 32)
                    Buffer.BlockCopy(bytes, 0, fsamples, 0, length * 4 * pcm.ChannelCount);
                else
                    throw new Exception("Unsupported bitsPerSample value");
            }
            dataInFloat = true;
            return fsamples;
        }
    }

    /// <summary>
    /// Gets the raw byte representation of the sample data. Converts from samples or floats on first access if necessary.
    /// </summary>
    public byte[] Bytes
    {
        get
        {
            if (bytes == null || bytes.Length < length * pcm.BlockAlign)
            {
                bytes = new byte[size * pcm.BlockAlign];
            }

            if (!dataInBytes && length != 0)
            {
                if (dataInSamples)
                    FLACSamplesToBytes(samples, 0, bytes, 0, length, pcm.ChannelCount, pcm.BitsPerSample);
                else if (dataInFloat)
                    FloatToBytes(fsamples, 0, bytes, 0, length, pcm.ChannelCount, pcm.BitsPerSample);
            }
            dataInBytes = true;
            return bytes;
        }
    }

    /// <summary>
    /// Initializes a new empty <see cref="AudioBuffer"/> with the given PCM configuration and capacity.
    /// </summary>
    /// <param name="_pcm">The PCM configuration.</param>
    /// <param name="_size">The buffer capacity in samples.</param>
    public AudioBuffer(AudioPCMConfig _pcm, int _size)
    {
        pcm = _pcm;
        size = _size;
        length = 0;
    }

    /// <summary>
    /// Initializes a new <see cref="AudioBuffer"/> from an existing integer sample array.
    /// </summary>
    /// <param name="_pcm">The PCM configuration.</param>
    /// <param name="_samples">The 2D sample array.</param>
    /// <param name="_length">The number of valid samples in the array.</param>
    public AudioBuffer(AudioPCMConfig _pcm, int[,] _samples, int _length)
    {
        pcm = _pcm;
        // assert _samples.GetLength(1) == pcm.ChannelCount
        Prepare(_samples, _length);
    }

    /// <summary>
    /// Initializes a new <see cref="AudioBuffer"/> from an existing byte array.
    /// </summary>
    /// <param name="_pcm">The PCM configuration.</param>
    /// <param name="_bytes">The byte array containing PCM data.</param>
    /// <param name="_length">The number of valid samples.</param>
    public AudioBuffer(AudioPCMConfig _pcm, byte[] _bytes, int _length)
    {
        pcm = _pcm;
        Prepare(_bytes, _length);
    }

    /// <summary>
    /// Initializes a new empty <see cref="AudioBuffer"/> from a source's PCM configuration.
    /// </summary>
    /// <param name="source">The audio source whose PCM configuration to use.</param>
    /// <param name="_size">The buffer capacity in samples.</param>
    public AudioBuffer(IAudioSource source, int _size)
    {
        pcm = source.PCM;
        size = _size;
    }

    /// <summary>
    /// Prepares the buffer for writing to a destination (no-op, reserved for future format validation).
    /// </summary>
    /// <param name="dest">The output destination.</param>
    public void Prepare(IAudioDest dest)
    {
        //if (dest.Settings.PCM.ChannelCount != pcm.ChannelCount || dest.Settings.PCM.BitsPerSample != pcm.BitsPerSample)
        //    throw new Exception("AudioBuffer format mismatch");
    }

    /// <summary>
    /// Prepares the buffer for reading from a source, validating format compatibility and clamping length.
    /// </summary>
    /// <param name="source">The audio source.</param>
    /// <param name="maxLength">The maximum number of samples to read.</param>
    /// <exception cref="Exception">Thrown if the source PCM format does not match.</exception>
    public void Prepare(IAudioSource source, int maxLength)
    {
        if (source.PCM.ChannelCount != pcm.ChannelCount || source.PCM.BitsPerSample != pcm.BitsPerSample)
            throw new Exception("AudioBuffer format mismatch");

        length = size;
        if (maxLength >= 0)
        {
            length = Math.Min(length, maxLength);
        }

        if (source.Remaining >= 0)
        {
            length = (int)Math.Min(length, source.Remaining);
        }

        dataInBytes = false;
        dataInSamples = false;
        dataInFloat = false;
    }

    /// <summary>
    /// Prepares the buffer by resetting the length and invalidating cached data representations.
    /// </summary>
    /// <param name="maxLength">The maximum number of samples to read.</param>
    public void Prepare(int maxLength)
    {
        length = size;
        if (maxLength >= 0)
        {
            length = Math.Min(length, maxLength);
        }

        dataInBytes = false;
        dataInSamples = false;
        dataInFloat = false;
    }

    /// <summary>
    /// Prepares the buffer with existing integer sample data.
    /// </summary>
    /// <param name="_samples">The 2D sample array.</param>
    /// <param name="_length">The number of valid samples.</param>
    /// <exception cref="Exception">Thrown if <paramref name="_length"/> exceeds the array capacity.</exception>
    public void Prepare(int[,] _samples, int _length)
    {
        length = _length;
        size = _samples.GetLength(0);
        samples = _samples;
        dataInSamples = true;
        dataInBytes = false;
        dataInFloat = false;
        if (length > size)
            throw new Exception("Invalid length");
    }

    /// <summary>
    /// Prepares the buffer with existing byte data.
    /// </summary>
    /// <param name="_bytes">The byte array containing PCM data.</param>
    /// <param name="_length">The number of valid samples.</param>
    /// <exception cref="Exception">Thrown if <paramref name="_length"/> exceeds the computed capacity.</exception>
    public void Prepare(byte[] _bytes, int _length)
    {
        length = _length;
        size = _bytes.Length / PCM.BlockAlign;
        bytes = _bytes;
        dataInSamples = false;
        dataInBytes = true;
        dataInFloat = false;
        if (length > size)
            throw new Exception("Invalid length");
    }

    internal unsafe void Load(int dstOffset, AudioBuffer src, int srcOffset, int copyLength)
    {
        if (dataInBytes)
            Buffer.BlockCopy(src.Bytes, srcOffset * pcm.BlockAlign, Bytes, dstOffset * pcm.BlockAlign, copyLength * pcm.BlockAlign);
        if (dataInSamples)
            Buffer.BlockCopy(src.Samples, srcOffset * pcm.ChannelCount * 4, Samples, dstOffset * pcm.ChannelCount * 4, copyLength * pcm.ChannelCount * 4);
        if (dataInFloat)
            Buffer.BlockCopy(src.Float, srcOffset * pcm.ChannelCount * 4, Float, dstOffset * pcm.ChannelCount * 4, copyLength * pcm.ChannelCount * 4);
    }

    /// <summary>
    /// Prepares the buffer by copying a segment from another buffer.
    /// </summary>
    /// <param name="_src">The source audio buffer.</param>
    /// <param name="_offset">The offset in the source buffer.</param>
    /// <param name="_length">The maximum number of samples to copy.</param>
    public unsafe void Prepare(AudioBuffer _src, int _offset, int _length)
    {
        length = Math.Min(size, _src.Length - _offset);
        if (_length >= 0)
        {
            length = Math.Min(length, _length);
        }

        dataInBytes = false;
        dataInFloat = false;
        dataInSamples = false;
        if (_src.dataInBytes)
        {
            dataInBytes = true;
        }
        else if (_src.dataInSamples)
        {
            dataInSamples = true;
        }
        else if (_src.dataInFloat)
        {
            dataInFloat = true;
        }

        Load(0, _src, _offset, length);
    }

    /// <summary>
    /// Swaps the internal data of this buffer with another, resetting the other buffer to empty.
    /// </summary>
    /// <param name="buffer">The buffer to swap with.</param>
    /// <exception cref="Exception">Thrown if the PCM formats do not match.</exception>
    public void Swap(AudioBuffer buffer)
    {
        if (pcm.BitsPerSample != buffer.PCM.BitsPerSample || pcm.ChannelCount != buffer.PCM.ChannelCount)
            throw new Exception("AudioBuffer format mismatch");

        var samplesTmp = samples;
        var floatsTmp = fsamples;
        var bytesTmp = bytes;

        fsamples = buffer.fsamples;
        samples = buffer.samples;
        bytes = buffer.bytes;
        length = buffer.length;
        size = buffer.size;
        dataInSamples = buffer.dataInSamples;
        dataInBytes = buffer.dataInBytes;
        dataInFloat = buffer.dataInFloat;

        buffer.samples = samplesTmp;
        buffer.bytes = bytesTmp;
        buffer.fsamples = floatsTmp;
        buffer.length = 0;
        buffer.dataInSamples = false;
        buffer.dataInBytes = false;
        buffer.dataInFloat = false;
    }

    /// <summary>
    /// Interlaces two mono sample buffers into the stereo byte buffer at the specified position. Operates on raw pointers.
    /// </summary>
    /// <param name="pos">The sample position in the output buffer to start writing.</param>
    /// <param name="src1">Pointer to the left channel samples.</param>
    /// <param name="src2">Pointer to the right channel samples.</param>
    /// <param name="n">Number of sample pairs to interlace.</param>
    /// <exception cref="Exception">Thrown if the PCM is not stereo or the bit depth is not 16 or 24.</exception>
    unsafe public void Interlace(int pos, int* src1, int* src2, int n)
    {
        if (PCM.ChannelCount != 2)
        {
            throw new Exception("Must be stereo");
        }
        if (PCM.BitsPerSample == 16)
        {
            fixed (byte* bs = Bytes)
            {
                var res = (int*)bs + pos;
                for (var i = n; i > 0; i--)
                {
                    *res++ = (*src1++ & 0xffff) ^ (*src2++ << 16);
                }
            }
        }
        else if (PCM.BitsPerSample == 24)
        {
            fixed (byte* bs = Bytes)
            {
                var res = bs + pos * 6;
                for (var i = n; i > 0; i--)
                {
                    var sample_out = (uint)*src1++;
                    *res++ = (byte)(sample_out & 0xFF);
                    sample_out >>= 8;
                    *res++ = (byte)(sample_out & 0xFF);
                    sample_out >>= 8;
                    *res++ = (byte)(sample_out & 0xFF);
                    sample_out = (uint)*src2++;
                    *res++ = (byte)(sample_out & 0xFF);
                    sample_out >>= 8;
                    *res++ = (byte)(sample_out & 0xFF);
                    sample_out >>= 8;
                    *res++ = (byte)(sample_out & 0xFF);
                }
            }
        }
        else
        {
            throw new Exception("Unsupported BPS");
        }
    }

    //public void Clear()
    //{
    //    length = 0;
    //}
}
