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
    private readonly int[] _residualBuffer;

    private readonly byte[] _framesBuffer;
    private int _framesBufferLength, _framesBufferOffset;
    private long _firstFrameOffset;

    private SeekPoint[] _seekTable;

    private readonly Crc8 _crc8;
    private readonly FlacFrame _frame;
    private readonly BitReader _framereader;

    private uint _minBlockSize;
    private uint _maxBlockSize;
    private uint _minFrameSize;
    private uint _maxFrameSize;

    private int _samplesInBuffer, _samplesBufferOffset;
    private long _sampleOffset;

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
    public AudioDecoder(DecoderSettings settings, string path, Stream io = null)
    {
        _mSettings = settings;

        if (path != null)
        {
            Path = path;
            _io = io != null ? io : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 0x10000);
        }
        else
        {
            _io = io;
        }

        _crc8 = new Crc8();

        _framesBuffer = new byte[0x20000];
        decode_metadata();

        _frame = new FlacFrame(PCM.ChannelCount);
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
            throw new Exception("invalid flac file");

        Samples = new int[FlakeConstants.MAXBLOCKSIZE * PCM.ChannelCount];
        _residualBuffer = new int[FlakeConstants.MAXBLOCKSIZE * PCM.ChannelCount];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDecoder"/> class with a given PCM configuration.
    /// </summary>
    /// <param name="pcm">PCM audio configuration specifying sample rate, bit depth, and channel count.</param>
    public AudioDecoder(AudioPcmConfig pcm)
    {
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
                throw new Exception("seeking past end of stream");

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
                    throw new Exception("cannot seek backwards without seek table");
            }

            while (value > _sampleOffset)
            {
                _samplesInBuffer = 0;
                _samplesBufferOffset = 0;

                fill_frames_buffer();
                if (_framesBufferLength == 0)
                    throw new Exception("seek failed");

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
    public string Path { get; }

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

    private unsafe void decode_frame_header(BitReader bitreader, FlacFrame frame)
    {
        var headerStart = bitreader.Position;

        if (bitreader.Readbits(15) != 0x7FFC)
            throw new Exception("invalid frame");

        var vbs = bitreader.Readbit();
        frame.bs_code0 = (int)bitreader.Readbits(4);
        var srCode0 = bitreader.Readbits(4);
        frame.ch_mode = (ChannelMode)bitreader.Readbits(4);
        var bpsCode = bitreader.Readbits(3);
        if (FlakeConstants.flacBitdepths[bpsCode] != PCM.BitsPerSample)
            throw new Exception("unsupported bps coding");

        var t1 = bitreader.Readbit(); // == 0?????
        if (t1 != 0)
            throw new Exception("unsupported frame coding");

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
            throw new Exception("invalid sample rate mode");
        }

        var frameChannels = (int)frame.ch_mode + 1;
        switch (frameChannels)
        {
            case > 11:
                throw new Exception("invalid channel mode");
            // Mid/Left/Right Side Stereo
            case 2 or > 8:
                frameChannels = 2;
                break;
            default:
                frame.ch_mode = ChannelMode.NotStereo;
                break;
        }

        if (frameChannels != PCM.ChannelCount)
            throw new Exception("invalid channel mode");

        // CRC-8 of frame header
        var crc = DoCrc ? _crc8.ComputeChecksum(bitreader.Buffer, headerStart, bitreader.Position - headerStart) : (byte)0;
        frame.crc8 = (byte)bitreader.Readbits(8);
        if (DoCrc && frame.crc8 != crc)
            throw new Exception("header crc mismatch");
    }

    private unsafe void decode_subframe_constant(BitReader bitreader, FlacFrame frame, int ch)
    {
        var obits = frame.subframes[ch].obits;
        frame.subframes[ch].best.residual[0] = bitreader.ReadbitsSigned(obits);
    }

    private unsafe void decode_subframe_verbatim(BitReader bitreader, FlacFrame frame, int ch)
    {
        var obits = frame.subframes[ch].obits;
        for (var i = 0; i < frame.blocksize; i++)
        {
            frame.subframes[ch].best.residual[i] = bitreader.ReadbitsSigned(obits);
        }
    }

    private static unsafe void decode_residual(BitReader bitreader, FlacFrame frame, int ch)
    {
        // rice-encoded block
        // coding method
        frame.subframes[ch].best.rc.coding_method = (int)bitreader.Readbits(2); // ????? == 0
        if (frame.subframes[ch].best.rc.coding_method != 0 && frame.subframes[ch].best.rc.coding_method != 1)
            throw new Exception("unsupported residual coding");
        // partition order
        frame.subframes[ch].best.rc.porder = (int)bitreader.Readbits(4);
        if (frame.subframes[ch].best.rc.porder > 8)
            throw new Exception("invalid partition order");

        var psize = frame.blocksize >> frame.subframes[ch].best.rc.porder;
        var res_cnt = psize - frame.subframes[ch].best.order;

        var rice_len = 4 + frame.subframes[ch].best.rc.coding_method;
        // residual
        var j = frame.subframes[ch].best.order;
        var r = frame.subframes[ch].best.residual + j;
        for (var p = 0; p < (1 << frame.subframes[ch].best.rc.porder); p++)
        {
            if (p == 1)
            {
                res_cnt = psize;
            }

            var n = Math.Min(res_cnt, frame.blocksize - j);

            var k = frame.subframes[ch].best.rc.rparams[p] = (int)bitreader.Readbits(rice_len);
            if (k == (1 << rice_len) - 1)
            {
                k = frame.subframes[ch].best.rc.esc_bps[p] = (int)bitreader.Readbits(5);
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

    private unsafe void decode_subframe_fixed(BitReader bitreader, FlacFrame frame, int ch)
    {
        // warm-up samples
        var obits = frame.subframes[ch].obits;
        for (var i = 0; i < frame.subframes[ch].best.order; i++)
        {
            frame.subframes[ch].best.residual[i] = bitreader.ReadbitsSigned(obits);
        }

        // residual
        decode_residual(bitreader, frame, ch);
    }

    private unsafe void decode_subframe_lpc(BitReader bitreader, FlacFrame frame, int ch)
    {
        // warm-up samples
        var obits = frame.subframes[ch].obits;
        for (var i = 0; i < frame.subframes[ch].best.order; i++)
        {
            frame.subframes[ch].best.residual[i] = bitreader.ReadbitsSigned(obits);
        }

        // LPC coefficients
        frame.subframes[ch].best.cbits = (int)bitreader.Readbits(4) + 1; // lpc_precision
        if (frame.subframes[ch].best.cbits >= 16)
            throw new Exception("cbits >= 16");

        frame.subframes[ch].best.shift = bitreader.ReadbitsSigned(5);
        if (frame.subframes[ch].best.shift < 0)
            throw new Exception("negative shift");

        for (var i = 0; i < frame.subframes[ch].best.order; i++)
        {
            frame.subframes[ch].best.coefs[i] = bitreader.ReadbitsSigned(frame.subframes[ch].best.cbits);
        }

        // residual
        decode_residual(bitreader, frame, ch);
    }

    private unsafe void decode_subframes(BitReader bitreader, FlacFrame frame)
    {
        fixed (int *r = _residualBuffer, s = Samples)
        {
            for (var ch = 0; ch < PCM.ChannelCount; ch++)
            {
                // subframe header
                var t1 = bitreader.Readbit(); // ?????? == 0
                if (t1 != 0)
                    throw new Exception("unsupported subframe coding (ch == " + ch + ")");

                var type_code = (int)bitreader.Readbits(6);
                frame.subframes[ch].wbits = (int)bitreader.Readbit();
                if (frame.subframes[ch].wbits != 0)
                {
                    frame.subframes[ch].wbits += (int)bitreader.ReadUnary();
                }

                frame.subframes[ch].obits = PCM.BitsPerSample - frame.subframes[ch].wbits;
                switch (frame.ch_mode)
                {
                    case ChannelMode.MidSide: frame.subframes[ch].obits += ch; break;
                    case ChannelMode.LeftSide: frame.subframes[ch].obits += ch; break;
                    case ChannelMode.RightSide: frame.subframes[ch].obits += 1 - ch; break;
                }

                frame.subframes[ch].best.type = (SubframeType)type_code;
                frame.subframes[ch].best.order = 0;

                if ((type_code & (uint)SubframeType.LPC) != 0)
                {
                    frame.subframes[ch].best.order = (type_code - (int)SubframeType.LPC) + 1;
                    frame.subframes[ch].best.type = SubframeType.LPC;
                }
                else if ((type_code & (uint)SubframeType.Fixed) != 0)
                {
                    frame.subframes[ch].best.order = (type_code - (int)SubframeType.Fixed);
                    frame.subframes[ch].best.type = SubframeType.Fixed;
                }

                frame.subframes[ch].best.residual = r + ch * FlakeConstants.MAXBLOCKSIZE;
                frame.subframes[ch].samples = s + ch * FlakeConstants.MAXBLOCKSIZE;

                // subframe
                switch (frame.subframes[ch].best.type)
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
                        throw new Exception("invalid subframe type");
                }
            }
        }
    }

    private unsafe void restore_samples_fixed(FlacFrame frame, int ch)
    {
        var sub = frame.subframes[ch];

        AudioSamples.MemCpy(sub.samples, sub.best.residual, sub.best.order);
        var data = sub.samples + sub.best.order;
        var residual = sub.best.residual + sub.best.order;
        var data_len = frame.blocksize - sub.best.order;
        int s0, s1, s2;
        switch (sub.best.order)
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

    private unsafe void restore_samples_lpc(FlacFrame frame, int ch)
    {
        var sub = frame.subframes[ch];
        ulong csum = 0;
        fixed (int* coefs = sub.best.coefs)
        {
            for (var i = sub.best.order; i > 0; i--)
            {
                csum += (ulong)Math.Abs(coefs[i - 1]);
            }

            if ((csum << sub.obits) >= 1UL << 32)
                Lpc.decodeResidualLong(sub.best.residual, sub.samples, frame.blocksize, sub.best.order, coefs, sub.best.shift);
            else
                Lpc.decodeResidual(sub.best.residual, sub.samples, frame.blocksize, sub.best.order, coefs, sub.best.shift);
        }
    }

    private unsafe void restore_samples(FlacFrame frame)
    {
        for (var ch = 0; ch < PCM.ChannelCount; ch++)
        {
            switch (frame.subframes[ch].best.type)
            {
                case SubframeType.Constant:
                    AudioSamples.MemSet(frame.subframes[ch].samples, frame.subframes[ch].best.residual[0], frame.blocksize);
                    break;
                case SubframeType.Verbatim:
                    AudioSamples.MemCpy(frame.subframes[ch].samples, frame.subframes[ch].best.residual, frame.blocksize);
                    break;
                case SubframeType.Fixed:
                    restore_samples_fixed(frame, ch);
                    break;
                case SubframeType.LPC:
                    restore_samples_lpc(frame, ch);
                    break;
            }
            if (frame.subframes[ch].wbits != 0)
            {
                var s = frame.subframes[ch].samples;
                var x = (int) frame.subframes[ch].wbits;
                for (var i = frame.blocksize; i > 0; i--)
                {
                    *(s++) <<= x;
                }
            }
        }
        if (frame.ch_mode != ChannelMode.NotStereo)
        {
            var l = frame.subframes[0].samples;
            var r = frame.subframes[1].samples;
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
                throw new Exception("frame crc mismatch");

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
        var FLAC__STREAM_SYNC_STRING = new[] { (byte)'f', (byte)'L', (byte)'a', (byte)'C' };
        var ID3V2_TAG_ = new[] { (byte)'I', (byte)'D', (byte)'3' };

        for (i = id = 0; i < 4; )
        {
            if (_io.Read(_framesBuffer, 0, 1) == 0)
                throw new Exception("FLAC stream not found");

            x = _framesBuffer[0];
            if (x == FLAC__STREAM_SYNC_STRING[i])
            {
                //first = true;
                i++;
                id = 0;
                continue;
            }
            if (id < 3 && x == ID3V2_TAG_[id])
            {
                id++;
                i = 0;
                if (id == 3)
                {
                    if (!skip_bytes(3))
                        throw new Exception("FLAC stream not found");

                    var skip = 0;
                    for (var j = 0; j < 4; j++)
                    {
                        if (0 == _io.Read(_framesBuffer, 0, 1))
                            throw new Exception("FLAC stream not found");

                        skip <<= 7;
                        skip |= ((int)_framesBuffer[0] & 0x7f);
                    }
                    if (!skip_bytes(skip))
                        throw new Exception("FLAC stream not found");
                }
                continue;
            }
            id = 0;
            if (x == 0xff) /* MAGIC NUMBER for the first 8 frame sync bits */
            {
                do
                {
                    if (_io.Read(_framesBuffer, 0, 1) == 0)
                        throw new Exception("FLAC stream not found");

                    x = _framesBuffer[0];
                } while (x == 0xff);
                if (x >> 2 == 0x3e) /* MAGIC NUMBER for the last 6 sync bits */
                {
                    //_IO.Position -= 2;
                    // state = frame
                    throw new Exception("headerless file unsupported");
                }
            }
            throw new Exception("FLAC stream not found");
        }

        do
        {
            fill_frames_buffer();
            fixed (byte* buf = _framesBuffer)
            {
                var bitreader = new BitReader(buf, _framesBufferOffset, _framesBufferLength - _framesBufferOffset);
                var is_last = bitreader.Readbit() != 0;
                var type = (MetadataType)bitreader.Readbits(7);
                var len = (int)bitreader.Readbits(24);

                if (type == MetadataType.StreamInfo)
                {
                    const int FLAC__STREAM_METADATA_STREAMINFO_MIN_BLOCK_SIZE_LEN = 16; /* bits */
                    const int FLAC__STREAM_METADATA_STREAMINFO_MAX_BLOCK_SIZE_LEN = 16; /* bits */
                    const int FLAC__STREAM_METADATA_STREAMINFO_MIN_FRAME_SIZE_LEN = 24; /* bits */
                    const int FLAC__STREAM_METADATA_STREAMINFO_MAX_FRAME_SIZE_LEN = 24; /* bits */
                    const int FLAC__STREAM_METADATA_STREAMINFO_SAMPLE_RATE_LEN = 20; /* bits */
                    const int FLAC__STREAM_METADATA_STREAMINFO_CHANNELS_LEN = 3; /* bits */
                    const int FLAC__STREAM_METADATA_STREAMINFO_BITS_PER_SAMPLE_LEN = 5; /* bits */
                    const int FLAC__STREAM_METADATA_STREAMINFO_TOTAL_SAMPLES_LEN = 36; /* bits */
                    const int FLAC__STREAM_METADATA_STREAMINFO_MD5SUM_LEN = 128; /* bits */

                    _minBlockSize = bitreader.Readbits(FLAC__STREAM_METADATA_STREAMINFO_MIN_BLOCK_SIZE_LEN);
                    _maxBlockSize = bitreader.Readbits(FLAC__STREAM_METADATA_STREAMINFO_MAX_BLOCK_SIZE_LEN);
                    _minFrameSize = bitreader.Readbits(FLAC__STREAM_METADATA_STREAMINFO_MIN_FRAME_SIZE_LEN);
                    _maxFrameSize = bitreader.Readbits(FLAC__STREAM_METADATA_STREAMINFO_MAX_FRAME_SIZE_LEN);
                    var sample_rate = (int)bitreader.Readbits(FLAC__STREAM_METADATA_STREAMINFO_SAMPLE_RATE_LEN);
                    var channels = 1 + (int)bitreader.Readbits(FLAC__STREAM_METADATA_STREAMINFO_CHANNELS_LEN);
                    var bits_per_sample = 1 + (int)bitreader.Readbits(FLAC__STREAM_METADATA_STREAMINFO_BITS_PER_SAMPLE_LEN);
                    PCM = new AudioPcmConfig(bits_per_sample, channels, sample_rate);
                    Length = (long)bitreader.Readbits64(FLAC__STREAM_METADATA_STREAMINFO_TOTAL_SAMPLES_LEN);
                    bitreader.Skipbits(FLAC__STREAM_METADATA_STREAMINFO_MD5SUM_LEN);
                }
                else if (type == MetadataType.Seektable)
                {
                    var num_entries = len / 18;
                    _seekTable = new SeekPoint[num_entries];
                    for (var e = 0; e < num_entries; e++)
                    {
                        _seekTable[e].number = bitreader.ReadLong();
                        _seekTable[e].offset = bitreader.ReadLong();
                        _seekTable[e].framesize = (int)bitreader.ReadUshort();
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
                if (is_last)
                    break;
            }
        } while (true);
        _firstFrameOffset = _io.Position - _framesBufferLength;
    }
}