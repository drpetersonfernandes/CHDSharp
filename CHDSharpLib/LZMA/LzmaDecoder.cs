using CHDSharp.LZMA.LZ;
using CHDSharp.LZMA.RangeCoder;
using CHDSharp.Models.LZMA;

namespace CHDSharp.LZMA;

/// <summary>Core LZMA range-coder decoder, ported from the LZMA SDK. Decodes a raw LZMA stream using adaptive probability models.</summary>
internal class Decoder
{
    private class LenDecoder
    {
        private BitDecoder _mChoice;
        private BitDecoder _mChoice2;
        private readonly BitTreeDecoder[] _mLowCoder = new BitTreeDecoder[Base.KNumPosStatesMax];
        private readonly BitTreeDecoder[] _mMidCoder = new BitTreeDecoder[Base.KNumPosStatesMax];
        private readonly BitTreeDecoder _mHighCoder = new(Base.KNumHighLenBits);
        private uint _mNumPosStates;

        public void Create(uint numPosStates)
        {
            for (var posState = _mNumPosStates; posState < numPosStates; posState++)
            {
                _mLowCoder[posState] = new BitTreeDecoder(Base.KNumLowLenBits);
                _mMidCoder[posState] = new BitTreeDecoder(Base.KNumMidLenBits);
            }

            _mNumPosStates = numPosStates;
        }

        public void Init()
        {
            _mChoice.Init();
            for (uint posState = 0; posState < _mNumPosStates; posState++)
            {
                _mLowCoder[posState].Init();
                _mMidCoder[posState].Init();
            }

            _mChoice2.Init();
            _mHighCoder.Init();
        }

        public uint Decode(RangeCoder.Decoder rangeDecoder, uint posState)
        {
            if (_mChoice.Decode(rangeDecoder) == 0)
            {
                return _mLowCoder[posState].Decode(rangeDecoder);
            }
            else
            {
                var symbol = Base.KNumLowLenSymbols;
                if (_mChoice2.Decode(rangeDecoder) == 0)
                {
                    symbol += _mMidCoder[posState].Decode(rangeDecoder);
                }
                else
                {
                    symbol += Base.KNumMidLenSymbols;
                    symbol += _mHighCoder.Decode(rangeDecoder);
                }

                return symbol;
            }
        }
    }

    private class LiteralDecoder
    {
        private struct Decoder2
        {
            private BitDecoder[] _mDecoders;

            public void Create()
            {
                _mDecoders = new BitDecoder[0x300];
            }

            public readonly void Init()
            {
                for (var i = 0; i < 0x300; i++) _mDecoders[i].Init();
            }

            public byte DecodeNormal(RangeCoder.Decoder rangeDecoder)
            {
                uint symbol = 1;
                do
                {
                    symbol = (symbol << 1) | _mDecoders[symbol].Decode(rangeDecoder);
                } while (symbol < 0x100);

                // ReSharper disable once IntVariableOverflowInUncheckedContext
                return (byte)symbol;
            }

            public byte DecodeWithMatchByte(RangeCoder.Decoder rangeDecoder, byte matchByte)
            {
                uint symbol = 1;
                do
                {
                    var matchBit = (uint)(matchByte >> 7) & 1;
                    matchByte <<= 1;
                    var bit = _mDecoders[((1 + matchBit) << 8) + symbol].Decode(rangeDecoder);
                    symbol = (symbol << 1) | bit;
                    if (matchBit != bit)
                    {
                        while (symbol < 0x100)
                        {
                            symbol = (symbol << 1) | _mDecoders[symbol].Decode(rangeDecoder);
                        }

                        break;
                    }
                } while (symbol < 0x100);

                // ReSharper disable once IntVariableOverflowInUncheckedContext
                return (byte)symbol;
            }
        }

        private Decoder2[] _mCoders = null!;
        private int _mNumPrevBits;
        private int _mNumPosBits;
        private uint _mPosMask;

        public void Create(int numPosBits, int numPrevBits)
        {
            if (_mNumPrevBits == numPrevBits &&
                _mNumPosBits == numPosBits)
                return;

            _mNumPosBits = numPosBits;
            _mPosMask = ((uint)1 << numPosBits) - 1;
            _mNumPrevBits = numPrevBits;
            var numStates = 1u << (_mNumPrevBits + _mNumPosBits);
            _mCoders = new Decoder2[numStates];
            for (uint i = 0; i < numStates; i++)
                _mCoders[i].Create();
        }

        public void Init()
        {
            var numStates = 1u << (_mNumPrevBits + _mNumPosBits);
            for (uint i = 0; i < numStates; i++)
                _mCoders[i].Init();
        }

        private uint GetState(uint pos, byte prevByte)
        {
            return ((pos & _mPosMask) << _mNumPrevBits) + (uint)(prevByte >> (8 - _mNumPrevBits));
        }

        public byte DecodeNormal(RangeCoder.Decoder rangeDecoder, uint pos, byte prevByte)
        {
            return _mCoders[GetState(pos, prevByte)].DecodeNormal(rangeDecoder);
        }

        public byte DecodeWithMatchByte(RangeCoder.Decoder rangeDecoder, uint pos, byte prevByte, byte matchByte)
        {
            return _mCoders[GetState(pos, prevByte)].DecodeWithMatchByte(rangeDecoder, matchByte);
        }
    }

    private OutWindow? _mOutWindow;

    private readonly BitDecoder[] _mIsMatchDecoders = new BitDecoder[Base.KNumStates << Base.KNumPosStatesBitsMax];
    private readonly BitDecoder[] _mIsRepDecoders = new BitDecoder[Base.KNumStates];
    private readonly BitDecoder[] _mIsRepG0Decoders = new BitDecoder[Base.KNumStates];
    private readonly BitDecoder[] _mIsRepG1Decoders = new BitDecoder[Base.KNumStates];
    private readonly BitDecoder[] _mIsRepG2Decoders = new BitDecoder[Base.KNumStates];
    private readonly BitDecoder[] _mIsRep0LongDecoders = new BitDecoder[Base.KNumStates << Base.KNumPosStatesBitsMax];

    private readonly BitTreeDecoder[] _mPosSlotDecoder = new BitTreeDecoder[Base.KNumLenToPosStates];
    private readonly BitDecoder[] _mPosDecoders = new BitDecoder[Base.KNumFullDistances - Base.KEndPosModelIndex];

    private readonly BitTreeDecoder _mPosAlignDecoder = new(Base.KNumAlignBits);

    private readonly LenDecoder _mLenDecoder = new();
    private readonly LenDecoder _mRepLenDecoder = new();

    private readonly LiteralDecoder _mLiteralDecoder = new();

    private int _mDictionarySize;

    private uint _mPosStateMask;

    private State _state;
    private uint _rep0, _rep1, _rep2, _rep3;

    /// <summary>Initialises a new LZMA decoder with all probability models set to their default states.</summary>
    public Decoder()
    {
        _mDictionarySize = -1;
        for (var i = 0; i < Base.KNumLenToPosStates; i++)
        {
            _mPosSlotDecoder[i] = new BitTreeDecoder(Base.KNumPosSlotBits);
        }
    }

    private void CreateDictionary()
    {
        if (_mDictionarySize < 0)
            throw new InvalidParamException();

        _mOutWindow = new OutWindow();
        var blockSize = Math.Max(_mDictionarySize, (1 << 12));
        _mOutWindow.Create(blockSize);
    }

    private void SetLiteralProperties(int lp, int lc)
    {
        if (lp > 8 || lc > 8)
            throw new InvalidParamException();

        _mLiteralDecoder.Create(lp, lc);
    }

    private void SetPosBitsProperties(int pb)
    {
        if (pb > Base.KNumPosStatesBitsMax)
            throw new InvalidParamException();

        var numPosStates = 1u << pb;
        _mLenDecoder.Create(numPosStates);
        _mRepLenDecoder.Create(numPosStates);
        _mPosStateMask = numPosStates - 1;
    }

    private void Init()
    {
        uint i;
        for (i = 0; i < Base.KNumStates; i++)
        {
            for (uint j = 0; j <= _mPosStateMask; j++)
            {
                var index = (i << Base.KNumPosStatesBitsMax) + j;
                _mIsMatchDecoders[index].Init();
                _mIsRep0LongDecoders[index].Init();
            }

            _mIsRepDecoders[i].Init();
            _mIsRepG0Decoders[i].Init();
            _mIsRepG1Decoders[i].Init();
            _mIsRepG2Decoders[i].Init();
        }

        _mLiteralDecoder.Init();
        for (i = 0; i < Base.KNumLenToPosStates; i++)
            _mPosSlotDecoder[i].Init();
        for (i = 0; i < Base.KNumFullDistances - Base.KEndPosModelIndex; i++)
            _mPosDecoders[i].Init();

        _mLenDecoder.Init();
        _mRepLenDecoder.Init();
        _mPosAlignDecoder.Init();

        _state.Init();
        _rep0 = 0;
        _rep1 = 0;
        _rep2 = 0;
        _rep3 = 0;
    }

    /// <summary>Decodes LZMA-compressed data from the range decoder into the output window.</summary>
    internal bool Code(int dictionarySize, OutWindow outWindow, RangeCoder.Decoder rangeDecoder)
    {
        var dictionarySizeCheck = Math.Max(dictionarySize, 1);

        outWindow.CopyPending();

        while (outWindow.HasSpace)
        {
            var posState = (uint)outWindow.Total & _mPosStateMask;
            if (_mIsMatchDecoders[(_state.Index << Base.KNumPosStatesBitsMax) + posState].Decode(rangeDecoder) == 0)
            {
                byte b;
                var prevByte = outWindow.GetByte(0);
                if (!_state.IsCharState())
                {
                    b = _mLiteralDecoder.DecodeWithMatchByte(rangeDecoder,
                        (uint)outWindow.Total, prevByte, outWindow.GetByte((int)_rep0));
                }
                else
                {
                    b = _mLiteralDecoder.DecodeNormal(rangeDecoder, (uint)outWindow.Total, prevByte);
                }

                outWindow.PutByte(b);
                _state.UpdateChar();
            }
            else
            {
                uint len;
                if (_mIsRepDecoders[_state.Index].Decode(rangeDecoder) == 1)
                {
                    if (_mIsRepG0Decoders[_state.Index].Decode(rangeDecoder) == 0)
                    {
                        if (_mIsRep0LongDecoders[(_state.Index << Base.KNumPosStatesBitsMax) + posState].Decode(rangeDecoder) == 0)
                        {
                            _state.UpdateShortRep();
                            outWindow.PutByte(outWindow.GetByte((int)_rep0));
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (_mIsRepG1Decoders[_state.Index].Decode(rangeDecoder) == 0)
                        {
                            distance = _rep1;
                        }
                        else
                        {
                            if (_mIsRepG2Decoders[_state.Index].Decode(rangeDecoder) == 0)
                            {
                                distance = _rep2;
                            }
                            else
                            {
                                distance = _rep3;
                                _rep3 = _rep2;
                            }

                            _rep2 = _rep1;
                        }

                        _rep1 = _rep0;
                        _rep0 = distance;
                    }

                    len = _mRepLenDecoder.Decode(rangeDecoder, posState) + Base.KMatchMinLen;
                    _state.UpdateRep();
                }
                else
                {
                    _rep3 = _rep2;
                    _rep2 = _rep1;
                    _rep1 = _rep0;
                    len = Base.KMatchMinLen + _mLenDecoder.Decode(rangeDecoder, posState);
                    _state.UpdateMatch();
                    var posSlot = _mPosSlotDecoder[Base.GetLenToPosState(len)].Decode(rangeDecoder);
                    if (posSlot >= Base.KStartPosModelIndex)
                    {
                        var numDirectBits = (int)((posSlot >> 1) - 1);
                        _rep0 = ((2 | (posSlot & 1)) << numDirectBits);
                        if (posSlot < Base.KEndPosModelIndex)
                        {
                            _rep0 += BitTreeDecoder.ReverseDecode(_mPosDecoders,
                                _rep0 - posSlot - 1, rangeDecoder, numDirectBits);
                        }
                        else
                        {
                            _rep0 += (rangeDecoder.DecodeDirectBits(
                                numDirectBits - Base.KNumAlignBits) << Base.KNumAlignBits);
                            _rep0 += _mPosAlignDecoder.ReverseDecode(rangeDecoder);
                        }
                    }
                    else
                    {
                        _rep0 = posSlot;
                    }
                }

                if (_rep0 >= outWindow.Total || _rep0 >= dictionarySizeCheck)
                {
                    if (_rep0 == 0xFFFFFFFF)
                        return true;

                    throw new DataErrorException();
                }

                outWindow.CopyBlock((int)_rep0, (int)len);
            }
        }

        return false;
    }

    /// <summary>Sets the LZMA decoder properties (lc, lp, pb) and optional dictionary size from a property byte array.</summary>
    public void SetDecoderProperties(byte[] properties)
    {
        if (properties.Length < 1)
            throw new InvalidParamException();

        var lc = properties[0] % 9;
        var remainder = properties[0] / 9;
        var lp = remainder % 5;
        var pb = remainder / 5;
        if (pb > Base.KNumPosStatesBitsMax)
            throw new InvalidParamException();

        SetLiteralProperties(lp, lc);
        SetPosBitsProperties(pb);
        Init();
        if (properties.Length >= 5)
        {
            _mDictionarySize = 0;
            for (var i = 0; i < 4; i++)
            {
                _mDictionarySize += properties[1 + i] << (i * 8);
            }
        }
    }
}