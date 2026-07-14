using CHDSharp.LZMA.LZ;

namespace CHDSharp.LZMA;

/// <summary>
/// Provides a read-only decompression <see cref="Stream"/> backed by LZMA or LZMA2 compressed data.
/// </summary>
/// <remarks>
/// Supports forward-only reading. Seeking is limited to <see cref="SeekOrigin.Current"/>.
/// </remarks>
public class LzmaStream : Stream
{
    private Stream inputStream;
    private long inputSize;
    private long outputSize;

    private int dictionarySize;
    private OutWindow outWindow = new OutWindow();
    private RangeCoder.Decoder rangeDecoder = new RangeCoder.Decoder();
    private Decoder decoder;

    private long position = 0;
    private bool endReached = false;
    private long availableBytes;
    private long rangeDecoderLimit;
    private long inputPosition = 0;

    // LZMA2
    private bool isLZMA2;
    private bool uncompressedChunk = false;
    private bool needDictReset = true;
    private bool needProps = true;
    private byte[] props = new byte[5];

    /// <summary>
    /// Initializes a new LZMA decompression stream with unknown input and output sizes.
    /// </summary>
    /// <param name="properties">LZMA properties header (5 bytes).</param>
    /// <param name="inputStream">The compressed source stream.</param>
	public LzmaStream(byte[] properties, Stream inputStream)
        : this(properties, inputStream, -1, -1, null, properties.Length < 5)
    {
    }

    /// <summary>
    /// Initializes a new LZMA decompression stream with a known input size.
    /// </summary>
    /// <param name="properties">LZMA properties header (5 bytes).</param>
    /// <param name="inputStream">The compressed source stream.</param>
    /// <param name="inputSize">Exact size in bytes of the compressed data, or -1 if unknown.</param>
    public LzmaStream(byte[] properties, Stream inputStream, long inputSize)
        : this(properties, inputStream, inputSize, -1, null, properties.Length < 5)
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
        : this(properties, inputStream, inputSize, outputSize, null, properties.Length < 5)
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
    /// <param name="isLZMA2"><c>true</c> to use LZMA2 format; <c>false</c> for LZMA.</param>
    /// <param name="outWindowBuff">Optional pre-allocated buffer for the output window, or <c>null</c>.</param>
    public LzmaStream(byte[] properties, Stream inputStream, long inputSize, long outputSize,
        Stream presetDictionary, bool isLZMA2, byte[] outWindowBuff = null)
    {
        this.inputStream = inputStream;
        this.inputSize = inputSize;
        this.outputSize = outputSize;
        this.isLZMA2 = isLZMA2;

        if (!isLZMA2)
        {
            dictionarySize = BitConverter.ToInt32(properties, 1);
            outWindow.Create(dictionarySize,outWindowBuff);
            if (presetDictionary != null)
                outWindow.Train(presetDictionary);

            rangeDecoder.Init(inputStream);

            decoder = new Decoder();
            decoder.SetDecoderProperties(properties);
            props = properties;

            availableBytes = outputSize < 0 ? long.MaxValue : outputSize;
            rangeDecoderLimit = inputSize;
        }
        else
        {
            dictionarySize = 2 | (properties[0] & 1);
            dictionarySize <<= (properties[0] >> 1) + 11;

            outWindow.Create(dictionarySize);
            if (presetDictionary != null)
            {
                outWindow.Train(presetDictionary);
                needDictReset = false;
            }

            props = new byte[1];
            availableBytes = 0;
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
	public override long Length => position + availableBytes;

    /// <summary>
    /// Gets or sets the current position within the decompressed stream.
    /// </summary>
    /// <exception cref="NotSupportedException">The setter always throws. Use <see cref="Seek"/> to advance.</exception>
	public override long Position
	{
		get => position;
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
        if (endReached)
            return 0;

        var total = 0;
        while (total < count)
        {
            if (availableBytes == 0)
            {
                if (isLZMA2)
                    decodeChunkHeader();
                else
                {
                    endReached = true;
                }

                if (endReached)
                    break;
            }

            var toProcess = count - total;
            if (toProcess > availableBytes)
            {
                toProcess = (int)availableBytes;
            }

            outWindow.SetLimit(toProcess);
            if (uncompressedChunk)
            {
                inputPosition += outWindow.CopyStream(inputStream, toProcess);
            }
            else if (decoder.Code(dictionarySize, outWindow, rangeDecoder)
                     && outputSize < 0)
            {
                availableBytes = outWindow.AvailableBytes;
            }

            var read = outWindow.Read(buffer, offset, toProcess);
            total += read;
            offset += read;
            position += read;
            availableBytes -= read;

            if (availableBytes == 0 && !uncompressedChunk)
            {
                rangeDecoder.ReleaseStream();
                if (!rangeDecoder.IsFinished || (rangeDecoderLimit >= 0 && rangeDecoder.Total != rangeDecoderLimit))
                    throw new DataErrorException();

                inputPosition += rangeDecoder.Total;
                if (outWindow.HasPending)
                    throw new DataErrorException();
            }
        }

        if (endReached)
        {
            if (inputSize >= 0 && inputPosition != inputSize)
                throw new DataErrorException();
            if (outputSize >= 0 && position != outputSize)
                throw new DataErrorException();
        }

        return total;
    }

    private void decodeChunkHeader()
    {
        var control = inputStream.ReadByte();
        inputPosition++;

        if (control == 0x00)
        {
            endReached = true;
            return;
        }

        if (control >= 0xE0 || control == 0x01)
        {
            needProps = true;
            needDictReset = false;
            outWindow.Reset();
        }
        else if (needDictReset)
            throw new DataErrorException();

        if (control >= 0x80)
        {
            uncompressedChunk = false;

            availableBytes = (control & 0x1F) << 16;
            availableBytes += (inputStream.ReadByte() << 8) + inputStream.ReadByte() + 1;
            inputPosition += 2;

            rangeDecoderLimit = (inputStream.ReadByte() << 8) + inputStream.ReadByte() + 1;
            inputPosition += 2;

            if (control >= 0xC0)
            {
                needProps = false;
                props[0] = (byte)inputStream.ReadByte();
                inputPosition++;

                decoder = new Decoder();
                decoder.SetDecoderProperties(props);
            }
            else if (needProps)
                throw new DataErrorException();
            else if (control >= 0xA0)
            {
                decoder = new Decoder();
                decoder.SetDecoderProperties(props);
            }

            rangeDecoder.Init(inputStream);
        }
        else if (control > 0x02)
            throw new DataErrorException();
        else
        {
            uncompressedChunk = true;
            availableBytes = (inputStream.ReadByte() << 8) + inputStream.ReadByte() + 1;
            inputPosition += 2;
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
	public byte[] Properties => props;
}