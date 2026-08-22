namespace CHDSharpEncoder.LZMA.LZ;

/// <summary>Binary-tree match finder for the LZMA encoder, ported from the LZMA SDK (public domain).</summary>
internal class BinTree : InWindow, IMatchFinder
{
    private uint _cyclicBufferPos;
    private uint _cyclicBufferSize;
    private uint _matchMaxLen;

    private uint[] _son = [];
    private uint[] _hash = [];

    private uint _cutValue = 0xFF;
    private uint _hashMask;
    private uint _hashSizeSum;

    private bool _hashArray = true;

    private const uint KHash2Size = 1 << 10;
    private const uint KHash3Size = 1 << 16;
    private const uint KBt2HashSize = 1 << 16;
    private const uint KStartMaxLen = 1;
    private const uint KHash3Offset = KHash2Size;
    private const uint KEmptyHashValue = 0;
    private const uint KMaxValForNormalize = ((uint)1 << 31) - 1;

    private uint _numHashDirectBytes;
    private uint _minMatchCheck = 4;
    private uint _fixHashSize = KHash2Size + KHash3Size;

    internal void SetType(int numHashBytes)
    {
        _hashArray = numHashBytes > 2;
        if (_hashArray)
        {
            _numHashDirectBytes = 0;
            _minMatchCheck = 4;
            _fixHashSize = KHash2Size + KHash3Size;
        }
        else
        {
            _numHashDirectBytes = 2;
            _minMatchCheck = 2 + 1;
            _fixHashSize = 0;
        }
    }

    public new void SetStream(Stream stream)
    {
        base.SetStream(stream);
    }

    public new void ReleaseStream()
    {
        base.ReleaseStream();
    }

    public new void Init()
    {
        base.Init();
        for (uint i = 0; i < _hashSizeSum; i++)
        {
            _hash[i] = KEmptyHashValue;
        }

        _cyclicBufferPos = 0;
        ReduceOffsets(-1);
    }

    internal new void MovePos()
    {
        if (++_cyclicBufferPos >= _cyclicBufferSize)
        {
            _cyclicBufferPos = 0;
        }

        base.MovePos();
        if (Pos == KMaxValForNormalize)
        {
            Normalize();
        }
    }

    public new byte GetIndexByte(int index)
    {
        return base.GetIndexByte(index);
    }

    public new uint GetMatchLen(int index, uint distance, uint limit)
    {
        return base.GetMatchLen(index, distance, limit);
    }

    public new uint GetNumAvailableBytes()
    {
        return base.GetNumAvailableBytes();
    }

    public void Create(uint historySize, uint keepAddBufferBefore,
        uint matchMaxLen, uint keepAddBufferAfter)
    {
        if (historySize > KMaxValForNormalize - 256)
        {
            throw new Exception();
        }

        _cutValue = 16 + (matchMaxLen >> 1);

        var windowReservSize = (historySize + keepAddBufferBefore +
                                matchMaxLen + keepAddBufferAfter) / 2 + 256;

        base.Create(historySize + keepAddBufferBefore, matchMaxLen + keepAddBufferAfter, windowReservSize);

        _matchMaxLen = matchMaxLen;

        var cyclicBufferSize = historySize + 1;
        if (_cyclicBufferSize != cyclicBufferSize)
        {
            _son = new uint[(_cyclicBufferSize = cyclicBufferSize) * 2];
        }

        var hs = KBt2HashSize;

        if (_hashArray)
        {
            hs = historySize - 1;
            hs |= hs >> 1;
            hs |= hs >> 2;
            hs |= hs >> 4;
            hs |= hs >> 8;
            hs >>= 1;
            hs |= 0xFFFF;
            if (hs > 1 << 24)
            {
                hs >>= 1;
            }

            _hashMask = hs;
            hs++;
            hs += _fixHashSize;
        }

        if (hs != _hashSizeSum)
        {
            _hash = new uint[_hashSizeSum = hs];
        }
    }

    public uint GetMatches(uint[] distances)
    {
        uint lenLimit;
        if (Pos + _matchMaxLen <= StreamPos)
        {
            lenLimit = _matchMaxLen;
        }
        else
        {
            lenLimit = StreamPos - Pos;
            if (lenLimit < _minMatchCheck)
            {
                MovePos();
                return 0;
            }
        }

        uint offset = 0;
        var matchMinPos = Pos > _cyclicBufferSize ? Pos - _cyclicBufferSize : 0;
        var cur = BufferOffset + Pos;
        var maxLen = _hashArray ? 3u : KStartMaxLen;
        uint hashValue, hash2Value = 0, hash3Value = 0;

        if (_hashArray)
        {
            var temp = Crc.Table[BufferBase[cur]] ^ BufferBase[cur + 1];
            hash2Value = temp & (KHash2Size - 1);
            temp ^= (uint)BufferBase[cur + 2] << 8;
            hash3Value = temp & (KHash3Size - 1);
            hashValue = (temp ^ (Crc.Table[BufferBase[cur + 3]] << 5)) & _hashMask;
        }
        else
        {
            hashValue = BufferBase[cur] ^ ((uint)BufferBase[cur + 1] << 8);
        }

        var curMatch = _hash[_fixHashSize + hashValue];
        if (_hashArray)
        {
            var d2 = Pos - _hash[hash2Value];
            var d3 = Pos - _hash[KHash3Offset + hash3Value];
            _hash[hash2Value] = Pos;
            _hash[KHash3Offset + hash3Value] = Pos;
            _hash[_fixHashSize + hashValue] = Pos;

            var mmm = Pos < _cyclicBufferSize ? Pos : _cyclicBufferSize;

            if (d2 < mmm && BufferBase[cur - (int)d2] == BufferBase[cur])
            {
                distances[offset++] = 2;
                distances[offset++] = d2 - 1;
                var update = true;
                if (BufferBase[cur - (int)d2 + 2] == BufferBase[cur + 2])
                {
                }
                else if (d3 < mmm && BufferBase[cur - (int)d3] == BufferBase[cur])
                {
                    d2 = d3;
                    distances[offset++] = 0;
                    distances[offset++] = d3 - 1;
                }
                else
                {
                    update = false;
                }

                if (update)
                {
                    var cIdx = cur + (int)maxLen;
                    var limIdx = cur + (int)lenLimit;
                    while (cIdx != limIdx)
                    {
                        if (BufferBase[cIdx - (int)d2] != BufferBase[cIdx])
                        {
                            break;
                        }

                        cIdx++;
                    }

                    maxLen = (uint)(cIdx - cur);
                    distances[offset - 2] = maxLen;
                    if (maxLen == lenLimit)
                    {
                        Skip(1);
                        return offset;
                    }
                }
            }
            else if (d3 < mmm && BufferBase[cur - (int)d3] == BufferBase[cur])
            {
                d2 = d3;
                distances[offset++] = 0;
                distances[offset++] = d3 - 1;

                var cIdx = cur + (int)maxLen;
                var limIdx = cur + (int)lenLimit;
                while (cIdx != limIdx)
                {
                    if (BufferBase[cIdx - (int)d2] != BufferBase[cIdx])
                    {
                        break;
                    }

                    cIdx++;
                }

                maxLen = (uint)(cIdx - cur);
                distances[offset - 2] = maxLen;
                if (maxLen == lenLimit)
                {
                    Skip(1);
                    return offset;
                }
            }
        }
        else
        {
            _hash[hashValue] = Pos;
        }

        var ptr0 = (_cyclicBufferPos << 1) + 1;
        var ptr1 = _cyclicBufferPos << 1;

        uint len1;
        var len0 = len1 = _numHashDirectBytes;

        if (_numHashDirectBytes != 0)
        {
            if (curMatch > matchMinPos)
            {
                if (BufferBase[BufferOffset + curMatch + _numHashDirectBytes] !=
                    BufferBase[cur + _numHashDirectBytes])
                {
                    distances[offset++] = maxLen = _numHashDirectBytes;
                    distances[offset++] = Pos - curMatch - 1;
                }
            }
        }

        var count = _cutValue;

        while (true)
        {
            if (curMatch <= matchMinPos || count-- == 0)
            {
                _son[ptr0] = _son[ptr1] = KEmptyHashValue;
                break;
            }

            var delta = Pos - curMatch;
            var cyclicPos = (delta <= _cyclicBufferPos ? _cyclicBufferPos - delta : _cyclicBufferPos - delta + _cyclicBufferSize) << 1;

            var pby1 = BufferOffset + curMatch;
            var len = Math.Min(len0, len1);
            if (BufferBase[pby1 + len] == BufferBase[cur + len])
            {
                while (++len != lenLimit)
                {
                    if (BufferBase[pby1 + len] != BufferBase[cur + len])
                    {
                        break;
                    }
                }

                if (maxLen < len)
                {
                    distances[offset++] = maxLen = len;
                    distances[offset++] = delta - 1;
                    if (len == lenLimit)
                    {
                        _son[ptr1] = _son[cyclicPos];
                        _son[ptr0] = _son[cyclicPos + 1];
                        break;
                    }
                }
            }

            if (BufferBase[pby1 + len] < BufferBase[cur + len])
            {
                _son[ptr1] = curMatch;
                ptr1 = cyclicPos + 1;
                curMatch = _son[ptr1];
                len1 = len;
            }
            else
            {
                _son[ptr0] = curMatch;
                ptr0 = cyclicPos;
                curMatch = _son[ptr0];
                len0 = len;
            }
        }

        MovePos();
        return offset;
    }

    public void Skip(uint num)
    {
        do
        {
            uint lenLimit;
            if (Pos + _matchMaxLen <= StreamPos)
            {
                lenLimit = _matchMaxLen;
            }
            else
            {
                lenLimit = StreamPos - Pos;
                if (lenLimit < _minMatchCheck)
                {
                    MovePos();
                    continue;
                }
            }

            var matchMinPos = Pos > _cyclicBufferSize ? Pos - _cyclicBufferSize : 0;
            var cur = BufferOffset + Pos;

            uint hashValue;

            if (_hashArray)
            {
                var temp = Crc.Table[BufferBase[cur]] ^ BufferBase[cur + 1];
                var hash2Value = temp & (KHash2Size - 1);
                _hash[hash2Value] = Pos;
                temp ^= (uint)BufferBase[cur + 2] << 8;
                var hash3Value = temp & (KHash3Size - 1);
                _hash[KHash3Offset + hash3Value] = Pos;
                hashValue = (temp ^ (Crc.Table[BufferBase[cur + 3]] << 5)) & _hashMask;
            }
            else
            {
                hashValue = BufferBase[cur] ^ ((uint)BufferBase[cur + 1] << 8);
            }

            var curMatch = _hash[_fixHashSize + hashValue];
            _hash[_fixHashSize + hashValue] = Pos;

            var ptr0 = (_cyclicBufferPos << 1) + 1;
            var ptr1 = _cyclicBufferPos << 1;

            uint len1;
            var len0 = len1 = _numHashDirectBytes;

            var count = _cutValue;
            while (true)
            {
                if (curMatch <= matchMinPos || count-- == 0)
                {
                    _son[ptr0] = _son[ptr1] = KEmptyHashValue;
                    break;
                }

                var delta = Pos - curMatch;
                var cyclicPos = (delta <= _cyclicBufferPos ? _cyclicBufferPos - delta : _cyclicBufferPos - delta + _cyclicBufferSize) << 1;

                var pby1 = BufferOffset + curMatch;
                var len = Math.Min(len0, len1);
                if (BufferBase[pby1 + len] == BufferBase[cur + len])
                {
                    while (++len != lenLimit)
                    {
                        if (BufferBase[pby1 + len] != BufferBase[cur + len])
                        {
                            break;
                        }
                    }

                    if (len == lenLimit)
                    {
                        _son[ptr1] = _son[cyclicPos];
                        _son[ptr0] = _son[cyclicPos + 1];
                        break;
                    }
                }

                if (BufferBase[pby1 + len] < BufferBase[cur + len])
                {
                    _son[ptr1] = curMatch;
                    ptr1 = cyclicPos + 1;
                    curMatch = _son[ptr1];
                    len1 = len;
                }
                else
                {
                    _son[ptr0] = curMatch;
                    ptr0 = cyclicPos;
                    curMatch = _son[ptr0];
                    len0 = len;
                }
            }

            MovePos();
        } while (--num != 0);
    }

    private static void NormalizeLinks(uint[] items, uint numItems, uint subValue)
    {
        for (uint i = 0; i < numItems; i++)
        {
            var value = items[i];
            value = value <= subValue ? KEmptyHashValue : value - subValue;
            items[i] = value;
        }
    }

    private void Normalize()
    {
        var subValue = Pos - _cyclicBufferSize;
        NormalizeLinks(_son, _cyclicBufferSize * 2, subValue);
        NormalizeLinks(_hash, _hashSizeSum, subValue);
        ReduceOffsets((int)subValue);
    }

    internal void SetCutValue(uint cutValue)
    {
        _cutValue = cutValue;
    }
}