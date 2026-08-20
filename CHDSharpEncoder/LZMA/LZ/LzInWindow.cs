namespace CHDSharpEncoder.LZMA.LZ;

/// <summary>Sliding input window buffer for the LZMA encoder, ported from the LZMA SDK (public domain).</summary>
internal class InWindow
{
    internal byte[] BufferBase = null!;
    private Stream _stream = null!;
    private uint _posLimit;
    private bool _streamEndWasReached;
    private uint _pointerToLastSafePosition;

    internal uint BufferOffset;

    internal uint BlockSize;
    internal uint Pos;
    private uint _keepSizeBefore;
    private uint _keepSizeAfter;
    internal uint StreamPos;

    internal void MoveBlock()
    {
        var offset = BufferOffset + Pos - _keepSizeBefore;
        if (offset > 0)
        {
            offset--;
        }

        var numBytes = BufferOffset + StreamPos - offset;

        for (uint i = 0; i < numBytes; i++)
        {
            BufferBase[i] = BufferBase[offset + i];
        }

        BufferOffset -= offset;
    }

    internal virtual void ReadBlock()
    {
        if (_streamEndWasReached)
        {
            return;
        }

        while (true)
        {
            var size = (int)(0 - BufferOffset + BlockSize - StreamPos);
            if (size == 0)
            {
                return;
            }

            var numReadBytes = _stream.Read(BufferBase, (int)(BufferOffset + StreamPos), size);
            if (numReadBytes == 0)
            {
                _posLimit = StreamPos;
                var pointerToPosition = BufferOffset + _posLimit;
                if (pointerToPosition > _pointerToLastSafePosition)
                {
                    _posLimit = _pointerToLastSafePosition - BufferOffset;
                }

                _streamEndWasReached = true;
                return;
            }

            StreamPos += (uint)numReadBytes;
            if (StreamPos >= Pos + _keepSizeAfter)
            {
                _posLimit = StreamPos - _keepSizeAfter;
            }
        }
    }

    private void Free()
    {
        BufferBase = null!;
    }

    internal void Create(uint keepSizeBefore, uint keepSizeAfter, uint keepSizeReserv)
    {
        _keepSizeBefore = keepSizeBefore;
        _keepSizeAfter = keepSizeAfter;
        var blockSize = keepSizeBefore + keepSizeAfter + keepSizeReserv;
        if (BufferBase == null || BlockSize != blockSize)
        {
            Free();
            BlockSize = blockSize;
            BufferBase = new byte[BlockSize];
        }

        _pointerToLastSafePosition = BlockSize - keepSizeAfter;
    }

    internal void SetStream(Stream stream)
    {
        _stream = stream;
    }

    internal void ReleaseStream()
    {
        _stream = null!;
    }

    internal void Init()
    {
        BufferOffset = 0;
        Pos = 0;
        StreamPos = 0;
        _streamEndWasReached = false;
        ReadBlock();
    }

    internal void MovePos()
    {
        Pos++;
        if (Pos > _posLimit)
        {
            var pointerToPosition = BufferOffset + Pos;
            if (pointerToPosition > _pointerToLastSafePosition)
            {
                MoveBlock();
            }

            ReadBlock();
        }
    }

    internal byte GetIndexByte(int index)
    {
        return BufferBase[BufferOffset + Pos + index];
    }

    internal uint GetMatchLen(int index, uint distance, uint limit)
    {
        if (_streamEndWasReached)
        {
            if (Pos + index + limit > StreamPos)
            {
                limit = StreamPos - (uint)(Pos + index);
            }
        }

        distance++;
        var pby = BufferOffset + Pos + (uint)index;

        uint i;
        for (i = 0; i < limit && BufferBase[pby + i] == BufferBase[pby + i - distance]; i++)
        {
        }

        return i;
    }

    internal uint GetNumAvailableBytes()
    {
        return StreamPos - Pos;
    }

    internal void ReduceOffsets(int subValue)
    {
        BufferOffset += (uint)subValue;
        _posLimit -= (uint)subValue;
        Pos -= (uint)subValue;
        StreamPos -= (uint)subValue;
    }
}