using CHDSharp.Flac.FlacDeps;
using CHDSharp.Interfaces.Flac.FlacDeps;
using CHDSharp.Models.Flac;
using CHDSharp.Models.Flac.FlacDeps;

namespace CHDSharp.Flac;

/// <summary>
/// FLAC audio decoder that reads and decodes FLAC streams into PCM audio samples.
/// Implements <see cref="IAudioSource"/> for integration with the audio pipeline.
/// </summary>
public class AudioDecoder : IAudioSource
{
    /// <summary>Buffer used to store residual values during subframe decoding. Allocated to <see cref="FlakeConstants.MAXBLOCKSIZE"/> × channel count ints.</summary>
    private readonly int[] _residualBuffer;

    /// <summary>Buffer that holds raw FLAC frame data read from the input stream.</summary>
    private readonly byte[] _framesBuffer;

    /// <summary>Number of valid bytes currently held in <see cref="_framesBuffer"/>.</summary>
    private int _framesBufferLength;

    /// <summary>Start offset within <see cref="_framesBuffer"/> of the next unprocessed data.</summary>
    private int _framesBufferOffset;

    /// <summary>File position at which the first audio frame begins (after all metadata blocks).</summary>
    private long _firstFrameOffset;

    /// <summary>Optional seek table parsed from the FLAC metadata; <c>null</c> if no seek table is present.</summary>
    private SeekPoint[]? _seekTable;

    /// <summary>CRC-8 calculator used for frame header integrity checks.</summary>
    private readonly Crc8 _crc8;

    /// <summary>Reused <see cref="FlacFrame"/> instance that holds the currently decoded frame's header and subframe state.</summary>
    private readonly FlacFrame _frame;

    /// <summary>Bit-level reader wrapping the raw frame data in <see cref="_framesBuffer"/>.</summary>
    private readonly BitReader _framereader;

    /// <summary>Minimum block size declared in the STREAMINFO metadata block.</summary>
    private uint _minBlockSize;

    /// <summary>Maximum block size declared in the STREAMINFO metadata block.</summary>
    private uint _maxBlockSize;

    /// <summary>Minimum frame size (bytes) declared in the STREAMINFO metadata block.</summary>
    private uint _minFrameSize;

    /// <summary>Maximum frame size (bytes) declared in the STREAMINFO metadata block.</summary>
    private uint _maxFrameSize;

    /// <summary>Number of decoded samples currently available in <see cref="Samples"/> (not yet consumed by <see cref="Read"/>).</summary>
    private int _samplesInBuffer;

    /// <summary>Offset into <see cref="Samples"/> where the next unconsumed sample starts.</summary>
    private int _samplesBufferOffset;

    /// <summary>Stream position (in samples) of the data held in <see cref="Samples"/>.</summary>
    private long _sampleOffset;

    /// <summary>Backing input stream — either the user-supplied stream or an internally-opened <see cref="FileStream"/>.</summary>
    private readonly Stream _io;

    /// <summary>
    /// Gets or sets whether CRC verification is performed during decoding.
    /// </summary>
    public bool DoCrc { get; set; } = true;

    /// <summary>
    /// Gets the decoded sample buffer containing the current frame's audio data.
    /// </summary>
    public int[] Samples { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDecoder"/> class from a file path or stream.
    /// </summary>
    /// <param name="settings">Decoder configuration settings.</param>
    /// <param name="path">Path to the FLAC file, or null if reading from a stream.</param>
    /// <param name="io">Input stream. If null and path is provided, a <see cref="FileStream"/> is opened.</param>
    public AudioDecoder(DecoderSettings settings, string? path, Stream? io = null)
    {
        _mSettings = settings;

        if (path != null)
        {
            Path = path;
            _io = io ?? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 0x10000);
        }
        else
        {
            _io = io!;
        }

        _crc8 = new Crc8();

        _framesBuffer = new byte[0x20000];
        decode_metadata();

        _frame = new FlacFrame(PCM!.ChannelCount);
        _framereader = new BitReader();

        //max_frame_size = 16 + ((Flake.MAX_BLOCKSIZE * PCM.BitsPerSample * PCM.ChannelCount + 1) + 7) >> 3);
        if ((((int)_maxFrameSize * PCM.BitsPerSample * PCM.ChannelCount * 2) >> 3) > _framesBuffer.Length)
        {
            var temp = _framesBuffer;
            _framesBuffer = new byte[(((int)_maxFrameSize * PCM.BitsPerSample * PCM.ChannelCount * 2) >> 3)];
            if (_framesBufferLength > 0)
                Array.Copy(temp, _framesBufferOffset, _framesBuffer, 0, _framesBufferLength);
            _framesBufferOffset = 0;
        }

        _samplesInBuffer = 0;

        if (PCM.BitsPerSample != 16 && PCM.BitsPerSample != 24)
            throw new InvalidDataException("invalid flac file");

        Samples = new int[FlakeConstants.MAXBLOCKSIZE * PCM.ChannelCount];
        _residualBuffer = new int[FlakeConstants.MAXBLOCKSIZE * PCM.ChannelCount];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDecoder"/> class with a given PCM configuration.
    /// </summary>
    /// <param name="pcm">PCM audio configuration specifying sample rate, bit depth, and channel count.</param>
    public AudioDecoder(AudioPcmConfig pcm)
    {
        _framesBuffer = null!;
        _io = null!;
        _mSettings = null!;
        PCM = pcm;
        _crc8 = new Crc8();

        Samples = new int[FlakeConstants.MAXBLOCKSIZE * PCM.ChannelCount];
        _residualBuffer = new int[FlakeConstants.MAXBLOCKSIZE * PCM.ChannelCount];
        _frame = new FlacFrame(PCM.ChannelCount);
        _framereader = new BitReader();
    }

    private readonly DecoderSettings _mSettings;

    /// <summary>
    /// Gets the decoder settings used by this instance.
    /// </summary>
    public IAudioDecoderSettings Settings => _mSettings;

    /// <summary>
    /// Closes the underlying input stream.
    /// </summary>
    public void Close()
    {
        _io.Close();
    }

    /// <summary>
    /// Gets the total duration of the audio stream.
    /// </summary>
    public TimeSpan Duration => Length < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Length / PCM.SampleRate);

    /// <summary>
    /// Gets the total number of samples in the stream.
    /// </summary>
    public long Length { get; private set; }

    /// <summary>
    /// Gets the number of samples remaining from the current position to the end of the stream.
    /// </summary>
    public long Remaining => Length - Position;

    /// <summary>
    /// Gets or sets the current sample position within the stream. Setting the position seeks using the seek table if available.
    /// </summary>
    public long Position
    {
        get => _sampleOffset - _samplesInBuffer;
        set
        {
            if (value > Length)
                throw new ArgumentOutOfRangeException(nameof(value), "seeking past end of stream");

            if (value < Position || value > _sampleOffset)
            {
                if (_seekTable != null && _io.CanSeek)
                {
                    var bestSt = -1;
                    for (var st = 0; st < _seekTable.Length; st++)
                    {
                        if (_seekTable[st].number <= value &&
                            (bestSt == -1 || _seekTable[st].number > _seekTable[bestSt].number))
                        {
                            bestSt = st;
                        }
                    }

                    if (bestSt != -1)
                    {
                        _framesBufferLength = 0;
                        _samplesInBuffer = 0;
                        _samplesBufferOffset = 0;
                        _io.Position = (long)_seekTable[bestSt].offset + _firstFrameOffset;
                        _sampleOffset = _seekTable[bestSt].number;
                    }
                }

                if (value < Position)
                    throw new InvalidOperationException("cannot seek backwards without seek table");
            }

            while (value > _sampleOffset)
            {
                _samplesInBuffer = 0;
                _samplesBufferOffset = 0;

                fill_frames_buffer();
                if (_framesBufferLength == 0)
                    throw new InvalidOperationException("seek failed");

                var bytesDecoded = DecodeFrame(_framesBuffer, _framesBufferOffset, _framesBufferLength);
                _framesBufferLength -= bytesDecoded;
                _framesBufferOffset += bytesDecoded;

                _sampleOffset += _samplesInBuffer;
            }

            var diff = _samplesInBuffer - (int)(_sampleOffset - value);
            _samplesInBuffer -= diff;
            _samplesBufferOffset += diff;
        }
    }

    /// <summary>
    /// Gets the PCM audio configuration for this stream.
    /// </summary>
    public AudioPcmConfig PCM { get; private set; }

    /// <summary>
    /// Gets the file path of the FLAC source, or null if reading from a stream.
    /// </summary>
    public string Path { get; } = null!;

    /// <summary>
    /// Interleaves decoded samples from <see cref="Samples"/> into the target <see cref="AudioBuffer"/>.
    /// For stereo streams, each channel's samples are interleaved (L,R,L,R,…).
    /// For other channel counts, samples are written channel-by-channel.
    /// </summary>
    /// <param name="buff">Destination audio buffer.</param>
    /// <param name="offset">Starting sample offset within <paramref name="buff"/>.</param>
    /// <param name="count">Number of samples to write per channel.</param>
    private unsafe void Interlace(AudioBuffer buff, int offset, int count)
    {
        if (PCM.ChannelCount == 2)
        {
            fixed (int* src = &Samples[_samplesBufferOffset])
            {
                buff.Interlace(offset, src, src + FlakeConstants.MAXBLOCKSIZE, count);
            }
        }
        else
        {
            for (var ch = 0; ch < PCM.ChannelCount; ch++)
                fixed (int* res = &buff.Samples[offset, ch], src = &Samples[_samplesBufferOffset + ch * FlakeConstants.MAXBLOCKSIZE])
                {
                    var psrc = src;
                    for (var i = 0; i < count; i++)
                    {
                        res[i * PCM.ChannelCount] = *(psrc++);
                    }
                }
        }
    }

    /// <summary>
    /// Reads audio samples into the specified buffer, decoding FLAC frames as needed.
    /// </summary>
    /// <param name="buffer">The audio buffer to fill with decoded samples.</param>
    /// <param name="maxLength">The maximum number of samples to read.</param>
    /// <returns>The actual number of samples read.</returns>
    public int Read(AudioBuffer buffer, int maxLength)
    {
        buffer.Prepare(this, maxLength);

        var offset = 0;
        var sampleCount = buffer.Length;

        while (_samplesInBuffer < sampleCount)
        {
            if (_samplesInBuffer > 0)
            {
                Interlace(buffer, offset, _samplesInBuffer);
                sampleCount -= _samplesInBuffer;
                offset += _samplesInBuffer;
                _samplesInBuffer = 0;
                _samplesBufferOffset = 0;
            }

            fill_frames_buffer();

            if (_framesBufferLength == 0)
                return buffer.Length = offset;

            var bytesDecoded = DecodeFrame(_framesBuffer, _framesBufferOffset, _framesBufferLength);
            _framesBufferLength -= bytesDecoded;
            _framesBufferOffset += bytesDecoded;

            _samplesInBuffer -= _samplesBufferOffset; // can be set by Seek, otherwise zero
            _sampleOffset += _samplesInBuffer;
        }

        Interlace(buffer, offset, sampleCount);
        _samplesInBuffer -= sampleCount;
        _samplesBufferOffset += sampleCount;
        if (_samplesInBuffer == 0)
        {
            _samplesBufferOffset = 0;
        }

        return buffer.Length = offset + sampleCount;
    }

    /// <summary>
    /// Ensures the internal frame buffer holds at least half its capacity by reading more data from the input stream.
    /// When the unread portion crosses the buffer's halfway point the remaining data is compacted to the start.
    /// </summary>
    private unsafe void fill_frames_buffer()
    {
        if (_framesBufferLength == 0)
        {
            _framesBufferOffset = 0;
        }
        else if (_framesBufferLength < _framesBuffer.Length / 2 && _framesBufferOffset >= _framesBuffer.Length / 2)
        {
            fixed (byte* buff = _framesBuffer)
            {
                AudioSamples.MemCpy(buff, buff + _framesBufferOffset, _framesBufferLength);
            }

            _framesBufferOffset = 0;
        }

        while (_framesBufferLength < _framesBuffer.Length / 2)
        {
            var read = _io.Read(_framesBuffer, _framesBufferOffset + _framesBufferLength, _framesBuffer.Length - _framesBufferOffset - _framesBufferLength);
            _framesBufferLength += read;
            if (read == 0)
                break;
        }
    }

    /// <summary>
    /// Decodes the FLAC frame header: sync code, block size, sample rate, channel mode, bit-depth, and frame/CRC check.
    /// Populates <paramref name="frame"/> with the parsed values.
    /// </summary>
    /// <param name="bitreader">Bit reader positioned at the start of the frame header.</param>
    /// <param name="frame">Target <see cref="FlacFrame"/> to populate.</param>
    private unsafe void decode_frame_header(BitReader bitreader, FlacFrame frame)
    {
        var headerStart = bitreader.Position;

        if (bitreader.Readbits(15) != 0x7FFC)
            throw new InvalidDataException("invalid frame");

        var vbs = bitreader.Readbit();
        frame.bs_code0 = (int)bitreader.Readbits(4);
        var srCode0 = bitreader.Readbits(4);
        frame.ch_mode = (ChannelMode)bitreader.Readbits(4);
        var bpsCode = bitreader.Readbits(3);
        if (FlakeConstants.flacBitdepths[bpsCode] != PCM.BitsPerSample)
            throw new NotSupportedException("unsupported bps coding");

        var t1 = bitreader.Readbit(); // == 0?????
        if (t1 != 0)
            throw new NotSupportedException("unsupported frame coding");

        frame.frame_number = (int)bitreader.ReadUtf8();

        switch (frame.bs_code0)
        {
            // custom block size
            case 6:
                frame.bs_code1 = (int)bitreader.Readbits(8);
                frame.blocksize = frame.bs_code1 + 1;
                break;
            case 7:
                frame.bs_code1 = (int)bitreader.Readbits(16);
                frame.blocksize = frame.bs_code1 + 1;
                break;
            default:
                frame.blocksize = FlakeConstants.flacBlocksizes[frame.bs_code0];
                break;
        }

        // custom sample rate
        if (srCode0 is < 1 or > 11)
        {
            // sr_code0 == 12 -> sr == bitreader.readbits(8) * 1000;
            // sr_code0 == 13 -> sr == bitreader.readbits(16);
            // sr_code0 == 14 -> sr == bitreader.readbits(16) * 10;
            throw new NotSupportedException("invalid sample rate mode");
        }

        var frameChannels = (int)frame.ch_mode + 1;
        switch (frameChannels)
        {
            case > 11:
                throw new InvalidDataException("invalid channel mode");
            // Mid/Left/Right Side Stereo
            case 2 or > 8:
                frameChannels = 2;
                break;
            default:
                frame.ch_mode = ChannelMode.NotStereo;
                break;
        }

        if (frameChannels != PCM.ChannelCount)
            throw new InvalidDataException("invalid channel mode");

        // CRC-8 of frame header
        var crc = DoCrc ? _crc8.ComputeChecksum(bitreader.Buffer, headerStart, bitreader.Position - headerStart) : (byte)0;
        frame.crc8 = (byte)bitreader.Readbits(8);
        if (DoCrc && frame.crc8 != crc)
            throw new InvalidDataException("header crc mismatch");
    }

    /// <summary>
    /// Decodes a constant subframe: all samples in the block share the same value read from the bitstream.
    /// </summary>
    /// <param name="bitreader">Bit reader positioned at the subframe data.</param>
    /// <param name="frame">The current frame being decoded.</param>
    /// <param name="ch">Zero-based channel index.</param>
    private unsafe void decode_subframe_constant(BitReader bitreader, FlacFrame frame, int ch)
    {
        var obits = frame.subframes[ch].Obits;
        frame.subframes[ch].Best.residual[0] = bitreader.ReadbitsSigned(obits);
    }

    /// <summary>
    /// Decodes a verbatim (uncompressed) subframe by reading raw signed samples directly from the bitstream.
    /// </summary>
    /// <param name="bitreader">Bit reader positioned at the subframe data.</param>
    /// <param name="frame">The current frame being decoded.</param>
    /// <param name="ch">Zero-based channel index.</param>
    private unsafe void decode_subframe_verbatim(BitReader bitreader, FlacFrame frame, int ch)
    {
        var obits = frame.subframes[ch].Obits;
        for (var i = 0; i < frame.blocksize; i++)
        {
            frame.subframes[ch].Best.residual[i] = bitreader.ReadbitsSigned(obits);
        }
    }

    /// <summary>
    /// Decodes the residual (Rice-coded error signal) portion of a subframe after prediction coefficients have been read.
    /// Handles both partition order 0 (Rice coding) and higher partition orders with configurable Rice parameters.
    /// </summary>
    /// <param name="bitreader">Bit reader positioned at the residual data.</param>
    /// <param name="frame">The current frame being decoded.</param>
    /// <param name="ch">Zero-based channel index.</param>
    private static unsafe void decode_residual(BitReader bitreader, FlacFrame frame, int ch)
    {
        // rice-encoded block
        // coding method
        frame.subframes[ch].Best.rc.CodingMethod = (int)bitreader.Readbits(2); // ????? == 0
        if (frame.subframes[ch].Best.rc.CodingMethod != 0 && frame.subframes[ch].Best.rc.CodingMethod != 1)
            throw new NotSupportedException("unsupported residual coding");
        // partition order
        frame.subframes[ch].Best.rc.Porder = (int)bitreader.Readbits(4);
        if (frame.subframes[ch].Best.rc.Porder > 8)
            throw new InvalidDataException("invalid partition order");

        var psize = frame.blocksize >> frame.subframes[ch].Best.rc.Porder;
        var res_cnt = psize - frame.subframes[ch].Best.order;

        var rice_len = 4 + frame.subframes[ch].Best.rc.CodingMethod;
        // residual
        var j = frame.subframes[ch].Best.order;
        var r = frame.subframes[ch].Best.residual + j;
        for (var p = 0; p < (1 << frame.subframes[ch].Best.rc.Porder); p++)
        {
            if (p == 1)
            {
                res_cnt = psize;
            }

            var n = Math.Min(res_cnt, frame.blocksize - j);

            var k = frame.subframes[ch].Best.rc.Rparams[p] = (int)bitreader.Readbits(rice_len);
            if (k == (1 << rice_len) - 1)
            {
                k = frame.subframes[ch].Best.rc.EscBps[p] = (int)bitreader.Readbits(5);
                for (var i = n; i > 0; i--)
                {
                    *(r++) = bitreader.ReadbitsSigned((int)k);
                }
            }
            else
            {
                bitreader.ReadRiceBlock(n, (int)k, r);
                r += n;
            }

            j += n;
        }
    }

    /// <summary>
    /// Decodes a Fixed-prediction subframe. Reads warm-up samples (equal to the prediction order) then the Rice-coded residual,
    /// which is later combined by <see cref="restore_samples_fixed"/> to reconstruct the original samples.
    /// </summary>
    /// <param name="bitreader">Bit reader positioned at the subframe data.</param>
    /// <param name="frame">The current frame being decoded.</param>
    /// <param name="ch">Zero-based channel index.</param>
    private unsafe void decode_subframe_fixed(BitReader bitreader, FlacFrame frame, int ch)
    {
        // warm-up samples
        var obits = frame.subframes[ch].Obits;
        for (var i = 0; i < frame.subframes[ch].Best.order; i++)
        {
            frame.subframes[ch].Best.residual[i] = bitreader.ReadbitsSigned(obits);
        }

        // residual
        decode_residual(bitreader, frame, ch);
    }

    /// <summary>
    /// Decodes an LPC (Linear Predictive Coding) subframe. Reads warm-up samples, quantised LPC coefficients,
    /// and the Rice-coded residual. Actual sample reconstruction is deferred to <see cref="restore_samples_lpc"/>.
    /// </summary>
    /// <param name="bitreader">Bit reader positioned at the subframe data.</param>
    /// <param name="frame">The current frame being decoded.</param>
    /// <param name="ch">Zero-based channel index.</param>
    private unsafe void decode_subframe_lpc(BitReader bitreader, FlacFrame frame, int ch)
    {
        // warm-up samples
        var obits = frame.subframes[ch].Obits;
        for (var i = 0; i < frame.subframes[ch].Best.order; i++)
        {
            frame.subframes[ch].Best.residual[i] = bitreader.ReadbitsSigned(obits);
        }

        // LPC coefficients
        frame.subframes[ch].Best.cbits = (int)bitreader.Readbits(4) + 1; // lpc_precision
        if (frame.subframes[ch].Best.cbits >= 16)
            throw new InvalidDataException("cbits >= 16");

        frame.subframes[ch].Best.shift = bitreader.ReadbitsSigned(5);
        if (frame.subframes[ch].Best.shift < 0)
            throw new InvalidDataException("negative shift");

        for (var i = 0; i < frame.subframes[ch].Best.order; i++)
        {
            frame.subframes[ch].Best.coefs[i] = bitreader.ReadbitsSigned(frame.subframes[ch].Best.cbits);
        }

        // residual
        decode_residual(bitreader, frame, ch);
    }

    private unsafe void decode_subframes(BitReader bitreader, FlacFrame frame)
    {
        fixed (int* r = _residualBuffer, s = Samples)
        {
            for (var ch = 0; ch < PCM.ChannelCount; ch++)
            {
                // subframe header
                var t1 = bitreader.Readbit(); // ?????? == 0
                if (t1 != 0)
                    throw new NotSupportedException("unsupported subframe coding (ch == " + ch + ")");

                var type_code = (int)bitreader.Readbits(6);
                frame.subframes[ch].Wbits = (int)bitreader.Readbit();
                if (frame.subframes[ch].Wbits != 0)
                {
                    frame.subframes[ch].Wbits += (int)bitreader.ReadUnary();
                }

                frame.subframes[ch].Obits = PCM.BitsPerSample - frame.subframes[ch].Wbits;
                switch (frame.ch_mode)
                {
                    case ChannelMode.MidSide:
                    case ChannelMode.LeftSide: frame.subframes[ch].Obits += ch; break;
                    case ChannelMode.RightSide: frame.subframes[ch].Obits += 1 - ch; break;
                }

                frame.subframes[ch].Best.type = (SubframeType)type_code;
                frame.subframes[ch].Best.order = 0;

                if ((type_code & (uint)SubframeType.LPC) != 0)
                {
                    frame.subframes[ch].Best.order = (type_code - (int)SubframeType.LPC) + 1;
                    frame.subframes[ch].Best.type = SubframeType.LPC;
                }
                else if ((type_code & (uint)SubframeType.Fixed) != 0)
                {
                    frame.subframes[ch].Best.order = (type_code - (int)SubframeType.Fixed);
                    frame.subframes[ch].Best.type = SubframeType.Fixed;
                }

                frame.subframes[ch].Best.residual = r + ch * FlakeConstants.MAXBLOCKSIZE;
                frame.subframes[ch].Samples = s + ch * FlakeConstants.MAXBLOCKSIZE;

                // subframe
                switch (frame.subframes[ch].Best.type)
                {
                    case SubframeType.Constant:
                        decode_subframe_constant(bitreader, frame, ch);
                        break;
                    case SubframeType.Verbatim:
                        decode_subframe_verbatim(bitreader, frame, ch);
                        break;
                    case SubframeType.Fixed:
                        decode_subframe_fixed(bitreader, frame, ch);
                        break;
                    case SubframeType.LPC:
                        decode_subframe_lpc(bitreader, frame, ch);
                        break;
                    default:
                        throw new InvalidDataException("invalid subframe type");
                }
            }
        }
    }

    private unsafe void restore_samples_fixed(FlacFrame frame, int ch)
    {
        var sub = frame.subframes[ch];

        AudioSamples.MemCpy(sub.Samples, sub.Best.residual, sub.Best.order);
        var data = sub.Samples + sub.Best.order;
        var residual = sub.Best.residual + sub.Best.order;
        var data_len = frame.blocksize - sub.Best.order;
        int s0, s1, s2;
        switch (sub.Best.order)
        {
            case 0:
                AudioSamples.MemCpy(data, residual, data_len);
                break;
            case 1:
                s1 = data[-1];
                for (var i = data_len; i > 0; i--)
                {
                    s1 += *(residual++);
                    *(data++) = s1;
                }

                //data[i] = residual[i] + data[i - 1];
                break;
            case 2:
                s2 = data[-2];
                s1 = data[-1];
                for (var i = data_len; i > 0; i--)
                {
                    s0 = *(residual++) + (s1 << 1) - s2;
                    *(data++) = s0;
                    s2 = s1;
                    s1 = s0;
                }

                //data[i] = residual[i] + data[i - 1] * 2  - data[i - 2];
                break;
            case 3:
                for (var i = 0; i < data_len; i++)
                {
                    data[i] = residual[i] + (((data[i - 1] - data[i - 2]) << 1) + (data[i - 1] - data[i - 2])) + data[i - 3];
                }

                break;
            case 4:
                for (var i = 0; i < data_len; i++)
                {
                    data[i] = residual[i] + ((data[i - 1] + data[i - 3]) << 2) - ((data[i - 2] << 2) + (data[i - 2] << 1)) - data[i - 4];
                }

                break;
        }
    }

    /// <summary>
    /// Reconstructs the original PCM samples from the LPC residual and coefficients for one channel.
    /// Uses 32-bit or 64-bit arithmetic depending on whether the coefficient sum would overflow 32 bits.
    /// </summary>
    /// <param name="frame">The current frame being decoded.</param>
    /// <param name="ch">Zero-based channel index.</param>
    private unsafe void restore_samples_lpc(FlacFrame frame, int ch)
    {
        var sub = frame.subframes[ch];
        ulong csum = 0;
        fixed (int* coefs = sub.Best.coefs)
        {
            for (var i = sub.Best.order; i > 0; i--)
            {
                csum += (ulong)Math.Abs(coefs[i - 1]);
            }

            if ((csum << sub.Obits) >= 1UL << 32)
                Lpc.decodeResidualLong(sub.Best.residual, sub.Samples, frame.blocksize, sub.Best.order, coefs, sub.Best.shift);
            else
                Lpc.decodeResidual(sub.Best.residual, sub.Samples, frame.blocksize, sub.Best.order, coefs, sub.Best.shift);
        }
    }

    /// <summary>
    /// Reconstructs PCM samples for all channels from the decoded subframe residuals and prediction coefficients.
    /// Handles constant, verbatim, fixed, and LPC subframe types, applies wasted-bits left shift, and performs
    /// stereo de-correlation (Mid/Side, Left/Side, Right/Side) when the frame uses joint stereo coding.
    /// </summary>
    /// <param name="frame">The current frame with fully decoded subframe data.</param>
    private unsafe void restore_samples(FlacFrame frame)
    {
        for (var ch = 0; ch < PCM.ChannelCount; ch++)
        {
            switch (frame.subframes[ch].Best.type)
            {
                case SubframeType.Constant:
                    AudioSamples.MemSet(frame.subframes[ch].Samples, frame.subframes[ch].Best.residual[0], frame.blocksize);
                    break;
                case SubframeType.Verbatim:
                    AudioSamples.MemCpy(frame.subframes[ch].Samples, frame.subframes[ch].Best.residual, frame.blocksize);
                    break;
                case SubframeType.Fixed:
                    restore_samples_fixed(frame, ch);
                    break;
                case SubframeType.LPC:
                    restore_samples_lpc(frame, ch);
                    break;
            }

            if (frame.subframes[ch].Wbits != 0)
            {
                var s = frame.subframes[ch].Samples;
                var x = (int)frame.subframes[ch].Wbits;
                for (var i = frame.blocksize; i > 0; i--)
                {
                    *(s++) <<= x;
                }
            }
        }

        if (frame.ch_mode != ChannelMode.NotStereo)
        {
            var l = frame.subframes[0].Samples;
            var r = frame.subframes[1].Samples;
            switch (frame.ch_mode)
            {
                case ChannelMode.LeftRight:
                    break;
                case ChannelMode.MidSide:
                    for (var i = frame.blocksize; i > 0; i--)
                    {
                        var mid = *l;
                        var side = *r;
                        mid <<= 1;
                        mid |= (side & 1); /* i.e. if 'side' is odd... */
                        *(l++) = (mid + side) >> 1;
                        *(r++) = (mid - side) >> 1;
                    }

                    break;
                case ChannelMode.LeftSide:
                    for (var i = frame.blocksize; i > 0; i--)
                    {
                        int _l = *(l++), _r = *r;
                        *(r++) = _l - _r;
                    }

                    break;
                case ChannelMode.RightSide:
                    for (var i = frame.blocksize; i > 0; i--)
                    {
                        *(l++) += *(r++);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Decodes a single FLAC frame from the provided buffer.
    /// </summary>
    /// <param name="buffer">Byte array containing the encoded frame data.</param>
    /// <param name="pos">Starting position within the buffer.</param>
    /// <param name="len">Length of data available in the buffer.</param>
    /// <returns>The number of bytes consumed from the buffer.</returns>
    public unsafe int DecodeFrame(byte[] buffer, int pos, int len)
    {
        fixed (byte* buf = buffer)
        {
            _framereader.Reset(buf, pos, len);
            decode_frame_header(_framereader, _frame);
            decode_subframes(_framereader, _frame);
            _framereader.Flush();
            var crc_1 = _framereader.GetCrc16();
            var crc_2 = _framereader.ReadUshort();
            if (DoCrc && crc_1 != crc_2)
                throw new InvalidDataException("frame crc mismatch");

            restore_samples(_frame);
            _samplesInBuffer = _frame.blocksize;
            return _framereader.Position - pos;
        }
    }


    private bool skip_bytes(int bytes)
    {
        for (var j = 0; j < bytes; j++)
            if (0 == _io.Read(_framesBuffer, 0, 1))
                return false;

        return true;
    }

    private unsafe void decode_metadata()
    {
        byte x;
        int i, id;
        //bool first = true;
        var flacStreamSyncString = "fLaC"u8.ToArray();
        var id3V2Tag = "ID3"u8.ToArray();

        for (i = id = 0; i < 4;)
        {
            if (_io.Read(_framesBuffer, 0, 1) == 0)
                throw new InvalidDataException("FLAC stream not found");

            x = _framesBuffer[0];
            if (x == flacStreamSyncString[i])
            {
                //first = true;
                i++;
                id = 0;
                continue;
            }

            if (id < 3 && x == id3V2Tag[id])
            {
                id++;
                i = 0;
                if (id == 3)
                {
                    if (!skip_bytes(3))
                        throw new InvalidDataException("FLAC stream not found");

                    var skip = 0;
                    for (var j = 0; j < 4; j++)
                    {
                        if (0 == _io.Read(_framesBuffer, 0, 1))
                            throw new InvalidDataException("FLAC stream not found");

                        skip <<= 7;
                        skip |= ((int)_framesBuffer[0] & 0x7f);
                    }

                    if (!skip_bytes(skip))
                        throw new InvalidDataException("FLAC stream not found");
                }

                continue;
            }

            id = 0;
            if (x == 0xff) /* MAGIC NUMBER for the first 8 frame sync bits */
            {
                do
                {
                    if (_io.Read(_framesBuffer, 0, 1) == 0)
                        throw new InvalidDataException("FLAC stream not found");

                    x = _framesBuffer[0];
                } while (x == 0xff);

                if (x >> 2 == 0x3e) /* MAGIC NUMBER for the last 6 sync bits */
                {
                    //_IO.Position -= 2;
                    // state = frame
                    throw new NotSupportedException("headerless file unsupported");
                }
            }

            throw new InvalidDataException("FLAC stream not found");
        }

        do
        {
            fill_frames_buffer();
            fixed (byte* buf = _framesBuffer)
            {
                var bitreader = new BitReader(buf, _framesBufferOffset, _framesBufferLength - _framesBufferOffset);
                var isLast = bitreader.Readbit() != 0;
                var type = (MetadataType)bitreader.Readbits(7);
                var len = (int)bitreader.Readbits(24);

                switch (type)
                {
                    case MetadataType.StreamInfo:
                    {
                        const int flacStreamMetadataStreaminfoMinBlockSizeLen = 16; /* bits */
                        const int flacStreamMetadataStreaminfoMaxBlockSizeLen = 16; /* bits */
                        const int flacStreamMetadataStreaminfoMinFrameSizeLen = 24; /* bits */
                        const int flacStreamMetadataStreaminfoMaxFrameSizeLen = 24; /* bits */
                        const int flacStreamMetadataStreaminfoSampleRateLen = 20; /* bits */
                        const int flacStreamMetadataStreaminfoChannelsLen = 3; /* bits */
                        const int flacStreamMetadataStreaminfoBitsPerSampleLen = 5; /* bits */
                        const int flacStreamMetadataStreaminfoTotalSamplesLen = 36; /* bits */
                        const int flacStreamMetadataStreaminfoMd5SumLen = 128; /* bits */

                        _minBlockSize = bitreader.Readbits(flacStreamMetadataStreaminfoMinBlockSizeLen);
                        _maxBlockSize = bitreader.Readbits(flacStreamMetadataStreaminfoMaxBlockSizeLen);
                        _minFrameSize = bitreader.Readbits(flacStreamMetadataStreaminfoMinFrameSizeLen);
                        _maxFrameSize = bitreader.Readbits(flacStreamMetadataStreaminfoMaxFrameSizeLen);
                        var sampleRate = (int)bitreader.Readbits(flacStreamMetadataStreaminfoSampleRateLen);
                        var channels = 1 + (int)bitreader.Readbits(flacStreamMetadataStreaminfoChannelsLen);
                        var bitsPerSample = 1 + (int)bitreader.Readbits(flacStreamMetadataStreaminfoBitsPerSampleLen);
                        PCM = new AudioPcmConfig(bitsPerSample, channels, sampleRate);
                        Length = (long)bitreader.Readbits64(flacStreamMetadataStreaminfoTotalSamplesLen);
                        bitreader.Skipbits(flacStreamMetadataStreaminfoMd5SumLen);
                        break;
                    }
                    case MetadataType.Seektable:
                    {
                        var numEntries = len / 18;
                        _seekTable = new SeekPoint[numEntries];
                        for (var e = 0; e < numEntries; e++)
                        {
                            _seekTable[e].number = bitreader.ReadLong();
                            _seekTable[e].offset = bitreader.ReadLong();
                            _seekTable[e].framesize = (int)bitreader.ReadUshort();
                        }

                        break;
                    }
                }

                if (_framesBufferLength < 4 + len)
                {
                    _io.Position += 4 + len - _framesBufferLength;
                    _framesBufferLength = 0;
                }
                else
                {
                    _framesBufferLength -= 4 + len;
                    _framesBufferOffset += 4 + len;
                }
                if (isLast)
                    break;
            }
        } while (true);
        _firstFrameOffset = _io.Position - _framesBufferLength;
    }
}