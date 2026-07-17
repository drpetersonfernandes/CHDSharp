using CHDSharp.LZMA.LZ;

namespace CHDSharp.LZMA;

/// <summary>
/// Provides a read-only decompression <see cref="Stream"/> backed by LZMA or LZMA2 compressed data.
/// </summary>
/// <remarks>
/// Supports forward-only reading. Seeking is limited to <see cref="SeekOrigin.Current"/>.
/// </remarks>
internal class LzmaStream : Stream
{
    private readonly Stream _inputStream;
    private readonly long _inputSize;
    private readonly long _outputSize;

    private readonly int _dictionarySize;
    private readonly OutWindow _outWindow = new();
    private readonly RangeCoder.Decoder _rangeDecoder = new();
    private Decoder _decoder = null!;

    private long _position;
    private bool _endReached;
    private long _availableBytes;
    private long _rangeDecoderLimit;
    private long _inputPosition;

    // LZMA2
    private readonly bool _isLzma2;
    private bool _uncompressedChunk;
    private bool _needDictReset = true;
    private bool _needProps = true;

    /// <summary>
    /// Initializes a new LZMA decompression stream with unknown input and output sizes.
    /// </summary>
    /// <param name="properties">LZMA properties header (5 bytes).</param>
    /// <param name="inputStream">The compressed source stream.</param>
    public LzmaStream(byte[] properties, Stream inputStream)
        : this(properties, inputStream, -1, -1, null!, properties.Length < 5)
    {
    }

    /// <summary>
    /// Initializes a new LZMA decompression stream with a known input size.
    /// </summary>
    /// <param name="properties">LZMA properties header (5 bytes).</param>
    /// <param name="inputStream">The compressed source stream.</param>
    /// <param name="inputSize">Exact size in bytes of the compressed data, or -1 if unknown.</param>
    public LzmaStream(byte[] properties, Stream inputStream, long inputSize)
        : this(properties, inputStream, inputSize, -1, null!, properties.Length < 5)
    {
    }

    /// <summary>
    /// Initializes a new LZMA decompression stream with known input and output sizes.
    /// </summary>
    /// <param name="properties">LZMA properties header (5 bytes).</param>
    /// <param name="inputStream">The compressed source stream.</param>
    /// <param name="inputSize">Exact size in bytes of the compressed data, or -1 if unknown.</param>
    /// <param name="outputSize">Exact size in bytes of the decompressed data, or -1 if unknown.</param>
    public LzmaStream(byte[] properties, Stream inputStream, long inputSize, long outputSize)
        : this(properties, inputStream, inputSize, outputSize, null!, properties.Length < 5)
    {
    }

    /// <summary>
    /// Initializes a new LZMA or LZMA2 decompression stream with full configuration.
    /// </summary>
    /// <param name="properties">Properties header (5 bytes for LZMA, 1 byte for LZMA2).</param>
    /// <param name="inputStream">The compressed source stream.</param>
    /// <param name="inputSize">Exact size in bytes of the compressed data, or -1 if unknown.</param>
    /// <param name="outputSize">Exact size in bytes of the decompressed data, or -1 if unknown.</param>
    /// <param name="presetDictionary">Optional preset dictionary stream for training the decoder, or <c>null</c>.</param>
    /// <param name="isLzma2"><c>true</c> to use LZMA2 format; <c>false</c> for LZMA.</param>
    /// <param name="outWindowBuff">Optional pre-allocated buffer for the output window, or <c>null</c>.</param>
    public LzmaStream(byte[] properties, Stream inputStream, long inputSize, long outputSize,
        Stream presetDictionary, bool isLzma2, byte[]? outWindowBuff = null)
    {
        _inputStream = inputStream;
        _inputSize = inputSize;
        _outputSize = outputSize;
        _isLzma2 = isLzma2;

        if (!isLzma2)
        {
            _dictionarySize = BitConverter.ToInt32(properties, 1);
            _outWindow.Create(_dictionarySize, outWindowBuff);
            if (presetDictionary != null)
                _outWindow.Train(presetDictionary);

            _rangeDecoder.Init(inputStream);

            _decoder = new Decoder();
            _decoder.SetDecoderProperties(properties);
            Properties = properties;

            _availableBytes = outputSize < 0 ? long.MaxValue : outputSize;
            _rangeDecoderLimit = inputSize;
        }
        else
        {
            _dictionarySize = 2 | (properties[0] & 1);
            _dictionarySize <<= (properties[0] >> 1) + 11;

            _outWindow.Create(_dictionarySize);
            if (presetDictionary != null)
            {
                _outWindow.Train(presetDictionary);
                _needDictReset = false;
            }

            Properties = new byte[1];
            _availableBytes = 0;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the stream supports reading. Always <c>true</c>.
    /// </summary>
    public override bool CanRead => true;

    /// <summary>
    /// Gets a value indicating whether the stream supports seeking. Always <c>false</c>.
    /// </summary>
    public override bool CanSeek => false;

    /// <summary>
    /// Gets a value indicating whether the stream supports writing. Always <c>false</c>.
    /// </summary>
    public override bool CanWrite => false;

    /// <summary>
    /// Does nothing. The stream has no buffers to flush.
    /// </summary>
    public override void Flush()
    {
    }

    /// <summary>
    /// Gets the total length of the decompressed stream, or the current position plus remaining available bytes if unknown.
    /// </summary>
    public override long Length => _position + _availableBytes;

    /// <summary>
    /// Gets or sets the current position within the decompressed stream.
    /// </summary>
    /// <exception cref="NotSupportedException">The setter always throws. Use <see cref="Seek"/> to advance.</exception>
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Reads a sequence of decompressed bytes from the stream and advances the position.
    /// </summary>
    /// <param name="buffer">The buffer to write decompressed data into.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin writing.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The total number of bytes read into the buffer, or 0 if the end of the stream has been reached.</returns>
    /// <exception cref="DataErrorException">Thrown when the compressed data is corrupt or truncated.</exception>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_endReached)
            return 0;

        var total = 0;
        while (total < count)
        {
            if (_availableBytes == 0)
            {
                if (_isLzma2)
                    DecodeChunkHeader();
                else
                {
                    _endReached = true;
                }

                if (_endReached)
                    break;
            }

            var toProcess = count - total;
            if (toProcess > _availableBytes)
            {
                toProcess = (int)_availableBytes;
            }

            _outWindow.SetLimit(toProcess);
            if (_uncompressedChunk)
            {
                _inputPosition += _outWindow.CopyStream(_inputStream, toProcess);
            }
            else if (_decoder.Code(_dictionarySize, _outWindow, _rangeDecoder)
                     && _outputSize < 0)
            {
                _availableBytes = _outWindow.AvailableBytes;
            }

            var read = _outWindow.Read(buffer, offset, toProcess);
            total += read;
            offset += read;
            _position += read;
            _availableBytes -= read;

            if (_availableBytes == 0 && !_uncompressedChunk)
            {
                _rangeDecoder.ReleaseStream();
                if (!_rangeDecoder.IsFinished || (_rangeDecoderLimit >= 0 && _rangeDecoder.Total != _rangeDecoderLimit))
                    throw new DataErrorException();

                _inputPosition += _rangeDecoder.Total;
                if (_outWindow.HasPending)
                    throw new DataErrorException();
            }
        }

        if (_endReached)
        {
            if ((_inputSize >= 0 && _inputPosition != _inputSize) || (_outputSize >= 0 && _position != _outputSize))
                throw new DataErrorException();
        }

        return total;
    }

    private void DecodeChunkHeader()
    {
        var control = _inputStream.ReadByte();
        _inputPosition++;

        switch (control)
        {
            case 0x00:
                _endReached = true;
                return;
            case >= 0xE0 or 0x01:
                _needProps = true;
                _needDictReset = false;
                _outWindow.Reset();
                break;
            default:
            {
                if (_needDictReset)
                {
                    throw new DataErrorException();
                }

                break;
            }
        }

        switch (control)
        {
            case >= 0x80:
            {
                _uncompressedChunk = false;

                _availableBytes = (control & 0x1F) << 16;
                _availableBytes += (_inputStream.ReadByte() << 8) + _inputStream.ReadByte() + 1;
                _inputPosition += 2;

                _rangeDecoderLimit = (_inputStream.ReadByte() << 8) + _inputStream.ReadByte() + 1;
                _inputPosition += 2;

                if (control >= 0xC0)
                {
                    _needProps = false;
                    Properties[0] = (byte)_inputStream.ReadByte();
                    _inputPosition++;

                    _decoder = new Decoder();
                    _decoder.SetDecoderProperties(Properties);
                }
                else if (_needProps)
                {
                    throw new DataErrorException();
                }
                else if (control >= 0xA0)
                {
                    _decoder = new Decoder();
                    _decoder.SetDecoderProperties(Properties);
                }

                _rangeDecoder.Init(_inputStream);
                break;
            }
            case > 0x02:
                throw new DataErrorException();
            default:
                _uncompressedChunk = true;
                _availableBytes = (_inputStream.ReadByte() << 8) + _inputStream.ReadByte() + 1;
                _inputPosition += 2;
                break;
        }
    }

    /// <summary>
    /// Advances the current position by <paramref name="offset"/> bytes from <see cref="SeekOrigin.Current"/>.
    /// Other origins are not supported.
    /// </summary>
    /// <param name="offset">The number of bytes to skip forward.</param>
    /// <param name="origin">Must be <see cref="SeekOrigin.Current"/>.</param>
    /// <returns>The new position in the stream.</returns>
    /// <exception cref="NotSupportedException"><paramref name="origin"/> is not <see cref="SeekOrigin.Current"/>.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (origin != SeekOrigin.Current)
            throw new NotSupportedException();

        var tmpBuff = new byte[1024];
        var sizeToGo = offset;
        while (sizeToGo > 0)
        {
            var sizenow = sizeToGo > 1024 ? 1024 : (int)sizeToGo;
            var read = Read(tmpBuff, 0, sizenow);
            if (read == 0)
                break;

            sizeToGo -= read;
        }

        return offset;
    }

    /// <summary>
    /// Not supported. The stream is read-only.
    /// </summary>
    /// <param name="value">Ignored.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Not supported. The stream is read-only.
    /// </summary>
    /// <param name="buffer">Ignored.</param>
    /// <param name="offset">Ignored.</param>
    /// <param name="count">Ignored.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the current LZMA/LZMA2 properties used for decompressing subsequent chunks.
    /// </summary>
    public byte[] Properties { get; }
}