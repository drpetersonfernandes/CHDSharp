using CHDSharp.LZMA.LZ;

namespace CHDSharp.LZMA;

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

	public LzmaStream(byte[] properties, Stream inputStream)
        : this(properties, inputStream, -1, -1, null, properties.Length < 5)
    {
    }

    public LzmaStream(byte[] properties, Stream inputStream, long inputSize)
        : this(properties, inputStream, inputSize, -1, null, properties.Length < 5)
    {
    }

    public LzmaStream(byte[] properties, Stream inputStream, long inputSize, long outputSize)
        : this(properties, inputStream, inputSize, outputSize, null, properties.Length < 5)
    {
    }

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

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override void Flush()
	{
	}

	public override long Length => position + availableBytes;

	public override long Position
	{
		get => position;
		set => throw new NotSupportedException();
	}

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

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public byte[] Properties
    {
        get
        {
            return props;
        }
    }
}