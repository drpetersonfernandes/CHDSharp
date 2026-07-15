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
    public static unsafe void FlacSamplesToBytes16(int[,] inSamples, int inSampleOffset,
        byte* outSamples, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleCount, inSamples.GetLength(0) - inSampleOffset);

        fixed (int* pInSamplesFixed = &inSamples[inSampleOffset, 0])
        {
            var pOutSamples = (short*)outSamples;
            for (var i = 0; i < loopCount; i++)
            {
                pOutSamples[i] = (short)pInSamplesFixed[i];
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
    public static unsafe void FlacSamplesToBytes16(int[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
            outSamples.Length - outByteOffset < loopCount * 2)
        {
            throw new ArgumentOutOfRangeException();
        }

        fixed (byte* pOutSamplesFixed = &outSamples[outByteOffset])
        {
            FlacSamplesToBytes16(inSamples, inSampleOffset, pOutSamplesFixed, sampleCount, channelCount);
        }
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
    public static unsafe void FlacSamplesToBytes24(int[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int wastedBits)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
            outSamples.Length - outByteOffset < loopCount * 3)
        {
            throw new ArgumentOutOfRangeException();
        }

        fixed (int* pInSamplesFixed = &inSamples[inSampleOffset, 0])
        {
            fixed (byte* pOutSamplesFixed = &outSamples[outByteOffset])
            {
                var pInSamples = pInSamplesFixed;
                var pOutSamples = pOutSamplesFixed;

                for (var i = 0; i < loopCount; i++)
                {
                    var sampleOut = (uint)*pInSamples++ << wastedBits;
                    *pOutSamples++ = (byte)(sampleOut & 0xFF);
                    sampleOut >>= 8;
                    *pOutSamples++ = (byte)(sampleOut & 0xFF);
                    sampleOut >>= 8;
                    *pOutSamples++ = (byte)(sampleOut & 0xFF);
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
    public static unsafe void FloatToBytes16(float[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
            outSamples.Length - outByteOffset < loopCount * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(inSampleOffset));
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
    public static void FloatToBytes(float[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int bitsPerSample)
    {
        switch (bitsPerSample)
        {
            case 16:
                FloatToBytes16(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount);
                break;
            case 32:
                Buffer.BlockCopy(inSamples, inSampleOffset * 4 * channelCount, outSamples, outByteOffset, sampleCount * 4 * channelCount);
                break;
            default:
                throw new NotSupportedException("Unsupported bitsPerSample value");
        }
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
    public static void FlacSamplesToBytes(int[,] inSamples, int inSampleOffset,
        byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int bitsPerSample)
    {
        switch (bitsPerSample)
        {
            case 16:
                FlacSamplesToBytes16(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount);
                break;
            case > 16 and <= 24:
                FlacSamplesToBytes24(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount, 24 - bitsPerSample);
                break;
            default:
                throw new NotSupportedException("Unsupported bitsPerSample value");
        }
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
    public static unsafe void FlacSamplesToBytes(int[,] inSamples, int inSampleOffset,
        byte* outSamples, int sampleCount, int channelCount, int bitsPerSample)
    {
        if (bitsPerSample == 16)
            FlacSamplesToBytes16(inSamples, inSampleOffset, outSamples, sampleCount, channelCount);
        else
            throw new NotSupportedException("Unsupported bitsPerSample value");
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
            throw new ArgumentOutOfRangeException(nameof(inByteOffset));

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
    public static unsafe void BytesToFlacSamples16(byte[] inSamples, int inByteOffset,
        int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.Length - inByteOffset < loopCount * 2 ||
            outSamples.GetLength(0) - outSampleOffset < sampleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(inByteOffset));
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
    public static unsafe void BytesToFlacSamples24(byte[] inSamples, int inByteOffset,
        int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount, int wastedBits)
    {
        var loopCount = sampleCount * channelCount;

        if (inSamples.Length - inByteOffset < loopCount * 3 ||
            outSamples.GetLength(0) - outSampleOffset < sampleCount)
            throw new ArgumentOutOfRangeException(nameof(inByteOffset));

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
    public static void BytesToFlacSamples(byte[] inSamples, int inByteOffset,
        int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount, int bitsPerSample)
    {
        switch (bitsPerSample)
        {
            case 16:
                BytesToFlacSamples16(inSamples, inByteOffset, outSamples, outSampleOffset, sampleCount, channelCount);
                break;
            case > 16 and <= 24:
                BytesToFlacSamples24(inSamples, inByteOffset, outSamples, outSampleOffset, sampleCount, channelCount, 24 - bitsPerSample);
                break;
            default:
                throw new NotSupportedException("Unsupported bitsPerSample value");
        }
    }

    #endregion

    private int[,] _samples = null!;
    private float[,] _fsamples = null!;
    private byte[] _bytes = null!;
    private bool _dataInSamples;
    private bool _dataInBytes;
    private bool _dataInFloat;

    /// <summary>
    /// Gets or sets the number of valid samples in the buffer.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Gets the total capacity of the buffer in samples.
    /// </summary>
    public int Size { get; private set; }

    /// <summary>
    /// Gets the PCM configuration for this buffer.
    /// </summary>
    public AudioPcmConfig Pcm { get; }

    /// <summary>
    /// Gets the length of valid data in bytes.
    /// </summary>
    public int ByteLength => Length * Pcm.BlockAlign;

    /// <summary>
    /// Gets the sample data as a 2D integer array. Converts from bytes on first access if necessary.
    /// </summary>
    public int[,] Samples
    {
        get
        {
            if (_samples == null || _samples.GetLength(0) < Length)
            {
                _samples = new int[Size, Pcm.ChannelCount];
            }

            if (!_dataInSamples && _dataInBytes && Length != 0)
                BytesToFlacSamples(_bytes, 0, _samples, 0, Length, Pcm.ChannelCount, Pcm.BitsPerSample);
            _dataInSamples = true;
            return _samples;
        }
    }

    /// <summary>
    /// Gets the sample data as a 2D floating-point array. Converts from bytes on first access if necessary.
    /// </summary>
    public float[,] FloatSamples
    {
        get
        {
            if (_fsamples == null || _fsamples.GetLength(0) < Length)
            {
                _fsamples = new float[Size, Pcm.ChannelCount];
            }

            if (!_dataInFloat && _dataInBytes && Length != 0)
            {
                switch (Pcm.BitsPerSample)
                {
                    case 16:
                        Bytes16ToFloat(_bytes, 0, _fsamples, 0, Length, Pcm.ChannelCount);
                        break;
                    case 32:
                        Buffer.BlockCopy(_bytes, 0, _fsamples, 0, Length * 4 * Pcm.ChannelCount);
                        break;
                    default:
                        throw new NotSupportedException("Unsupported bitsPerSample value");
                }
            }

            _dataInFloat = true;
            return _fsamples;
        }
    }

    /// <summary>
    /// Gets the raw byte representation of the sample data. Converts from samples or floats on first access if necessary.
    /// </summary>
    public byte[] Bytes
    {
        get
        {
            if (_bytes == null || _bytes.Length < Length * Pcm.BlockAlign)
            {
                _bytes = new byte[Size * Pcm.BlockAlign];
            }

            if (!_dataInBytes && Length != 0)
            {
                if (_dataInSamples)
                    FlacSamplesToBytes(_samples, 0, _bytes, 0, Length, Pcm.ChannelCount, Pcm.BitsPerSample);
                else if (_dataInFloat)
                    FloatToBytes(_fsamples, 0, _bytes, 0, Length, Pcm.ChannelCount, Pcm.BitsPerSample);
            }

            _dataInBytes = true;
            return _bytes;
        }
    }

    /// <summary>
    /// Initializes a new empty <see cref="AudioBuffer"/> with the given PCM configuration and capacity.
    /// </summary>
    /// <param name="pcm">The PCM configuration.</param>
    /// <param name="size">The buffer capacity in samples.</param>
    public AudioBuffer(AudioPcmConfig pcm, int size)
    {
        Pcm = pcm;
        Size = size;
        Length = 0;
    }

    /// <summary>
    /// Initializes a new <see cref="AudioBuffer"/> from an existing integer sample array.
    /// </summary>
    /// <param name="pcm">The PCM configuration.</param>
    /// <param name="samples">The 2D sample array.</param>
    /// <param name="length">The number of valid samples in the array.</param>
    public AudioBuffer(AudioPcmConfig pcm, int[,] samples, int length)
    {
        Pcm = pcm;
        // assert _samples.GetLength(1) == pcm.ChannelCount
        Prepare(samples, length);
    }

    /// <summary>
    /// Initializes a new <see cref="AudioBuffer"/> from an existing byte array.
    /// </summary>
    /// <param name="pcm">The PCM configuration.</param>
    /// <param name="bytes">The byte array containing PCM data.</param>
    /// <param name="length">The number of valid samples.</param>
    public AudioBuffer(AudioPcmConfig pcm, byte[] bytes, int length)
    {
        Pcm = pcm;
        Prepare(bytes, length);
    }

    /// <summary>
    /// Initializes a new empty <see cref="AudioBuffer"/> from a source's PCM configuration.
    /// </summary>
    /// <param name="source">The audio source whose PCM configuration to use.</param>
    /// <param name="size">The buffer capacity in samples.</param>
    public AudioBuffer(IAudioSource source, int size)
    {
        Pcm = source.Pcm;
        Size = size;
    }

    /// <summary>
    /// Prepares the buffer for reading from a source, validating format compatibility and clamping length.
    /// </summary>
    /// <param name="source">The audio source.</param>
    /// <param name="maxLength">The maximum number of samples to read.</param>
    /// <exception cref="Exception">Thrown if the source PCM format does not match.</exception>
    public void Prepare(IAudioSource source, int maxLength)
    {
        if (source.Pcm.ChannelCount != Pcm.ChannelCount || source.Pcm.BitsPerSample != Pcm.BitsPerSample)
            throw new InvalidOperationException("AudioBuffer format mismatch");

        Length = Size;
        if (maxLength >= 0)
        {
            Length = Math.Min(Length, maxLength);
        }

        if (source.Remaining >= 0)
        {
            Length = (int)Math.Min(Length, source.Remaining);
        }

        _dataInBytes = false;
        _dataInSamples = false;
        _dataInFloat = false;
    }

    /// <summary>
    /// Prepares the buffer by resetting the length and invalidating cached data representations.
    /// </summary>
    /// <param name="maxLength">The maximum number of samples to read.</param>
    public void Prepare(int maxLength)
    {
        Length = Size;
        if (maxLength >= 0)
        {
            Length = Math.Min(Length, maxLength);
        }

        _dataInBytes = false;
        _dataInSamples = false;
        _dataInFloat = false;
    }

    /// <summary>
    /// Prepares the buffer with existing integer sample data.
    /// </summary>
    /// <param name="samples">The 2D sample array.</param>
    /// <param name="length">The number of valid samples.</param>
    /// <exception cref="Exception">Thrown if <paramref name="length"/> exceeds the array capacity.</exception>
    public void Prepare(int[,] samples, int length)
    {
        Length = length;
        Size = samples.GetLength(0);
        _samples = samples;
        _dataInSamples = true;
        _dataInBytes = false;
        _dataInFloat = false;
        if (Length > Size)
            throw new ArgumentException("Invalid length");
    }

    /// <summary>
    /// Prepares the buffer with existing byte data.
    /// </summary>
    /// <param name="bytes">The byte array containing PCM data.</param>
    /// <param name="length">The number of valid samples.</param>
    /// <exception cref="Exception">Thrown if <paramref name="length"/> exceeds the computed capacity.</exception>
    public void Prepare(byte[] bytes, int length)
    {
        Length = length;
        Size = bytes.Length / Pcm.BlockAlign;
        _bytes = bytes;
        _dataInSamples = false;
        _dataInBytes = true;
        _dataInFloat = false;
        if (Length > Size)
            throw new ArgumentException("Invalid length");
    }

    internal void Load(int dstOffset, AudioBuffer src, int srcOffset, int copyLength)
    {
        if (_dataInBytes)
            Buffer.BlockCopy(src.Bytes, srcOffset * Pcm.BlockAlign, Bytes, dstOffset * Pcm.BlockAlign, copyLength * Pcm.BlockAlign);
        if (_dataInSamples)
            Buffer.BlockCopy(src.Samples, srcOffset * Pcm.ChannelCount * 4, Samples, dstOffset * Pcm.ChannelCount * 4, copyLength * Pcm.ChannelCount * 4);
        if (_dataInFloat)
            Buffer.BlockCopy(src.FloatSamples, srcOffset * Pcm.ChannelCount * 4, FloatSamples, dstOffset * Pcm.ChannelCount * 4, copyLength * Pcm.ChannelCount * 4);
    }

    /// <summary>
    /// Prepares the buffer by copying a segment from another buffer.
    /// </summary>
    /// <param name="src">The source audio buffer.</param>
    /// <param name="offset">The offset in the source buffer.</param>
    /// <param name="length">The maximum number of samples to copy.</param>
    public void Prepare(AudioBuffer src, int offset, int length)
    {
        Length = Math.Min(Size, src.Length - offset);
        if (length >= 0)
        {
            Length = Math.Min(Length, length);
        }

        _dataInBytes = false;
        _dataInFloat = false;
        _dataInSamples = false;
        if (src._dataInBytes)
        {
            _dataInBytes = true;
        }
        else if (src._dataInSamples)
        {
            _dataInSamples = true;
        }
        else if (src._dataInFloat)
        {
            _dataInFloat = true;
        }

        Load(0, src, offset, Length);
    }

    /// <summary>
    /// Swaps the internal data of this buffer with another, resetting the other buffer to empty.
    /// </summary>
    /// <param name="buffer">The buffer to swap with.</param>
    /// <exception cref="Exception">Thrown if the PCM formats do not match.</exception>
    public void Swap(AudioBuffer buffer)
    {
        if (Pcm.BitsPerSample != buffer.Pcm.BitsPerSample || Pcm.ChannelCount != buffer.Pcm.ChannelCount)
            throw new InvalidOperationException("AudioBuffer format mismatch");

        var samplesTmp = _samples;
        var floatsTmp = _fsamples;
        var bytesTmp = _bytes;

        _fsamples = buffer._fsamples;
        _samples = buffer._samples;
        _bytes = buffer._bytes;
        Length = buffer.Length;
        Size = buffer.Size;
        _dataInSamples = buffer._dataInSamples;
        _dataInBytes = buffer._dataInBytes;
        _dataInFloat = buffer._dataInFloat;

        buffer._samples = samplesTmp;
        buffer._bytes = bytesTmp;
        buffer._fsamples = floatsTmp;
        buffer.Length = 0;
        buffer._dataInSamples = false;
        buffer._dataInBytes = false;
        buffer._dataInFloat = false;
    }

    /// <summary>
    /// Interlaces two mono sample buffers into the stereo byte buffer at the specified position. Operates on raw pointers.
    /// </summary>
    /// <param name="pos">The sample position in the output buffer to start writing.</param>
    /// <param name="src1">Pointer to the left channel samples.</param>
    /// <param name="src2">Pointer to the right channel samples.</param>
    /// <param name="n">Number of sample pairs to interlace.</param>
    /// <exception cref="Exception">Thrown if the PCM is not stereo or the bit depth is not 16 or 24.</exception>
    public unsafe void Interlace(int pos, int* src1, int* src2, int n)
    {
        if (Pcm.ChannelCount != 2)
        {
            throw new InvalidOperationException("Must be stereo");
        }

        switch (Pcm.BitsPerSample)
        {
            case 16:
            {
                fixed (byte* bs = Bytes)
                {
                    var res = (int*)bs + pos;
                    for (var i = n; i > 0; i--)
                    {
                        *res++ = (*src1++ & 0xffff) ^ (*src2++ << 16);
                    }
                }

                break;
            }
            case 24:
            {
                fixed (byte* bs = Bytes)
                {
                    var res = bs + pos * 6;
                    for (var i = n; i > 0; i--)
                    {
                        var sampleOut = (uint)*src1++;
                        *res++ = (byte)(sampleOut & 0xFF);
                        sampleOut >>= 8;
                        *res++ = (byte)(sampleOut & 0xFF);
                        sampleOut >>= 8;
                        *res++ = (byte)(sampleOut & 0xFF);
                        sampleOut = (uint)*src2++;
                        *res++ = (byte)(sampleOut & 0xFF);
                        sampleOut >>= 8;
                        *res++ = (byte)(sampleOut & 0xFF);
                        sampleOut >>= 8;
                        *res++ = (byte)(sampleOut & 0xFF);
                    }
                }

                break;
            }
            default:
                throw new NotSupportedException("Unsupported BPS");
        }
    }
}
