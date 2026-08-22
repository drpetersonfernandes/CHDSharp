using CHDSharp.LZMA;
using CHDSharp.Models.LZMA;
using CHDSharpEncoder.LZMA.LZ;
using CHDSharpEncoder.LZMA.RangeCoder;
using RangeEncoder = CHDSharpEncoder.LZMA.RangeCoder.Encoder;

namespace CHDSharpEncoder.LZMA;

/// <summary>
/// LZMA encoder ported from Igor Pavlov's official LZMA SDK C# source (public domain),
/// with the optimal-parser (GetOptimum/Backward) and price-calculation machinery upgraded
/// to match the 18.06+ C encoder used by MAME's chdman (the "opt-extra" chain mechanism,
/// the kCyclesBits price table and the matchPriceCount/repLenEncCounter price-update
/// triggers). Produces the raw headerless LZMA stream used by CHD: the caller configures
/// lc/lp/pb and dictionary size via <see cref="SetCoderProperties"/> and must NOT write
/// the 5-byte property header to the output.
/// </summary>
internal class Encoder : ICoder, ISetCoderProperties, IWriteCoderProperties
{
    private enum EMatchFinderType
    {
        Bt2,
        Bt4
    }

    private const uint KIfinityPrice = 1 << 30;
    private const uint KNumOpts = 1 << 11;
    private const int KRepLenCount = 64;
    private const uint KMarkLit = 0xFFFFFFFF;
    private const uint KStateStart = 0;
    private const uint KStateLitAfterMatch = 4;
    private const uint KStateLitAfterRep = 5;
    private const uint KStateMatchAfterLit = 7;
    private const uint KStateRepAfterLit = 8;

    private static readonly byte[] GFastPos = BuildFastPos();

    private static byte[] BuildFastPos()
    {
        const byte kFastSlots = 22;
        var fastPos = new byte[1 << 11];
        var c = 2;
        fastPos[0] = 0;
        fastPos[1] = 1;
        for (byte slotFast = 2; slotFast < kFastSlots; slotFast++)
        {
            var k = (uint)1 << ((slotFast >> 1) - 1);
            for (uint j = 0; j < k; j++, c++)
            {
                fastPos[c] = slotFast;
            }
        }

        return fastPos;
    }

    private static uint GetPosSlot(uint pos)
    {
        switch (pos)
        {
            case < 1 << 11:
                return GFastPos[pos];
            case < 1 << 21:
                return (uint)(GFastPos[pos >> 10] + 20);
            default:
                return (uint)(GFastPos[pos >> 20] + 40);
        }
    }

    private static uint GetPosSlot2(uint pos)
    {
        switch (pos)
        {
            case < 1 << 17:
                return (uint)(GFastPos[pos >> 6] + 12);
            case < 1 << 27:
                return (uint)(GFastPos[pos >> 16] + 32);
            default:
                return (uint)(GFastPos[pos >> 26] + 52);
        }
    }

    private static bool IsLitState(uint state)
    {
        return state < 7;
    }

    private static uint NextStateChar(uint state)
    {
        var s = new State { Index = state };
        s.UpdateChar();
        return s.Index;
    }

    private static uint NextStateMatch(uint state)
    {
        var s = new State { Index = state };
        s.UpdateMatch();
        return s.Index;
    }

    private static uint NextStateRep(uint state)
    {
        var s = new State { Index = state };
        s.UpdateRep();
        return s.Index;
    }

    private static uint NextStateShortRep(uint state)
    {
        var s = new State { Index = state };
        s.UpdateShortRep();
        return s.Index;
    }

    private static uint GetLenToPosState2(uint len)
    {
        return len < Base.KNumLenToPosStates - 1 ? len : Base.KNumLenToPosStates - 1;
    }

    private State _state;
    private byte _previousByte;
    private readonly uint[] _reps = new uint[Base.KNumRepDistances];

    private void BaseInit()
    {
        _state.Init();
        _previousByte = 0;
        // 1-based distances: 1 = the byte immediately before the current one
        for (uint i = 0; i < Base.KNumRepDistances; i++)
        {
            _reps[i] = 1;
        }
    }

    private const int KDefaultDictionaryLogSize = 22;
    private const uint KNumFastBytesDefault = 0x20;

    private class LiteralEncoder
    {
        internal struct Encoder2
        {
            private BitEncoder[] _encoders;

            internal void Create()
            {
                _encoders = new BitEncoder[0x300];
            }

            internal readonly void Init()
            {
                for (var i = 0; i < 0x300; i++)
                {
                    _encoders[i].Init();
                }
            }

            internal readonly void Encode(RangeEncoder rangeEncoder, byte symbol)
            {
                uint context = 1;
                for (var i = 7; i >= 0; i--)
                {
                    var bit = (uint)((symbol >> i) & 1);
                    _encoders[context].Encode(rangeEncoder, bit);
                    context = (context << 1) | bit;
                }
            }

            internal readonly void EncodeMatched(RangeEncoder rangeEncoder, byte matchByte, byte symbol)
            {
                uint context = 1;
                var same = true;
                for (var i = 7; i >= 0; i--)
                {
                    var bit = (uint)((symbol >> i) & 1);
                    var state = context;
                    if (same)
                    {
                        var matchBit = (uint)((matchByte >> i) & 1);
                        state += (1 + matchBit) << 8;
                        same = matchBit == bit;
                    }

                    _encoders[state].Encode(rangeEncoder, bit);
                    context = (context << 1) | bit;
                }
            }

            internal uint GetPrice(bool matchMode, byte matchByte, byte symbol)
            {
                uint price = 0;
                uint context = 1;
                var i = 7;
                if (matchMode)
                {
                    for (; i >= 0; i--)
                    {
                        var matchBit = (uint)(matchByte >> i) & 1;
                        var bit = (uint)(symbol >> i) & 1;
                        price += _encoders[((1 + matchBit) << 8) + context].GetPrice(bit);
                        context = (context << 1) | bit;
                        if (matchBit != bit)
                        {
                            i--;
                            break;
                        }
                    }
                }

                for (; i >= 0; i--)
                {
                    var bit = (uint)(symbol >> i) & 1;
                    price += _encoders[context].GetPrice(bit);
                    context = (context << 1) | bit;
                }

                return price;
            }
        }

        private Encoder2[] _coders = [];
        private int _numPrevBits;
        private int _numPosBits;
        private uint _posMask;

        internal void Create(int numPosBits, int numPrevBits)
        {
            if (_coders.Length != 0 && _numPrevBits == numPrevBits && _numPosBits == numPosBits)
            {
                return;
            }

            _numPosBits = numPosBits;
            _posMask = ((uint)1 << numPosBits) - 1;
            _numPrevBits = numPrevBits;
            var numStates = (uint)1 << (_numPrevBits + _numPosBits);
            _coders = new Encoder2[numStates];
            for (uint i = 0; i < numStates; i++)
            {
                _coders[i].Create();
            }
        }

        internal void Init()
        {
            var numStates = (uint)1 << (_numPrevBits + _numPosBits);
            for (uint i = 0; i < numStates; i++)
            {
                _coders[i].Init();
            }
        }

        internal Encoder2 GetSubCoder(uint pos, byte prevByte)
        {
            return _coders[((pos & _posMask) << _numPrevBits) + (uint)(prevByte >> (8 - _numPrevBits))];
        }
    }

    private class LenEncoder
    {
        private BitEncoder _choice;
        private BitEncoder _choice2;
        private readonly BitTreeEncoder[] _lowCoder = new BitTreeEncoder[Base.KNumPosStatesEncodingMax];
        private readonly BitTreeEncoder[] _midCoder = new BitTreeEncoder[Base.KNumPosStatesEncodingMax];
        private readonly BitTreeEncoder _highCoder = new(Base.KNumHighLenBits);

        internal LenEncoder()
        {
            for (uint posState = 0; posState < Base.KNumPosStatesEncodingMax; posState++)
            {
                _lowCoder[posState] = new BitTreeEncoder(Base.KNumLowLenBits);
                _midCoder[posState] = new BitTreeEncoder(Base.KNumMidLenBits);
            }
        }

        internal void Init(uint numPosStates)
        {
            _choice.Init();
            _choice2.Init();
            for (uint posState = 0; posState < numPosStates; posState++)
            {
                _lowCoder[posState].Init();
                _midCoder[posState].Init();
            }

            _highCoder.Init();
        }

        internal void Encode(RangeEncoder rangeEncoder, uint symbol, uint posState)
        {
            if (symbol < Base.KNumLowLenSymbols)
            {
                _choice.Encode(rangeEncoder, 0);
                _lowCoder[posState].Encode(rangeEncoder, symbol);
            }
            else
            {
                symbol -= Base.KNumLowLenSymbols;
                _choice.Encode(rangeEncoder, 1);
                if (symbol < Base.KNumMidLenSymbols)
                {
                    _choice2.Encode(rangeEncoder, 0);
                    _midCoder[posState].Encode(rangeEncoder, symbol);
                }
                else
                {
                    _choice2.Encode(rangeEncoder, 1);
                    _highCoder.Encode(rangeEncoder, symbol - Base.KNumMidLenSymbols);
                }
            }
        }

        internal void SetPrices(uint posState, uint numSymbols, uint[] prices, uint st)
        {
            var a0 = _choice.GetPrice0();
            var a1 = _choice.GetPrice1();
            var b0 = a1 + _choice2.GetPrice0();
            var b1 = a1 + _choice2.GetPrice1();
            uint i;
            for (i = 0; i < Base.KNumLowLenSymbols; i++)
            {
                if (i >= numSymbols)
                {
                    return;
                }

                prices[st + i] = a0 + _lowCoder[posState].GetPrice(i);
            }

            for (; i < Base.KNumLowLenSymbols + Base.KNumMidLenSymbols; i++)
            {
                if (i >= numSymbols)
                {
                    return;
                }

                prices[st + i] = b0 + _midCoder[posState].GetPrice(i - Base.KNumLowLenSymbols);
            }

            for (; i < numSymbols; i++)
            {
                prices[st + i] = b1 + _highCoder.GetPrice(i - Base.KNumLowLenSymbols - Base.KNumMidLenSymbols);
            }
        }
    }

    private const uint KNumLenSpecSymbols = Base.KNumLowLenSymbols + Base.KNumMidLenSymbols;

    private class LenPriceTableEncoder : LenEncoder
    {
        private readonly uint[] _prices = new uint[Base.KNumLenSymbols << Base.KNumPosStatesBitsEncodingMax];
        private uint _tableSize;
        private readonly uint[] _counters = new uint[Base.KNumPosStatesEncodingMax];

        internal void SetTableSize(uint tableSize)
        {
            _tableSize = tableSize;
        }

        internal uint GetPrice(uint symbol, uint posState)
        {
            return _prices[posState * Base.KNumLenSymbols + symbol];
        }

        private void UpdateTable(uint posState)
        {
            SetPrices(posState, _tableSize, _prices, posState * Base.KNumLenSymbols);
        }

        internal void UpdateTables(uint numPosStates)
        {
            for (uint posState = 0; posState < numPosStates; posState++)
            {
                UpdateTable(posState);
            }
        }
    }

    private class Optimal
    {
        internal uint Price = KIfinityPrice;
        internal State State;
        internal uint Extra;
        internal uint Len;
        internal uint Dist;
        internal uint Backs0;
        internal uint Backs1;
        internal uint Backs2;
        internal uint Backs3;
    }

    private readonly Optimal[] _opt = new Optimal[KNumOpts];
    private uint _optEnd;
    private uint _optCur;
    private uint _numAvail;
    private uint _longestMatchLen;
    private uint _numPairs;
    private uint _additionalOffset;
    private uint _backRes;
    private int _repLenEncCounter;

    private readonly uint[] _optReps = new uint[Base.KNumRepDistances];
    private readonly uint[] _repLens = new uint[Base.KNumRepDistances];

    private IMatchFinder? _matchFinder;
    private readonly RangeEncoder _rangeEncoder = new();

    private readonly BitEncoder[] _isMatch = new BitEncoder[Base.KNumStates << Base.KNumPosStatesBitsMax];
    private readonly BitEncoder[] _isRep = new BitEncoder[Base.KNumStates];
    private readonly BitEncoder[] _isRepG0 = new BitEncoder[Base.KNumStates];
    private readonly BitEncoder[] _isRepG1 = new BitEncoder[Base.KNumStates];
    private readonly BitEncoder[] _isRepG2 = new BitEncoder[Base.KNumStates];
    private readonly BitEncoder[] _isRep0Long = new BitEncoder[Base.KNumStates << Base.KNumPosStatesBitsMax];

    private readonly BitTreeEncoder[] _posSlotEncoder = new BitTreeEncoder[Base.KNumLenToPosStates];

    private readonly BitEncoder[] _posEncoders = new BitEncoder[Base.KNumFullDistances - Base.KEndPosModelIndex];
    private readonly BitTreeEncoder _posAlignEncoder = new(Base.KNumAlignBits);

    private readonly LenPriceTableEncoder _lenEncoder = new();
    private readonly LenPriceTableEncoder _repMatchLenEncoder = new();

    private readonly LiteralEncoder _literalEncoder = new();

    private readonly uint[] _matchDistances = new uint[Base.KMatchMaxLen * 2 + 2];

    private uint _matchPriceCount;

    private uint _numFastBytes = KNumFastBytesDefault;

    private readonly uint[] _posSlotPrices = new uint[1 << (Base.KNumPosSlotBits + Base.KNumLenToPosStatesBits)];
    private readonly uint[] _distancesPrices = new uint[Base.KNumFullDistances << Base.KNumLenToPosStatesBits];
    private readonly uint[] _alignPrices = new uint[Base.KAlignTableSize];

    private uint _distTableSize = KDefaultDictionaryLogSize * 2;

    private int _posStateBits = 2;
    private uint _posStateMask = 4 - 1;
    private int _numLiteralPosStateBits;
    private int _numLiteralContextBits = 3;

    private uint _dictionarySize = 1 << KDefaultDictionaryLogSize;
    private uint _dictionarySizePrev = 0xFFFFFFFF;
    private uint _numFastBytesPrev = 0xFFFFFFFF;

    private long _nowPos64;
    private bool _finished;
    private Stream? _inStream;

    private EMatchFinderType _matchFinderType = EMatchFinderType.Bt4;
    private bool _writeEndMark;

    private bool _needReleaseMfStream;

    private void Create()
    {
        if (_matchFinder == null)
        {
            var bt = new BinTree();
            var numHashBytes = 4;
            if (_matchFinderType == EMatchFinderType.Bt2)
            {
                numHashBytes = 2;
            }

            bt.SetType(numHashBytes);
            _matchFinder = bt;
        }

        _literalEncoder.Create(_numLiteralPosStateBits, _numLiteralContextBits);

        if (_dictionarySize == _dictionarySizePrev && _numFastBytesPrev == _numFastBytes)
        {
            return;
        }

        _matchFinder.Create(_dictionarySize, KNumOpts, _numFastBytes, Base.KMatchMaxLen + 1);
        _dictionarySizePrev = _dictionarySize;
        _numFastBytesPrev = _numFastBytes;
    }

    internal Encoder()
    {
        for (var i = 0; i < KNumOpts; i++)
        {
            _opt[i] = new Optimal();
        }

        for (var i = 0; i < Base.KNumLenToPosStates; i++)
        {
            _posSlotEncoder[i] = new BitTreeEncoder(Base.KNumPosSlotBits);
        }
    }

    private void SetWriteEndMarkerMode(bool writeEndMarker)
    {
        _writeEndMark = writeEndMarker;
    }

    private void Init()
    {
        BaseInit();
        _rangeEncoder.Init();

        uint i;
        for (i = 0; i < Base.KNumStates; i++)
        {
            for (uint j = 0; j <= _posStateMask; j++)
            {
                var complexState = (i << Base.KNumPosStatesBitsMax) + j;
                _isMatch[complexState].Init();
                _isRep0Long[complexState].Init();
            }

            _isRep[i].Init();
            _isRepG0[i].Init();
            _isRepG1[i].Init();
            _isRepG2[i].Init();
        }

        _literalEncoder.Init();
        for (i = 0; i < Base.KNumLenToPosStates; i++)
        {
            _posSlotEncoder[i].Init();
        }

        for (i = 0; i < Base.KNumFullDistances - Base.KEndPosModelIndex; i++)
        {
            _posEncoders[i].Init();
        }

        _lenEncoder.Init((uint)1 << _posStateBits);
        _repMatchLenEncoder.Init((uint)1 << _posStateBits);

        _posAlignEncoder.Init();

        _optEnd = 0;
        _optCur = 0;
        for (i = 0; i < KNumOpts; i++)
        {
            _opt[i].Price = KIfinityPrice;
        }

        _additionalOffset = 0;
    }

    private void InitPrices()
    {
        FillDistancesPrices();
        FillAlignPrices();

        _lenEncoder.SetTableSize(_numFastBytes + 1 - Base.KMatchMinLen);
        _repMatchLenEncoder.SetTableSize(_numFastBytes + 1 - Base.KMatchMinLen);

        _repLenEncCounter = KRepLenCount;

        _lenEncoder.UpdateTables((uint)1 << _posStateBits);
        _repMatchLenEncoder.UpdateTables((uint)1 << _posStateBits);
    }

    private uint ReadMatchDistances(out uint numPairs)
    {
        _additionalOffset++;
        _numAvail = _matchFinder!.GetNumAvailableBytes();
        numPairs = _matchFinder.GetMatches(_matchDistances);

        if (numPairs == 0)
        {
            return 0;
        }

        var len = _matchDistances[numPairs - 2];
        if (len != _numFastBytes)
        {
            return len;
        }

        var numAvail = _numAvail;
        if (numAvail > Base.KMatchMaxLen)
        {
            numAvail = Base.KMatchMaxLen;
        }

        var dist = _matchDistances[numPairs - 1];
        var m = len;
        while (m < numAvail &&
               _matchFinder.GetIndexByte((int)m - 1) ==
               _matchFinder.GetIndexByte((int)m - 2 - (int)dist))
        {
            m++;
        }

        return m;
    }

    private void MovePos(uint num)
    {
        if (num > 0)
        {
            _matchFinder!.Skip(num);
            _additionalOffset += num;
        }
    }

    private uint GetPriceShortRep(uint state, uint posState)
    {
        return _isRepG0[state].GetPrice0() +
               _isRep0Long[(state << Base.KNumPosStatesBitsMax) + posState].GetPrice0();
    }

    private uint GetPriceRep0(uint state, uint posState)
    {
        return _isMatch[(state << Base.KNumPosStatesBitsMax) + posState].GetPrice1() +
               _isRep0Long[(state << Base.KNumPosStatesBitsMax) + posState].GetPrice1() +
               _isRep[state].GetPrice1() +
               _isRepG0[state].GetPrice0();
    }

    private uint GetPricePureRep(uint repIndex, uint state, uint posState)
    {
        uint price;
        var prob = _isRepG0[state];
        if (repIndex == 0)
        {
            price = prob.GetPrice0();
            price += _isRep0Long[(state << Base.KNumPosStatesBitsMax) + posState].GetPrice1();
        }
        else
        {
            price = prob.GetPrice1();
            prob = _isRepG1[state];
            if (repIndex == 1)
            {
                price += prob.GetPrice0();
            }
            else
            {
                price += prob.GetPrice1();
                price += _isRepG2[state].GetPrice(repIndex - 2);
            }
        }

        return price;
    }

    private uint Backward(uint cur)
    {
        var wr = cur + 1;
        _optEnd = wr;

        while (true)
        {
            var dist = _opt[cur].Dist;
            var len = _opt[cur].Len;
            var extra = _opt[cur].Extra;
            cur -= len;

            if (extra != 0)
            {
                wr--;
                _opt[wr].Len = len;
                cur -= extra;
                len = extra;
                if (extra == 1)
                {
                    _opt[wr].Dist = dist;
                    dist = KMarkLit;
                }
                else
                {
                    _opt[wr].Dist = 0;
                    len--;
                    wr--;
                    _opt[wr].Dist = KMarkLit;
                    _opt[wr].Len = 1;
                }
            }

            if (cur == 0)
            {
                _backRes = dist;
                _optCur = wr;
                return len;
            }

            wr--;
            _opt[wr].Dist = dist;
            _opt[wr].Len = len;
        }
    }

    private uint GetOptimum(uint position, out uint backRes)
    {
        uint numPairs, mainLen, i;

        _optCur = _optEnd = 0;

        if (_additionalOffset == 0)
        {
            mainLen = ReadMatchDistances(out numPairs);
        }
        else
        {
            mainLen = _longestMatchLen;
            numPairs = _numPairs;
        }

        var numAvail = _numAvail;
        switch (numAvail)
        {
            case < 2:
                backRes = KMarkLit;
                return 1;
            case > Base.KMatchMaxLen:
                numAvail = Base.KMatchMaxLen;
                break;
        }

        uint repMaxIndex = 0;

        for (i = 0; i < Base.KNumRepDistances; i++)
        {
            uint len;
            _optReps[i] = _reps[i];
            if (_matchFinder!.GetIndexByte(-1) != _matchFinder.GetIndexByte(-1 - (int)_optReps[i]) ||
                _matchFinder.GetIndexByte(0) != _matchFinder.GetIndexByte(0 - (int)_optReps[i]))
            {
                _repLens[i] = 0;
                continue;
            }

            for (len = 2; len < numAvail && _matchFinder.GetIndexByte((int)len - 1) == _matchFinder.GetIndexByte((int)len - 1 - (int)_optReps[i]); len++)
            {
            }

            _repLens[i] = len;
            if (len > _repLens[repMaxIndex])
            {
                repMaxIndex = i;
            }

            if (len == Base.KMatchMaxLen) // 21.03 : optimization
            {
                break;
            }
        }

        if (_repLens[repMaxIndex] >= _numFastBytes)
        {
            var len = _repLens[repMaxIndex];
            backRes = repMaxIndex;
            MovePos(len - 1);
            return len;
        }

        if (mainLen >= _numFastBytes)
        {
            backRes = _matchDistances[numPairs - 1] + Base.KNumRepDistances;
            MovePos(mainLen - 1);
            return mainLen;
        }

        var curByte = _matchFinder!.GetIndexByte(-1);
        var matchByte = _matchFinder.GetIndexByte(-1 - (int)_optReps[0]);

        var last = _repLens[repMaxIndex];
        if (last <= mainLen)
        {
            last = mainLen;
        }

        if (last < 2 && curByte != matchByte)
        {
            backRes = KMarkLit;
            return 1;
        }

        _opt[0].State = _state;

        var posState = position & _posStateMask;

        _opt[1].Price = _isMatch[(_state.Index << Base.KNumPosStatesBitsMax) + posState].GetPrice0() +
                        (!IsLitState(_state.Index) ? _literalEncoder.GetSubCoder(position, _matchFinder.GetIndexByte(-2)).GetPrice(true, matchByte, curByte) : _literalEncoder.GetSubCoder(position, _matchFinder.GetIndexByte(-2)).GetPrice(false, matchByte, curByte));

        _opt[1].Dist = KMarkLit;
        _opt[1].Extra = 0;

        var matchPrice = _isMatch[(_state.Index << Base.KNumPosStatesBitsMax) + posState].GetPrice1();
        var repMatchPrice = matchPrice + _isRep[_state.Index].GetPrice1();

        // 18.06
        if (matchByte == curByte && _repLens[0] == 0)
        {
            var shortRepPrice = repMatchPrice + GetPriceShortRep(_state.Index, posState);
            if (shortRepPrice < _opt[1].Price)
            {
                _opt[1].Price = shortRepPrice;
                _opt[1].Dist = 0;
                _opt[1].Extra = 0;
            }

            if (last < 2)
            {
                backRes = _opt[1].Dist;
                return 1;
            }
        }

        _opt[1].Len = 1;

        _opt[0].Backs0 = _optReps[0];
        _opt[0].Backs1 = _optReps[1];
        _opt[0].Backs2 = _optReps[2];
        _opt[0].Backs3 = _optReps[3];

        // ---------- REP ----------

        for (i = 0; i < Base.KNumRepDistances; i++)
        {
            var repLen = _repLens[i];
            if (repLen < 2)
            {
                continue;
            }

            var price = repMatchPrice + GetPricePureRep(i, _state.Index, posState);
            do
            {
                var price2 = price + _repMatchLenEncoder.GetPrice(repLen - Base.KMatchMinLen, posState);
                var opt = _opt[repLen];
                if (price2 < opt.Price)
                {
                    opt.Price = price2;
                    opt.Len = repLen;
                    opt.Dist = i;
                    opt.Extra = 0;
                }
            } while (--repLen >= 2);
        }

        // ---------- MATCH ----------

        {
            var len = _repLens[0] + 1;
            if (len <= mainLen)
            {
                uint offs = 0;
                var normalMatchPrice = matchPrice + _isRep[_state.Index].GetPrice0();

                if (len < 2)
                {
                    len = 2;
                }
                else
                {
                    while (len > _matchDistances[offs])
                    {
                        offs += 2;
                    }
                }

                for (;; len++)
                {
                    var dist = _matchDistances[offs + 1];
                    var price = normalMatchPrice + _lenEncoder.GetPrice(len - Base.KMatchMinLen, posState);
                    var lenToPosState = Base.GetLenToPosState(len);

                    if (dist < Base.KNumFullDistances)
                    {
                        price += _distancesPrices[lenToPosState * Base.KNumFullDistances + dist];
                    }
                    else
                    {
                        var slot = GetPosSlot2(dist);
                        price += _alignPrices[dist & Base.KAlignMask];
                        price += _posSlotPrices[(lenToPosState << Base.KNumPosSlotBits) + slot];
                    }

                    var opt = _opt[len];

                    if (price < opt.Price)
                    {
                        opt.Price = price;
                        opt.Len = len;
                        opt.Dist = dist + Base.KNumRepDistances;
                        opt.Extra = 0;
                    }

                    if (len == _matchDistances[offs])
                    {
                        offs += 2;
                        if (offs == numPairs)
                        {
                            break;
                        }
                    }
                }
            }
        }

        uint cur = 0;

        // ---------- Optimal Parsing ----------
        while (true)
        {
            uint state;
            uint litPrice, matchPriceLoop;

            cur++;
            if (cur == last)
            {
                break;
            }

            // 18.06
            if (cur >= KNumOpts - 64)
            {
                uint j;
                var price = _opt[cur].Price;
                var best = cur;
                for (j = cur + 1; j <= last; j++)
                {
                    var price2 = _opt[j].Price;
                    if (price >= price2)
                    {
                        price = price2;
                        best = j;
                    }
                }

                var delta = best - cur;
                if (delta != 0)
                {
                    MovePos(delta);
                }

                cur = best;
                break;
            }

            var newLen = ReadMatchDistances(out numPairs);

            if (newLen >= _numFastBytes)
            {
                _numPairs = numPairs;
                _longestMatchLen = newLen;
                break;
            }

            var curOpt = _opt[cur];

            position++;

            var prev = cur - curOpt.Len;

            if (curOpt.Len == 1)
            {
                state = _opt[prev].State.Index;
                state = IsShortRep(curOpt) ? NextStateShortRep(state) : NextStateChar(state);
            }
            else
            {
                var dist = curOpt.Dist;

                if (curOpt.Extra != 0)
                {
                    prev -= curOpt.Extra;
                    state = KStateRepAfterLit;
                    if (curOpt.Extra == 1)
                    {
                        state = dist < Base.KNumRepDistances ? KStateRepAfterLit : KStateMatchAfterLit;
                    }
                }
                else
                {
                    state = _opt[prev].State.Index;
                    if (dist < Base.KNumRepDistances)
                    {
                        state = NextStateRep(state);
                    }
                    else
                    {
                        state = NextStateMatch(state);
                    }
                }

                var prevOpt = _opt[prev];
                var b0 = prevOpt.Backs0;

                if (dist < Base.KNumRepDistances)
                {
                    if (dist == 0)
                    {
                        _optReps[0] = b0;
                        _optReps[1] = prevOpt.Backs1;
                        _optReps[2] = prevOpt.Backs2;
                        _optReps[3] = prevOpt.Backs3;
                    }
                    else
                    {
                        _optReps[1] = b0;
                        b0 = prevOpt.Backs1;
                        if (dist == 1)
                        {
                            _optReps[0] = b0;
                            _optReps[2] = prevOpt.Backs2;
                            _optReps[3] = prevOpt.Backs3;
                        }
                        else
                        {
                            _optReps[2] = b0;
                            _optReps[0] = dist == 2 ? prevOpt.Backs2 : prevOpt.Backs3;
                            _optReps[3] = dist == 2 ? prevOpt.Backs3 : prevOpt.Backs2;
                        }
                    }
                }
                else
                {
                    _optReps[0] = dist - Base.KNumRepDistances + 1;
                    _optReps[1] = b0;
                    _optReps[2] = prevOpt.Backs1;
                    _optReps[3] = prevOpt.Backs2;
                }
            }

            curOpt.State = new State { Index = state };
            curOpt.Backs0 = _optReps[0];
            curOpt.Backs1 = _optReps[1];
            curOpt.Backs2 = _optReps[2];
            curOpt.Backs3 = _optReps[3];

            curByte = _matchFinder!.GetIndexByte(-1);
            matchByte = _matchFinder.GetIndexByte(-1 - (int)_optReps[0]);

            var posStateLoop = position & _posStateMask;

            {
                var curPrice = curOpt.Price;
                matchPriceLoop = curPrice + _isMatch[(state << Base.KNumPosStatesBitsMax) + posStateLoop].GetPrice1();
                litPrice = curPrice + _isMatch[(state << Base.KNumPosStatesBitsMax) + posStateLoop].GetPrice0();
            }

            var nextOpt = _opt[cur + 1];
            var nextIsLit = false;

            // 18.new.06
            if ((nextOpt.Price < KIfinityPrice
                 && matchByte == curByte)
                || litPrice > nextOpt.Price)
            {
                litPrice = 0;
            }
            else
            {
                var subCoder = _literalEncoder.GetSubCoder(position, _matchFinder.GetIndexByte(-2));
                litPrice += !IsLitState(state)
                    ? subCoder.GetPrice(true, matchByte, curByte)
                    : subCoder.GetPrice(false, matchByte, curByte);

                if (litPrice < nextOpt.Price)
                {
                    nextOpt.Price = litPrice;
                    nextOpt.Len = 1;
                    nextOpt.Dist = KMarkLit;
                    nextOpt.Extra = 0;
                    nextIsLit = true;
                }
            }

            var repMatchPriceLoop = matchPriceLoop + _isRep[state].GetPrice1();

            var numAvailFull = _numAvail;
            {
                var temp = KNumOpts - 1 - cur;
                if (numAvailFull > temp)
                {
                    numAvailFull = temp;
                }
            }

            // 18.06
            // ---------- SHORT_REP ----------
            if (IsLitState(state)) // 18.new
                if (matchByte == curByte)
                    if (repMatchPriceLoop < nextOpt.Price) // 18.new
                        if (
                            nextOpt.Len < 2
                            || nextOpt.Dist != 0
                        )
                        {
                            var shortRepPrice = repMatchPriceLoop + GetPriceShortRep(state, posStateLoop);
                            if (shortRepPrice < nextOpt.Price) // 18.new
                            {
                                nextOpt.Price = shortRepPrice;
                                nextOpt.Len = 1;
                                nextOpt.Dist = 0;
                                nextOpt.Extra = 0;
                                nextIsLit = false;
                            }
                        }

            if (numAvailFull < 2)
            {
                continue;
            }

            numAvail = numAvailFull <= _numFastBytes ? numAvailFull : _numFastBytes;

            // ---------- LIT : REP_0 ----------

            if (!nextIsLit
                && litPrice != 0 // 18.new
                && matchByte != curByte
                && numAvailFull > 2)
            {
                var rep0 = (int)_optReps[0];
                if (_matchFinder.GetIndexByte(0) == _matchFinder.GetIndexByte(0 - rep0) &&
                    _matchFinder.GetIndexByte(1) == _matchFinder.GetIndexByte(1 - rep0))
                {
                    uint len;
                    var limit = _numFastBytes + 1;
                    if (limit > numAvailFull)
                    {
                        limit = numAvailFull;
                    }

                    for (len = 3;
                         len < limit &&
                         _matchFinder.GetIndexByte((int)len - 1) == _matchFinder.GetIndexByte((int)len - 1 - rep0);
                         len++)
                    {
                    }

                    {
                        var state2 = NextStateChar(state);
                        var posState2 = (position + 1) & _posStateMask;
                        var price = litPrice + GetPriceRep0(state2, posState2);
                        var offset = cur + len;

                        if (last < offset)
                        {
                            last = offset;
                        }

                        len--;
                        var price2 = price + _repMatchLenEncoder.GetPrice(len - Base.KMatchMinLen, posState2);
                        var opt = _opt[offset];
                        if (price2 < opt.Price)
                        {
                            opt.Price = price2;
                            opt.Len = len;
                            opt.Dist = 0;
                            opt.Extra = 1;
                        }
                    }
                }
            }

            uint startLen = 2 /* speed optimization */;

            {
                // ---------- REP ----------
                uint repIndex = 0;
                for (; repIndex < Base.KNumRepDistances; repIndex++)
                {
                    uint len;
                    uint price;
                    var repDist = (int)_optReps[repIndex];
                    if (_matchFinder.GetIndexByte(-1) != _matchFinder.GetIndexByte(-1 - repDist) ||
                        _matchFinder.GetIndexByte(0) != _matchFinder.GetIndexByte(0 - repDist))
                    {
                        continue;
                    }

                    for (len = 2; len < numAvail && _matchFinder.GetIndexByte((int)len - 1) == _matchFinder.GetIndexByte((int)len - 1 - repDist); len++)
                    {
                    }

                    {
                        var offset = cur + len;
                        if (last < offset)
                        {
                            last = offset;
                        }
                    }

                    {
                        var len2 = len;
                        price = repMatchPriceLoop + GetPricePureRep(repIndex, state, posStateLoop);
                        do
                        {
                            var price2 = price + _repMatchLenEncoder.GetPrice(len2 - Base.KMatchMinLen, posStateLoop);
                            var opt = _opt[cur + len2];
                            if (price2 < opt.Price)
                            {
                                opt.Price = price2;
                                opt.Len = len2;
                                opt.Dist = repIndex;
                                opt.Extra = 0;
                            }
                        } while (--len2 >= 2);
                    }

                    if (repIndex == 0)
                    {
                        startLen = len + 1;
                    }

                    /* if (_maxMode) */
                    {
                        // ---------- REP : LIT : REP_0 ----------
                        // numFastBytes + 1 + numFastBytes

                        var len2 = len + 1;
                        var limit = len2 + _numFastBytes;
                        if (limit > numAvailFull)
                        {
                            limit = numAvailFull;
                        }

                        len2 += 2;
                        if (len2 <= limit)
                            if (_matchFinder.GetIndexByte((int)len2 - 3) == _matchFinder.GetIndexByte((int)len2 - 3 - repDist))
                                if (_matchFinder.GetIndexByte((int)len2 - 2) == _matchFinder.GetIndexByte((int)len2 - 2 - repDist))
                                {
                                    var state2 = NextStateRep(state);
                                    var posState2 = (position + len) & _posStateMask;
                                    price += _repMatchLenEncoder.GetPrice(len - Base.KMatchMinLen, posStateLoop)
                                             + _isMatch[(state2 << Base.KNumPosStatesBitsMax) + posState2].GetPrice0()
                                             + _literalEncoder.GetSubCoder(position + len, _matchFinder.GetIndexByte((int)len - 2)).GetPrice(true,
                                                 _matchFinder.GetIndexByte((int)len - 1 - repDist),
                                                 _matchFinder.GetIndexByte((int)len - 1));

                                    state2 = KStateLitAfterRep;
                                    posState2 = (posState2 + 1) & _posStateMask;

                                    price += GetPriceRep0(state2, posState2);

                                    while (len2 < limit &&
                                           _matchFinder.GetIndexByte((int)len2 - 1) == _matchFinder.GetIndexByte((int)len2 - 1 - repDist))
                                    {
                                        len2++;
                                    }

                                    len2 -= len;

                                    {
                                        var offset = cur + len + len2;

                                        if (last < offset)
                                        {
                                            last = offset;
                                        }

                                        len2--;
                                        var price2 = price + _repMatchLenEncoder.GetPrice(len2 - Base.KMatchMinLen, posState2);
                                        var opt = _opt[offset];
                                        if (price2 < opt.Price)
                                        {
                                            opt.Price = price2;
                                            opt.Len = len2;
                                            opt.Extra = len + 1;
                                            opt.Dist = repIndex;
                                        }
                                    }
                                }
                    }
                }
            }

            // ---------- MATCH ----------
            if (newLen > numAvail)
            {
                newLen = numAvail;
                for (numPairs = 0; newLen > _matchDistances[numPairs]; numPairs += 2)
                {
                }

                _matchDistances[numPairs] = newLen;
                numPairs += 2;
            }

            if (newLen >= startLen)
            {
                var normalMatchPrice = matchPriceLoop + _isRep[state].GetPrice0();
                uint len;

                {
                    var offset = cur + newLen;
                    if (last < offset)
                    {
                        last = offset;
                    }
                }

                uint offs = 0;
                while (startLen > _matchDistances[offs])
                {
                    offs += 2;
                }

                var dist = _matchDistances[offs + 1];
                var posSlot = GetPosSlot2(dist);

                for (len = startLen;; len++)
                {
                    var price = normalMatchPrice + _lenEncoder.GetPrice(len - Base.KMatchMinLen, posStateLoop);
                    {
                        var lenNorm = len - 2;
                        lenNorm = GetLenToPosState2(lenNorm);
                        if (dist < Base.KNumFullDistances)
                        {
                            price += _distancesPrices[lenNorm * Base.KNumFullDistances + dist];
                        }
                        else
                        {
                            price += _posSlotPrices[(lenNorm << Base.KNumPosSlotBits) + posSlot] + _alignPrices[dist & Base.KAlignMask];
                        }

                        var opt = _opt[cur + len];
                        if (price < opt.Price)
                        {
                            opt.Price = price;
                            opt.Len = len;
                            opt.Dist = dist + Base.KNumRepDistances;
                            opt.Extra = 0;
                        }
                    }

                    if (len == _matchDistances[offs])
                    {
                        // MATCH : LIT : REP_0
                        var distInt = (int)dist;
                        var len2 = len + 1;
                        var limit = len2 + _numFastBytes;
                        if (limit > numAvailFull)
                        {
                            limit = numAvailFull;
                        }

                        len2 += 2;
                        if (len2 <= limit)
                            if (_matchFinder.GetIndexByte((int)len2 - 3 - distInt - 1) == _matchFinder.GetIndexByte((int)len2 - 3))
                                if (_matchFinder.GetIndexByte((int)len2 - 2 - distInt - 1) == _matchFinder.GetIndexByte((int)len2 - 2))
                                {
                                    while (len2 < limit &&
                                           _matchFinder.GetIndexByte((int)len2 - 1 - distInt - 1) == _matchFinder.GetIndexByte((int)len2 - 1))
                                    {
                                        len2++;
                                    }

                                    len2 -= len;

                                    {
                                        var state2 = NextStateMatch(state);
                                        var posState2 = (position + len) & _posStateMask;
                                        price += _isMatch[(state2 << Base.KNumPosStatesBitsMax) + posState2].GetPrice0();
                                        price += _literalEncoder.GetSubCoder(position + len, _matchFinder.GetIndexByte((int)len - 2)).GetPrice(true,
                                            _matchFinder.GetIndexByte((int)len - distInt - 2),
                                            _matchFinder.GetIndexByte((int)len - 1));

                                        state2 = KStateLitAfterMatch;

                                        posState2 = (posState2 + 1) & _posStateMask;
                                        price += GetPriceRep0(state2, posState2);

                                        var offset = cur + len + len2;

                                        if (last < offset)
                                        {
                                            last = offset;
                                        }

                                        len2--;
                                        var price2 = price + _repMatchLenEncoder.GetPrice(len2 - Base.KMatchMinLen, posState2);
                                        var opt = _opt[offset];
                                        if (price2 < opt.Price)
                                        {
                                            opt.Price = price2;
                                            opt.Len = len2;
                                            opt.Extra = len + 1;
                                            opt.Dist = dist + Base.KNumRepDistances;
                                        }
                                    }
                                }

                        offs += 2;
                        if (offs == numPairs)
                        {
                            break;
                        }

                        dist = _matchDistances[offs + 1];
                        posSlot = GetPosSlot2(dist);
                    }
                }
            }
        }

        do
        {
            _opt[last].Price = KIfinityPrice;
        } while (--last != 0);

        var lenRes = Backward(cur);
        backRes = _backRes;
        return lenRes;
    }

    private static bool IsShortRep(Optimal opt)
    {
        return opt.Dist == 0;
    }

    private void WriteEndMarker(uint posState)
    {
        if (!_writeEndMark)
        {
            return;
        }

        _isMatch[(_state.Index << Base.KNumPosStatesBitsMax) + posState].Encode(_rangeEncoder, 1);
        _isRep[_state.Index].Encode(_rangeEncoder, 0);
        _state.UpdateMatch();
        const uint len = Base.KMatchMinLen;
        _lenEncoder.Encode(_rangeEncoder, len - Base.KMatchMinLen, posState);
        const uint posSlot = (1 << Base.KNumPosSlotBits) - 1;
        var lenToPosState = Base.GetLenToPosState(len);
        _posSlotEncoder[lenToPosState].Encode(_rangeEncoder, posSlot);
        const int footerBits = 30;
        const uint posReduced = ((uint)1 << footerBits) - 1;
        _rangeEncoder.EncodeDirectBits(posReduced >> Base.KNumAlignBits, footerBits - Base.KNumAlignBits);
        _posAlignEncoder.ReverseEncode(_rangeEncoder, posReduced & Base.KAlignMask);
    }

    private void Flush(uint nowPos)
    {
        ReleaseMfStream();
        WriteEndMarker(nowPos & _posStateMask);
        _rangeEncoder.FlushData();
        _rangeEncoder.FlushStream();
    }

    internal void CodeOneBlock(out long inSize, out long outSize, out bool finished)
    {
        inSize = 0;
        outSize = 0;
        finished = true;

        if (_inStream != null)
        {
            _matchFinder!.SetStream(_inStream);
            _matchFinder.Init();
            _needReleaseMfStream = true;
            _inStream = null;
            if (_trainSize > 0)
            {
                _matchFinder.Skip(_trainSize);
            }
        }

        if (_finished)
        {
            return;
        }

        _finished = true;

        var nowPos = (uint)_nowPos64;
        var startPos = nowPos;

        if (_nowPos64 == 0)
        {
            if (_matchFinder!.GetNumAvailableBytes() == 0)
            {
                Flush(nowPos);
                return;
            }

            ReadMatchDistances(out _);
            _isMatch[0].Encode(_rangeEncoder, 0);
            var curByte = _matchFinder.GetIndexByte((int)(0 - _additionalOffset));
            _literalEncoder.GetSubCoder(0, _previousByte).Encode(_rangeEncoder, curByte);
            _previousByte = curByte;
            _additionalOffset--;
            nowPos++;
        }

        if (_matchFinder!.GetNumAvailableBytes() != 0)
        {
            while (true)
            {
                uint len;
                if (_optEnd == _optCur)
                {
                    len = GetOptimum(nowPos, out _backRes);
                }
                else
                {
                    var opt = _opt[_optCur];
                    len = opt.Len;
                    _backRes = opt.Dist;
                    _optCur++;
                }

                var posState = nowPos & _posStateMask;
                var complexState = (_state.Index << Base.KNumPosStatesBitsMax) + posState;
                if (_backRes == KMarkLit)
                {
                    _isMatch[complexState].Encode(_rangeEncoder, 0);
                    var curByte = _matchFinder.GetIndexByte((int)(0 - _additionalOffset));
                    var subCoder = _literalEncoder.GetSubCoder(nowPos, _previousByte);
                    if (!IsLitState(_state.Index))
                    {
                        var matchByte = _matchFinder.GetIndexByte((int)(0 - _additionalOffset - (int)_reps[0]));
                        subCoder.EncodeMatched(_rangeEncoder, matchByte, curByte);
                    }
                    else
                    {
                        subCoder.Encode(_rangeEncoder, curByte);
                    }

                    _previousByte = curByte;
                    _state.UpdateChar();
                }
                else
                {
                    var dist = _backRes;
                    _isMatch[complexState].Encode(_rangeEncoder, 1);
                    if (dist < Base.KNumRepDistances)
                    {
                        _isRep[_state.Index].Encode(_rangeEncoder, 1);
                        if (dist == 0)
                        {
                            _isRepG0[_state.Index].Encode(_rangeEncoder, 0);
                            _isRep0Long[complexState].Encode(_rangeEncoder, len == 1 ? 0u : 1u);
                        }
                        else
                        {
                            _isRepG0[_state.Index].Encode(_rangeEncoder, 1);
                            if (dist == 1)
                            {
                                _isRepG1[_state.Index].Encode(_rangeEncoder, 0);
                            }
                            else
                            {
                                _isRepG1[_state.Index].Encode(_rangeEncoder, 1);
                                _isRepG2[_state.Index].Encode(_rangeEncoder, dist - 2);
                            }
                        }

                        if (len == 1)
                        {
                            _state.UpdateShortRep();
                        }
                        else
                        {
                            _repMatchLenEncoder.Encode(_rangeEncoder, len - Base.KMatchMinLen, posState);
                            _state.UpdateRep();
                            _repLenEncCounter--;
                        }

                        if (dist != 0)
                        {
                            var distance = _reps[dist];
                            for (var i = dist; i >= 1; i--)
                            {
                                _reps[i] = _reps[i - 1];
                            }

                            _reps[0] = distance;
                        }
                    }
                    else
                    {
                        _isRep[_state.Index].Encode(_rangeEncoder, 0);
                        _state.UpdateMatch();
                        _lenEncoder.Encode(_rangeEncoder, len - Base.KMatchMinLen, posState);
                        dist -= Base.KNumRepDistances;
                        var posSlot = GetPosSlot(dist);
                        var lenToPosState = Base.GetLenToPosState(len);
                        _posSlotEncoder[lenToPosState].Encode(_rangeEncoder, posSlot);

                        if (posSlot >= Base.KStartPosModelIndex)
                        {
                            var footerBits = (int)((posSlot >> 1) - 1);
                            var baseVal = (2 | (posSlot & 1)) << footerBits;
                            var posReduced = dist - baseVal;

                            if (posSlot < Base.KEndPosModelIndex)
                            {
                                BitTreeEncoder.ReverseEncode(_posEncoders,
                                    baseVal - posSlot - 1, _rangeEncoder, footerBits, posReduced);
                            }
                            else
                            {
                                _rangeEncoder.EncodeDirectBits(posReduced >> Base.KNumAlignBits, footerBits - Base.KNumAlignBits);
                                _posAlignEncoder.ReverseEncode(_rangeEncoder, posReduced & Base.KAlignMask);
                            }
                        }

                        _reps[3] = _reps[2];
                        _reps[2] = _reps[1];
                        _reps[1] = _reps[0];
                        _reps[0] = dist + 1;
                        _matchPriceCount++;
                    }

                    _previousByte = _matchFinder.GetIndexByte((int)(len - 1 - _additionalOffset));
                }

                _additionalOffset -= len;
                nowPos += len;
                if (_additionalOffset == 0)
                {
                    if (_matchPriceCount >= 64)
                    {
                        FillAlignPrices();
                        FillDistancesPrices();
                        _lenEncoder.UpdateTables((uint)1 << _posStateBits);
                    }

                    if (_repLenEncCounter <= 0)
                    {
                        _repLenEncCounter = KRepLenCount;
                        _repMatchLenEncoder.UpdateTables((uint)1 << _posStateBits);
                    }

                    if (_matchFinder.GetNumAvailableBytes() == 0)
                    {
                        break;
                    }

                    var processed = nowPos - startPos;
                    inSize = _nowPos64 + processed;
                    outSize = _rangeEncoder.GetProcessedSizeAdd();
                    if (processed >= 1 << 17)
                    {
                        _nowPos64 += processed;
                        _finished = false;
                        finished = false;
                        return;
                    }
                }
            }
        }

        _nowPos64 += nowPos - startPos;
        Flush(nowPos);
    }

    private void ReleaseMfStream()
    {
        if (_matchFinder != null && _needReleaseMfStream)
        {
            _matchFinder.ReleaseStream();
            _needReleaseMfStream = false;
        }
    }

    private void SetOutStream(Stream outStream)
    {
        _rangeEncoder.SetStream(outStream);
    }

    private void ReleaseOutStream()
    {
        _rangeEncoder.ReleaseStream();
    }

    private void ReleaseStreams()
    {
        ReleaseMfStream();
        ReleaseOutStream();
    }

    private void SetStreams(Stream inStream, Stream outStream)
    {
        _inStream = inStream;
        _finished = false;
        Create();
        SetOutStream(outStream);
        Init();
        InitPrices();

        _nowPos64 = 0;
    }

    public void Code(Stream inStream, Stream outStream,
        long inSize, long outSize, ICodeProgress? progress)
    {
        _needReleaseMfStream = false;
        try
        {
            SetStreams(inStream, outStream);
            while (true)
            {
                CodeOneBlock(out var processedInSize, out var processedOutSize, out var finished);
                if (finished)
                {
                    return;
                }

                progress?.SetProgress(processedInSize, processedOutSize);
            }
        }
        finally
        {
            ReleaseStreams();
        }
    }

    private const int KPropSize = 5;
    private readonly byte[] _properties = new byte[KPropSize];

    public void WriteCoderProperties(Stream outStream)
    {
        _properties[0] = (byte)((_posStateBits * 5 + _numLiteralPosStateBits) * 9 + _numLiteralContextBits);
        for (var i = 0; i < 4; i++)
        {
            _properties[1 + i] = (byte)((_dictionarySize >> (8 * i)) & 0xFF);
        }

        outStream.Write(_properties, 0, KPropSize);
    }

    private readonly uint[] _tempPrices = new uint[Base.KNumFullDistances];

    private void FillDistancesPrices()
    {
        for (var i = Base.KStartPosModelIndex; i < Base.KNumFullDistances; i++)
        {
            var posSlot = GetPosSlot(i);
            var footerBits = (int)((posSlot >> 1) - 1);
            var baseVal = (2 | (posSlot & 1)) << footerBits;
            _tempPrices[i] = BitTreeEncoder.ReverseGetPrice(_posEncoders,
                baseVal - posSlot - 1, footerBits, i - baseVal);
        }

        for (uint lenToPosState = 0; lenToPosState < Base.KNumLenToPosStates; lenToPosState++)
        {
            uint posSlot;
            var encoder = _posSlotEncoder[lenToPosState];

            var st = lenToPosState << Base.KNumPosSlotBits;
            for (posSlot = 0; posSlot < _distTableSize; posSlot++)
            {
                _posSlotPrices[st + posSlot] = encoder.GetPrice(posSlot);
            }

            for (posSlot = Base.KEndPosModelIndex; posSlot < _distTableSize; posSlot++)
            {
                _posSlotPrices[st + posSlot] += ((posSlot >> 1) - 1 - Base.KNumAlignBits) << BitEncoder.KNumBitPriceShiftBits;
            }

            var st2 = lenToPosState * Base.KNumFullDistances;
            uint i;
            for (i = 0; i < Base.KStartPosModelIndex; i++)
            {
                _distancesPrices[st2 + i] = _posSlotPrices[st + i];
            }

            for (; i < Base.KNumFullDistances; i++)
            {
                _distancesPrices[st2 + i] = _posSlotPrices[st + GetPosSlot(i)] + _tempPrices[i];
            }
        }

        _matchPriceCount = 0;
    }

    private void FillAlignPrices()
    {
        for (uint i = 0; i < Base.KAlignTableSize; i++)
        {
            _alignPrices[i] = _posAlignEncoder.ReverseGetPrice(i);
        }
    }

    private static readonly string[] KMatchFinderIds =
    {
        "BT2",
        "BT4"
    };

    private static int FindMatchFinder(string s)
    {
        for (var m = 0; m < KMatchFinderIds.Length; m++)
        {
            if (string.Equals(s, KMatchFinderIds[m], StringComparison.Ordinal))
            {
                return m;
            }
        }

        return -1;
    }

    public void SetCoderProperties(CoderPropId[] propIDs, object[] properties)
    {
        for (uint i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            switch (propIDs[i])
            {
                case CoderPropId.NumFastBytes:
                {
                    if (prop is not int numFastBytes || numFastBytes < 5 || numFastBytes > Base.KMatchMaxLen)
                    {
                        throw new InvalidParamException();
                    }

                    _numFastBytes = (uint)numFastBytes;
                    break;
                }

                case CoderPropId.Algorithm:
                {
                    break;
                }

                case CoderPropId.MatchFinder:
                {
                    if (prop is not string s)
                    {
                        throw new InvalidParamException();
                    }

                    var matchFinderIndexPrev = _matchFinderType;
                    var m = FindMatchFinder(s.ToUpper());
                    if (m < 0)
                    {
                        throw new InvalidParamException();
                    }

                    _matchFinderType = (EMatchFinderType)m;
                    if (_matchFinder != null && matchFinderIndexPrev != _matchFinderType)
                    {
                        _dictionarySizePrev = 0xFFFFFFFF;
                        _matchFinder = null;
                    }

                    break;
                }

                case CoderPropId.DictionarySize:
                {
                    const int kDicLogSizeMaxCompress = 30;
                    if (prop is not int dictionarySize ||
                        dictionarySize < (uint)(1 << Base.KDicLogSizeMin) ||
                        dictionarySize > (uint)(1 << kDicLogSizeMaxCompress))
                    {
                        throw new InvalidParamException();
                    }

                    _dictionarySize = (uint)dictionarySize;
                    int dicLogSize;
                    for (dicLogSize = 0; dicLogSize < (uint)kDicLogSizeMaxCompress; dicLogSize++)
                    {
                        if (dictionarySize <= (uint)(1 << dicLogSize))
                        {
                            break;
                        }
                    }

                    _distTableSize = (uint)dicLogSize * 2;
                    break;
                }

                case CoderPropId.PosStateBits:
                {
                    if (prop is not int bits || bits < 0 || bits > (uint)Base.KNumPosStatesBitsEncodingMax)
                    {
                        throw new InvalidParamException();
                    }

                    _posStateBits = bits;
                    _posStateMask = ((uint)1 << _posStateBits) - 1;
                    break;
                }

                case CoderPropId.LitPosBits:
                {
                    if (prop is not int bits || bits < 0 || bits > Base.KNumLitPosStatesBitsEncodingMax)
                    {
                        throw new InvalidParamException();
                    }

                    _numLiteralPosStateBits = bits;
                    break;
                }

                case CoderPropId.LitContextBits:
                {
                    if (prop is not int bits || bits < 0 || bits > Base.KNumLitContextBitsMax)
                    {
                        throw new InvalidParamException();
                    }

                    _numLiteralContextBits = bits;
                    break;
                }

                case CoderPropId.EndMarker:
                {
                    if (prop is not bool b)
                    {
                        throw new InvalidParamException();
                    }

                    SetWriteEndMarkerMode(b);
                    break;
                }

                default:
                    throw new InvalidParamException();
            }
        }
    }

    private uint _trainSize;

    internal void SetTrainSize(uint trainSize)
    {
        _trainSize = trainSize;
    }
}