namespace CHDSharp.LZMA.LZ;

/// <summary>Sliding-window output buffer for LZMA decompression. Handles literal bytes, LZ77 copy blocks, and streaming to the output stream.</summary>
internal class OutWindow
{
    private byte[] _buffer = null!;
    private int _windowSize;
    private int _pos;
    private int _streamPos;
    private int _pendingLen;
    private int _pendingDist;
    private Stream _stream = null!;

    /// <summary>Total number of bytes written so far.</summary>
    public long Total;
    /// <summary>Maximum number of bytes allowed to be written.</summary>
    public long Limit;

    /// <summary>Initialises or resizes the output window buffer.</summary>
    public void Create(int windowSize, byte[]? buffer=null)
    {
        if (buffer != null)
        {
            _windowSize = buffer.Length;
            _buffer = buffer;
        }

        if (_windowSize != windowSize)
        {
            _buffer = new byte[windowSize];
        }
        else
        {
            _buffer[windowSize - 1] = 0;
        }

        _windowSize = windowSize;
        _pos = 0;
        _streamPos = 0;
        _pendingLen = 0;
        Total = 0;
        Limit = 0;
    }

    /// <summary>Resets the window state.</summary>
    public void Reset()
    {
        Create(_windowSize);
    }

    /// <summary>Sets the output stream and releases any previous stream.</summary>
    public void Init(Stream stream)
    {
        ReleaseStream();
        _stream = stream;
    }

    /// <summary>Pre-fills the window with the tail of the stream for dictionary training.</summary>
    public void Train(Stream stream)
    {
        var len = stream.Length;
        var size = (len < _windowSize) ? (int)len : _windowSize;
        stream.Position = len - size;
        Total = 0;
        Limit = size;
        _pos = _windowSize - size;
        CopyStream(stream, size);
        if (_pos == _windowSize)
        {
            _pos = 0;
        }

        _streamPos = _pos;
    }

    /// <summary>Flushes pending data and releases the output stream reference.</summary>
    public void ReleaseStream()
    {
        Flush();
        _stream = null!;
    }

    /// <summary>Writes buffered data to the output stream.</summary>
    public void Flush()
    {
        if (_stream == null)
            return;

        var size = _pos - _streamPos;
        if (size == 0)
            return;

        _stream.Write(_buffer, _streamPos, size);
        if (_pos >= _windowSize)
        {
            _pos = 0;
        }

        _streamPos = _pos;
    }

    /// <summary>Copies a previously emitted block of bytes (LZ77 back-reference).</summary>
    public void CopyBlock(int distance, int len)
    {
        var size = len;
        var pos = _pos - distance - 1;
        if (pos < 0)
        {
            pos += _windowSize;
        }

        for (; size > 0 && _pos < _windowSize && Total < Limit; size--)
        {
            if (pos >= _windowSize)
            {
                pos = 0;
            }

            _buffer[_pos++] = _buffer[pos++];
            Total++;
            if (_pos >= _windowSize)
                Flush();
        }
        _pendingLen = size;
        _pendingDist = distance;
    }

    /// <summary>Writes a single literal byte to the output window.</summary>
    public void PutByte(byte b)
    {
        _buffer[_pos++] = b;
        Total++;
        if (_pos >= _windowSize)
            Flush();
    }

    /// <summary>Reads a previously written byte at the given distance (for back-references).</summary>
    public byte GetByte(int distance)
    {
        var pos = _pos - distance - 1;
        if (pos < 0)
        {
            pos += _windowSize;
        }

        return _buffer[pos];
    }

    /// <summary>Copies raw data from a stream into the output window.</summary>
    public int CopyStream(Stream stream, int len)
    {
        var size = len;
        while (size > 0 && _pos < _windowSize && Total < Limit)
        {
            var curSize = _windowSize - _pos;
            if (curSize > Limit - Total)
            {
                curSize = (int)(Limit - Total);
            }

            if (curSize > size)
            {
                curSize = size;
            }

            var numReadBytes = stream.Read(_buffer, _pos, curSize);
            if (numReadBytes == 0)
                throw new DataErrorException();

            size -= numReadBytes;
            _pos += numReadBytes;
            Total += numReadBytes;
            if (_pos >= _windowSize)
                Flush();
        }
        return len - size;
    }

    /// <summary>Sets the maximum total number of bytes that can be produced.</summary>
    public void SetLimit(long size)
    {
        Limit = Total + size;
    }

    /// <summary>Gets whether the window has space to write more data.</summary>
    public bool HasSpace => _pos < _windowSize && Total < Limit;

    /// <summary>Gets whether there is a pending copy-block operation.</summary>
    public bool HasPending => _pendingLen > 0;

    /// <summary>Reads decoded data from the window into a byte array.</summary>
    public int Read(byte[] buffer, int offset, int count)
    {
        if (_streamPos >= _pos)
            return 0;

        var size = _pos - _streamPos;
        if (size > count)
        {
            size = count;
        }

        Buffer.BlockCopy(_buffer, _streamPos, buffer, offset, size);
        _streamPos += size;
        if (_streamPos >= _windowSize)
        {
            _pos = 0;
            _streamPos = 0;
        }
        return size;
    }

    /// <summary>Completes any pending copy-block operation from a previous <see cref="CopyBlock"/> call.</summary>
    public void CopyPending()
    {
        if (_pendingLen > 0)
            CopyBlock(_pendingDist, _pendingLen);
    }

    /// <summary>Gets the number of bytes available for reading from the window.</summary>
    public int AvailableBytes => _pos - _streamPos;
}
