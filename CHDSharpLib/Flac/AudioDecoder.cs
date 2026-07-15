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

    private int _framesBufferLength;

    private int _framesBufferOffset;

    private long _firstFrameOffset;

    private SeekPoint[]? _seekTable;

    private readonly Crc8 _crc8;

    private readonly FlacFrame _frame;

    private readonly BitReader _framereader;

    private uint _minBlockSize;

    private uint _maxBlockSize;

    private uint _minFrameSize;

    private uint _maxFrameSize;

    private int _samplesInBuffer;

    private int _samplesBufferOffset;

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

        _frame = new FlacFrame(Pcm!.ChannelCount);
        _framereader = new BitReader();

        //max_frame_size = 16 + ((Flake.MAX_BLOCKSIZE * PCM.BitsPerSample * PCM.ChannelCount + 1) + 7) >> 3);
        if ((((int)_maxFrameSize * Pcm.BitsPerSample * Pcm.ChannelCount * 2) >> 3) > _framesBuffer.Length)
        {
            var temp = _framesBuffer;
            _framesBuffer = new byte[(((int)_maxFrameSize * Pcm.BitsPerSample * Pcm.ChannelCount * 2) >> 3)];
            if (_framesBufferLength > 0)
                Array.Copy(temp, _framesBufferOffset, _framesBuffer, 0, _framesBufferLength);
            _framesBufferOffset = 0;
        }

        _samplesInBuffer = 0;

        if (Pcm.BitsPerSample != 16 && Pcm.BitsPerSample != 24)
            throw new InvalidDataException("invalid flac file");

        Samples = new int[FlakeConstants.Maxblocksize * Pcm.ChannelCount];
        _residualBuffer = new int[FlakeConstants.Maxblocksize * Pcm.ChannelCount];
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
        Pcm = pcm;
        _crc8 = new Crc8();

        Samples = new int[FlakeConstants.Maxblocksize * Pcm.ChannelCount];
        _residualBuffer = new int[FlakeConstants.Maxblocksize * Pcm.ChannelCount];
        _frame = new FlacFrame(Pcm.ChannelCount);
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
    public TimeSpan Duration => Length < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Length / Pcm.SampleRate);

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
                        if (_seekTable[st].Number <= value &&
                            (bestSt == -1 || _seekTable[st].Number > _seekTable[bestSt].Number))
                        {
                            bestSt = st;
                        }
                    }

                    if (bestSt != -1)
                    {
                        _framesBufferLength = 0;
                        _samplesInBuffer = 0;
                        _samplesBufferOffset = 0;
                        _io.Position = _seekTable[bestSt].Offset + _firstFrameOffset;
                        _sampleOffset = _seekTable[bestSt].Number;
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
    public AudioPcmConfig Pcm { get; private set; }

    /// <summary>
    /// Gets the file path of the FLAC source, or null if reading from a stream.
    /// </summary>
    public string Path { get; } = null!;

    private unsafe void Interlace(AudioBuffer buff, int offset, int count)
    {
        if (Pcm.ChannelCount == 2)
        {
            fixed (int* src = &Samples[_samplesBufferOffset])
            {
                buff.Interlace(offset, src, src + FlakeConstants.Maxblocksize, count);
            }
        }
        else
        {
            for (var ch = 0; ch < Pcm.ChannelCount; ch++)
                fixed (int* res = &buff.Samples[offset, ch], src = &Samples[_samplesBufferOffset + ch * FlakeConstants.Maxblocksize])
                {
                    var psrc = src;
                    for (var i = 0; i < count; i++)
                    {
                        res[i * Pcm.ChannelCount] = *(psrc++);
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
            throw new InvalidDataException("invalid frame");

        var vbs = bitreader.Readbit();
        frame.BsCode0 = (int)bitreader.Readbits(4);
        var srCode0 = bitreader.Readbits(4);
        frame.ChMode = (ChannelMode)bitreader.Readbits(4);
        var bpsCode = bitreader.Readbits(3);
        if (FlakeConstants.FlacBitdepths[bpsCode] != Pcm.BitsPerSample)
            throw new NotSupportedException("unsupported bps coding");

        var t1 = bitreader.Readbit(); // == 0?????
        if (t1 != 0)
            throw new NotSupportedException("unsupported frame coding");

        frame.FrameNumber = (int)bitreader.ReadUtf8();

        switch (frame.BsCode0)
        {
            // custom block size
            case 6:
                frame.BsCode1 = (int)bitreader.Readbits(8);
                frame.Blocksize = frame.BsCode1 + 1;
                break;
            case 7:
                frame.BsCode1 = (int)bitreader.Readbits(16);
                frame.Blocksize = frame.BsCode1 + 1;
                break;
            default:
                frame.Blocksize = FlakeConstants.FlacBlocksizes[frame.BsCode0];
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

        var frameChannels = (int)frame.ChMode + 1;
        switch (frameChannels)
        {
            case > 11:
                throw new InvalidDataException("invalid channel mode");
            // Mid/Left/Right Side Stereo
            case 2 or > 8:
                frameChannels = 2;
                break;
            default:
                frame.ChMode = ChannelMode.NotStereo;
                break;
        }

        if (frameChannels != Pcm.ChannelCount)
            throw new InvalidDataException("invalid channel mode");

        // CRC-8 of frame header
        var crc = DoCrc ? _crc8.ComputeChecksum(bitreader.Buffer, headerStart, bitreader.Position - headerStart) : (byte)0;
        frame.Crc8 = (byte)bitreader.Readbits(8);
        if (DoCrc && frame.Crc8 != crc)
            throw new InvalidDataException("header crc mismatch");
    }

    private static unsafe void decode_subframe_constant(BitReader bitreader, FlacFrame frame, int ch)
    {
        var obits = frame.Subframes[ch].Obits;
        frame.Subframes[ch].Best.Residual[0] = bitreader.ReadbitsSigned(obits);
    }

    private static unsafe void decode_subframe_verbatim(BitReader bitreader, FlacFrame frame, int ch)
    {
        var obits = frame.Subframes[ch].Obits;
        for (var i = 0; i < frame.Blocksize; i++)
        {
            frame.Subframes[ch].Best.Residual[i] = bitreader.ReadbitsSigned(obits);
        }
    }

    private static unsafe void decode_residual(BitReader bitreader, FlacFrame frame, int ch)
    {
        // rice-encoded block
        // coding method
        frame.Subframes[ch].Best.Rc.CodingMethod = (int)bitreader.Readbits(2); // ????? == 0
        if (frame.Subframes[ch].Best.Rc.CodingMethod != 0 && frame.Subframes[ch].Best.Rc.CodingMethod != 1)
            throw new NotSupportedException("unsupported residual coding");
        // partition order
        frame.Subframes[ch].Best.Rc.Porder = (int)bitreader.Readbits(4);
        if (frame.Subframes[ch].Best.Rc.Porder > 8)
            throw new InvalidDataException("invalid partition order");

        var psize = frame.Blocksize >> frame.Subframes[ch].Best.Rc.Porder;
        var resCnt = psize - frame.Subframes[ch].Best.Order;

        var riceLen = 4 + frame.Subframes[ch].Best.Rc.CodingMethod;
        // residual
        var j = frame.Subframes[ch].Best.Order;
        var r = frame.Subframes[ch].Best.Residual + j;
        for (var p = 0; p < (1 << frame.Subframes[ch].Best.Rc.Porder); p++)
        {
            if (p == 1)
            {
                resCnt = psize;
            }

            var n = Math.Min(resCnt, frame.Blocksize - j);

            var k = frame.Subframes[ch].Best.Rc.Rparams[p] = (int)bitreader.Readbits(riceLen);
            if (k == (1 << riceLen) - 1)
            {
                k = frame.Subframes[ch].Best.Rc.EscBps[p] = (int)bitreader.Readbits(5);
                for (var i = n; i > 0; i--)
                {
                    *(r++) = bitreader.ReadbitsSigned(k);
                }
            }
            else
            {
                bitreader.ReadRiceBlock(n, k, r);
                r += n;
            }

            j += n;
        }
    }

    private static unsafe void decode_subframe_fixed(BitReader bitreader, FlacFrame frame, int ch)
    {
        // warm-up samples
        var obits = frame.Subframes[ch].Obits;
        for (var i = 0; i < frame.Subframes[ch].Best.Order; i++)
        {
            frame.Subframes[ch].Best.Residual[i] = bitreader.ReadbitsSigned(obits);
        }

        // residual
        decode_residual(bitreader, frame, ch);
    }

    private static unsafe void decode_subframe_lpc(BitReader bitreader, FlacFrame frame, int ch)
    {
        // warm-up samples
        var obits = frame.Subframes[ch].Obits;
        for (var i = 0; i < frame.Subframes[ch].Best.Order; i++)
        {
            frame.Subframes[ch].Best.Residual[i] = bitreader.ReadbitsSigned(obits);
        }

        // LPC coefficients
        frame.Subframes[ch].Best.Cbits = (int)bitreader.Readbits(4) + 1; // lpc_precision
        if (frame.Subframes[ch].Best.Cbits >= 16)
            throw new InvalidDataException("cbits >= 16");

        frame.Subframes[ch].Best.Shift = bitreader.ReadbitsSigned(5);
        if (frame.Subframes[ch].Best.Shift < 0)
            throw new InvalidDataException("negative shift");

        for (var i = 0; i < frame.Subframes[ch].Best.Order; i++)
        {
            frame.Subframes[ch].Best.Coefs[i] = bitreader.ReadbitsSigned(frame.Subframes[ch].Best.Cbits);
        }

        // residual
        decode_residual(bitreader, frame, ch);
    }

    private unsafe void decode_subframes(BitReader bitreader, FlacFrame frame)
    {
        fixed (int* r = _residualBuffer, s = Samples)
        {
            for (var ch = 0; ch < Pcm.ChannelCount; ch++)
            {
                // subframe header
                var t1 = bitreader.Readbit(); // ?????? == 0
                if (t1 != 0)
                    throw new NotSupportedException("unsupported subframe coding (ch == " + ch + ")");

                var typeCode = (int)bitreader.Readbits(6);
                frame.Subframes[ch].Wbits = (int)bitreader.Readbit();
                if (frame.Subframes[ch].Wbits != 0)
                {
                    frame.Subframes[ch].Wbits += (int)bitreader.ReadUnary();
                }

                frame.Subframes[ch].Obits = Pcm.BitsPerSample - frame.Subframes[ch].Wbits;
                switch (frame.ChMode)
                {
                    case ChannelMode.MidSide:
                    case ChannelMode.LeftSide: frame.Subframes[ch].Obits += ch; break;
                    case ChannelMode.RightSide: frame.Subframes[ch].Obits += 1 - ch; break;
                }

                frame.Subframes[ch].Best.Type = (SubframeType)typeCode;
                frame.Subframes[ch].Best.Order = 0;

                if ((typeCode & (uint)SubframeType.LPC) != 0)
                {
                    frame.Subframes[ch].Best.Order = (typeCode - (int)SubframeType.LPC) + 1;
                    frame.Subframes[ch].Best.Type = SubframeType.LPC;
                }
                else if ((typeCode & (uint)SubframeType.Fixed) != 0)
                {
                    frame.Subframes[ch].Best.Order = (typeCode - (int)SubframeType.Fixed);
                    frame.Subframes[ch].Best.Type = SubframeType.Fixed;
                }

                frame.Subframes[ch].Best.Residual = r + ch * FlakeConstants.Maxblocksize;
                frame.Subframes[ch].Samples = s + ch * FlakeConstants.Maxblocksize;

                // subframe
                switch (frame.Subframes[ch].Best.Type)
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

    private static unsafe void restore_samples_fixed(FlacFrame frame, int ch)
    {
        var sub = frame.Subframes[ch];

        AudioSamples.MemCpy(sub.Samples, sub.Best.Residual, sub.Best.Order);
        var data = sub.Samples + sub.Best.Order;
        var residual = sub.Best.Residual + sub.Best.Order;
        var dataLen = frame.Blocksize - sub.Best.Order;
        int s1;
        switch (sub.Best.Order)
        {
            case 0:
                AudioSamples.MemCpy(data, residual, dataLen);
                break;
            case 1:
                s1 = data[-1];
                for (var i = dataLen; i > 0; i--)
                {
                    s1 += *(residual++);
                    *(data++) = s1;
                }

                //data[i] = residual[i] + data[i - 1];
                break;
            case 2:
                var s2 = data[-2];
                s1 = data[-1];
                for (var i = dataLen; i > 0; i--)
                {
                    var s0 = *(residual++) + (s1 << 1) - s2;
                    *(data++) = s0;
                    s2 = s1;
                    s1 = s0;
                }

                //data[i] = residual[i] + data[i - 1] * 2  - data[i - 2];
                break;
            case 3:
                for (var i = 0; i < dataLen; i++)
                {
                    data[i] = residual[i] + (((data[i - 1] - data[i - 2]) << 1) + (data[i - 1] - data[i - 2])) + data[i - 3];
                }

                break;
            case 4:
                for (var i = 0; i < dataLen; i++)
                {
                    data[i] = residual[i] + ((data[i - 1] + data[i - 3]) << 2) - ((data[i - 2] << 2) + (data[i - 2] << 1)) - data[i - 4];
                }

                break;
        }
    }

    private static unsafe void restore_samples_lpc(FlacFrame frame, int ch)
    {
        var sub = frame.Subframes[ch];
        ulong csum = 0;
        fixed (int* coefs = sub.Best.Coefs)
        {
            for (var i = sub.Best.Order; i > 0; i--)
            {
                csum += (ulong)Math.Abs(coefs[i - 1]);
            }

            if ((csum << sub.Obits) >= 1UL << 32)
                Lpc.DecodeResidualLong(sub.Best.Residual, sub.Samples, frame.Blocksize, sub.Best.Order, coefs, sub.Best.Shift);
            else
                Lpc.DecodeResidual(sub.Best.Residual, sub.Samples, frame.Blocksize, sub.Best.Order, coefs, sub.Best.Shift);
        }
    }

    private unsafe void restore_samples(FlacFrame frame)
    {
        for (var ch = 0; ch < Pcm.ChannelCount; ch++)
        {
            switch (frame.Subframes[ch].Best.Type)
            {
                case SubframeType.Constant:
                    AudioSamples.MemSet(frame.Subframes[ch].Samples, frame.Subframes[ch].Best.Residual[0], frame.Blocksize);
                    break;
                case SubframeType.Verbatim:
                    AudioSamples.MemCpy(frame.Subframes[ch].Samples, frame.Subframes[ch].Best.Residual, frame.Blocksize);
                    break;
                case SubframeType.Fixed:
                    restore_samples_fixed(frame, ch);
                    break;
                case SubframeType.LPC:
                    restore_samples_lpc(frame, ch);
                    break;
            }

            if (frame.Subframes[ch].Wbits != 0)
            {
                var s = frame.Subframes[ch].Samples;
                var x = frame.Subframes[ch].Wbits;
                for (var i = frame.Blocksize; i > 0; i--)
                {
                    *(s++) <<= x;
                }
            }
        }

        if (frame.ChMode != ChannelMode.NotStereo)
        {
            var l = frame.Subframes[0].Samples;
            var r = frame.Subframes[1].Samples;
            switch (frame.ChMode)
            {
                case ChannelMode.LeftRight:
                    break;
                case ChannelMode.MidSide:
                    for (var i = frame.Blocksize; i > 0; i--)
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
                    for (var i = frame.Blocksize; i > 0; i--)
                    {
                        int _l = *(l++), _r = *r;
                        *(r++) = _l - _r;
                    }

                    break;
                case ChannelMode.RightSide:
                    for (var i = frame.Blocksize; i > 0; i--)
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
            var crc1 = _framereader.GetCrc16();
            var crc2 = _framereader.ReadUshort();
            if (DoCrc && crc1 != crc2)
                throw new InvalidDataException("frame crc mismatch");

            restore_samples(_frame);
            _samplesInBuffer = _frame.Blocksize;
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
                        skip |= (_framesBuffer[0] & 0x7f);
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
                        Pcm = new AudioPcmConfig(bitsPerSample, channels, sampleRate);
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
                            _seekTable[e].Number = bitreader.ReadLong();
                            _seekTable[e].Offset = bitreader.ReadLong();
                            _seekTable[e].Framesize = bitreader.ReadUshort();
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