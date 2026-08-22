#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Diagnostics;
using System.Runtime.InteropServices;
using static CHDSharpEncoder.ZLib.Deflate.Constants;

namespace CHDSharpEncoder.ZLib.Deflate;

internal static partial class Deflater
{
    private const int InitState = 42; // zlib header -> BUSY_STATE
    private const int ExtraState = 69; // gzip extra block -> NAME_STATE
    private const int NameState = 73; // gzip file name -> COMMENT_STATE
    private const int CommentState = 91; // gzip comment -> HCRC_STATE
    private const int HcrcState = 103; // gzip header CRC -> BUSY_STATE
    private const int BusyState = 113; // deflate -> FINISH_STATE
    private const int FinishState = 666; // stream complete
    private const int PresetDict = 0x20; // preset dictionary flag in zlib header
    private const int MaxStored = 65535; // maximum stored block length in deflate format (not including header)
    private const int DistCodeLen = 512;

    private const uint MinLookAhead = MaxMatch + MinMatch + 1; // Minimum amount of lookahead, except at the end of the input file.
#if DEBUG
    private const ushort Literals = 256; // number of literal bytes 0..255
#endif

    private const uint WinInit = MaxMatch;
    /* Number of bytes after end of data in window to initialize in order to avoid
       memory checker errors from longest match routines */

    private const ushort TooFar = 4096;

    private static readonly string[] SzErrmsg = new[]
    {
        "need dictionary", /* Z_NEED_DICT       2  */
        "stream end", /* Z_STREAM_END      1  */
        "", /* Z_OK              0  */
        "file error", /* Z_ERRNO         (-1) */
        "stream error", /* Z_STREAM_ERROR  (-2) */
        "data error", /* Z_DATA_ERROR    (-3) */
        "insufficient memory", /* Z_MEM_ERROR     (-4) */
        "buffer error", /* Z_BUF_ERROR     (-5) */
        "incompatible version", /* Z_VERSION_ERROR (-6) */
        ""
    };

    internal static readonly Config[] SConfigurationTable = new Config[]
    {
        new(0, 0, 0, 0, Config.DeflateType.Stored), // 0: store only
        new(4, 4, 8, 4, Config.DeflateType.Fast), // 1: max speed, no lazy matches
        new(4, 5, 16, 8, Config.DeflateType.Fast), // 2
        new(4, 6, 32, 32, Config.DeflateType.Fast), // 3
        new(4, 4, 16, 16, Config.DeflateType.Slow), // 4: lazy matches
        new(8, 16, 32, 32, Config.DeflateType.Slow), // 5
        new(8, 16, 128, 128, Config.DeflateType.Slow), // 6
        new(8, 32, 128, 256, Config.DeflateType.Slow), // 7
        new(32, 128, 258, 1024, Config.DeflateType.Slow), // 8
        new(32, 258, 258, 4096, Config.DeflateType.Slow) // 9: max compression
    };

    private static readonly int[] SBaseDist = new[]
    {
        0, 1, 2, 3, 4, 6, 8, 12, 16, 24,
        32, 48, 64, 96, 128, 192, 256, 384, 512, 768,
        1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576
    };

    private static readonly byte[] SDistCode = new byte[]
    {
        0, 1, 2, 3, 4, 4, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8,
        8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9, 10, 10, 10, 10, 10, 10, 10, 10,
        10, 10, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11,
        11, 11, 11, 11, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
        12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 13, 13, 13, 13,
        13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13,
        13, 13, 13, 13, 13, 13, 13, 13, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
        14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
        14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
        14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 0, 0, 16, 17,
        18, 18, 19, 19, 20, 20, 20, 20, 21, 21, 21, 21, 22, 22, 22, 22, 22, 22, 22, 22,
        23, 23, 23, 23, 23, 23, 23, 23, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
        24, 24, 24, 24, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
        26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
        26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 27, 27, 27, 27, 27, 27, 27, 27,
        27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27,
        27, 27, 27, 27, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
        28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
        28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
        28, 28, 28, 28, 28, 28, 28, 28, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
        29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
        29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
        29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29, 29
    };

    private static readonly byte[] SLengthCode = new byte[]
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 12, 12,
        13, 13, 13, 13, 14, 14, 14, 14, 15, 15, 15, 15, 16, 16, 16, 16, 16, 16, 16, 16,
        17, 17, 17, 17, 17, 17, 17, 17, 18, 18, 18, 18, 18, 18, 18, 18, 19, 19, 19, 19,
        19, 19, 19, 19, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20,
        21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 22, 22, 22, 22,
        22, 22, 22, 22, 22, 22, 22, 22, 22, 22, 22, 22, 23, 23, 23, 23, 23, 23, 23, 23,
        23, 23, 23, 23, 23, 23, 23, 23, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
        24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
        25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
        25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 26, 26, 26, 26, 26, 26, 26, 26,
        26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
        26, 26, 26, 26, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27,
        27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 27, 28
    };

    private static readonly int[] SBaseLength = new[]
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40, 48, 56,
        64, 80, 96, 112, 128, 160, 192, 224, 0
    };

    private static readonly ushort[] SBlOrder = // The lengths of the bit length codes are sent in order of decreasing probability, to avoid transmitting the lengths for unused bit length codes.
        new ushort[] { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

    private static readonly int[] SExtraDbits = // extra bits for each distance code
        new[] { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };

    private static readonly int[] SExtraLbits = // extra bits for each length code
        new[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };

    private static readonly int[] SExtraBlbits = // extra bits for each bit length code
        new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 7 };

    internal static void Init()
    {
        SObjectPool.Return(new DeflateState());
    }

    internal static int Deflate(ref ZStream strm, int flush)
    {
        if (DeflateStateCheck(ref strm) || flush > ZBlock || flush < 0)
            return ZStreamError;

        var s = strm.DeflateState;

        if (strm._output.IsEmpty
            || (strm.AvailIn != 0 && strm._input.IsEmpty)
            || (s.Status == FinishState && flush != ZFinish))
            return ReturnWithError(ref strm, ZStreamError);
        if (strm.AvailOut == 0)
            return ReturnWithError(ref strm, ZBufError);

        var oldFlush = s.LastFlush; // value of flush param for previous deflate call
        s.LastFlush = flush;

        s.SymEnd = (s.LitBufsize - 1) * 3;

#if NET7_0_OR_GREATER
        ref var refs = ref strm.DeflateRefs;
#endif
        ref var pendingBuf = ref
#if NET7_0_OR_GREATER
            refs.PendingBuf;
#else
            MemoryMarshal.GetReference<byte>(s.pending_buf);
#endif
        ref var pendingOut = ref
#if NET7_0_OR_GREATER
            refs.PendingOut;
#else
            MemoryMarshal.GetReference<byte>(s.pending_out);
#endif

        // Flush as much pending output as possible
        if (s.Pending != 0)
        {
            FlushPending(ref strm, ref pendingBuf, ref pendingOut);
            if (strm.AvailOut == 0)
            {
                /* Since avail_out is 0, deflate will be called again with
                 * more output space, but possibly with both pending and
                 * avail_in equal to zero. There won't be anything to do,
                 * but this is not an error situation so make sure we
                 * return OK instead of BUF_ERROR at next call of deflate:
                 */
                s.LastFlush = -1;
                return ZOk;
            }

            /* Make sure there is something to do and avoid duplicate consecutive
             * flushes. For repeated and useless calls with Z_FINISH, we keep
             * returning Z_STREAM_END instead of Z_BUF_ERROR.
             */
        }
        else if (strm.AvailIn == 0
            && Rank(flush) <= Rank(oldFlush)
            && flush != ZFinish)
        {
            return ReturnWithError(ref strm, ZBufError);
        }

        switch (s.Status)
        {
            // User must not provide more input after the first FINISH:
            case FinishState when strm.AvailIn != 0:
                return ReturnWithError(ref strm, ZBufError);
            // Write the header
            case InitState when s.Wrap == 0:
                s.Status = BusyState;
                break;
        }

        if (s.Status == InitState)
        {
            // zlib header
            var header = (ZDeflated + ((s.WBits - 8) << 4)) << 8;
            uint levelFlags;

            if (s.Strategy >= ZHuffmanOnly || s.Level < 2)
            {
                levelFlags = 0;
            }
            else switch (s.Level)
            {
                case < 6:
                    levelFlags = 1;
                    break;
                case 6:
                    levelFlags = 2;
                    break;
                default:
                    levelFlags = 3;
                    break;
            }

            header |= levelFlags << 6;
            if (s.Strstart != 0)
            {
                header |= PresetDict;
            }

            header += 31 - header % 31;

            PutShort(s, header, ref pendingBuf);

            // Save the adler32 of the preset dictionary:
            if (s.Strstart != 0)
            {
                PutShort(s, strm.Adler >> 16, ref pendingBuf);
                PutShort(s, strm.Adler & 0xffff, ref pendingBuf);
            }

            strm.Adler = Adler32.Update(0, ref netUnsafe.NullRef<byte>(), 0);
            s.Status = BusyState;

            // Compression must start with an empty pending buffer
            FlushPending(ref strm, ref pendingBuf, ref pendingOut);
            if (s.Pending != 0)
            {
                s.LastFlush = -1;
                return ZOk;
            }
        }

        // Start a new block or continue the current one.
        if (strm.AvailIn != 0
            || s.Lookahead != 0
            || (flush != ZNoFlush && s.Status != FinishState))
        {
            BlockState bstate;
            if (s.Level == 0)
            {
                bstate = DeflateStored(ref strm, flush, ref pendingBuf, ref pendingOut);
            }
            else
            {
                switch (s.Strategy)
                {
                    case ZHuffmanOnly:
                        bstate = DeflateHuff(ref strm, flush, ref pendingBuf, ref pendingOut);
                        break;
                    case ZRle:
                        bstate = DeflateRle(ref strm, flush, ref pendingBuf, ref pendingOut);
                        break;
                    default:
                        ref var configurationTable = ref
#if NET7_0_OR_GREATER
                            refs.ConfigurationTable;
#else
                            MemoryMarshal.GetReference<Config>(s_configuration_table);
#endif
                        var type = Unsafe.Add(ref configurationTable, (uint)s.Level).deflate_type;
                        bstate = type switch
                        {
                            Config.DeflateType.Stored => DeflateStored(ref strm, flush, ref pendingBuf,
                                ref pendingOut),
                            Config.DeflateType.Fast => DeflateFast(ref strm, flush, ref pendingBuf,
                                ref pendingOut),
                            _ => DeflateSlow(ref strm, flush, ref pendingBuf, ref pendingOut)
                        };
                        break;
                }
            }

            if (bstate is BlockState.FinishStarted or BlockState.FinishDone)
            {
                s.Status = FinishState;
            }

            switch (bstate)
            {
                case BlockState.NeedMore or BlockState.FinishStarted:
                {
                    if (strm.AvailOut == 0)
                    {
                        s.LastFlush = -1; // avoid BUF_ERROR next call, see above
                    }

                    return ZOk;
                    /* If flush != Z_NO_FLUSH && avail_out == 0, the next call
                 * of deflate should use the same flush parameter to make sure
                 * that the flush is complete. So we don't have to output an
                 * empty block here, this will be done at next call. This also
                 * ensures that for a very small output buffer, we emit at most
                 * one empty block.
                 */
                }
                case BlockState.BlockDone:
                {
                    if (flush == ZPartialFlush)
                    {
#if NET7_0_OR_GREATER
                        if (netUnsafe.IsNullRef(ref refs.StaLtree))
                        {
                            refs.StaLtree = ref MemoryMarshal.GetReference<TreeNode>(Tree.SLtree);
                        }
#endif
                        Tree.Align(s, ref pendingBuf, ref
#if NET7_0_OR_GREATER
                            refs.StaLtree
#else
                        MemoryMarshal.GetReference<TreeNode>(Tree.s_ltree)
#endif
                        );
                    }
                    else if (flush != ZBlock) // FULL_FLUSH or SYNC_FLUSH
                    {
                        Tree.StoredBlock(s, ref netUnsafe.NullRef<byte>(), 0, 0, ref pendingBuf);
                        /* For a full flush, this empty block will be recognized
                     * as a special marker by InflateSync().
                     */
                        if (flush == ZFullFlush)
                        {
                            ClearHash(ref strm);
                            if (s.Lookahead == 0)
                            {
                                s.Strstart = 0;
                                s.BlockStart = 0;
                                s.Insert = 0;
                            }
                        }
                    }

                    FlushPending(ref strm, ref pendingBuf, ref pendingOut);
                    if (strm.AvailOut == 0)
                    {
                        s.LastFlush = -1; // avoid BUF_ERROR at next call, see above
                        return ZOk;
                    }

                    break;
                }
            }
        }

        if (flush != ZFinish)
            return ZOk;
        if (s.Wrap <= 0)
            return ZStreamEnd;

        // Write the trailer
        PutShort(s, strm.Adler >> 16, ref pendingBuf);
        PutShort(s, strm.Adler & 0xffff, ref pendingBuf);

        FlushPending(ref strm, ref pendingBuf, ref pendingOut);

        // If avail_out is zero, the application will call deflate again to flush the rest.
        if (s.Wrap > 0)
        {
            s.Wrap = -s.Wrap; // write the trailer only once!
        }

        return s.Pending != 0 ? ZOk : ZStreamEnd;
    }

    private static bool DeflateStateCheck(ref ZStream strm)
    {
        var s = strm.DeflateState;
        return s == null
            || (s.Status != InitState
                && s.Status != ExtraState
                && s.Status != NameState
                && s.Status != CommentState
                && s.Status != HcrcState
                && s.Status != BusyState
                && s.Status != FinishState);
    }

    private static void LongestMatchInit(ref ZStream strm)
    {
        const byte minMatch = 3;

        var s = strm.DeflateState;
        s.WindowSize = 2 * s.WSize;

        ClearHash(ref strm);

        // set the default configuration parameters
        ref var configurationTable = ref
#if NET7_0_OR_GREATER
            strm.DeflateRefs.ConfigurationTable;
#else
            MemoryMarshal.GetReference<Config>(s_configuration_table);
#endif
        ref var config = ref Unsafe.Add(ref configurationTable, (uint)s.Level);
        s.MaxLazyMatch = config.max_lazy;
        s.GoodMatch = config.good_length;
        s.NiceMatch = config.nice_length;
        s.MaxChainLength = config.max_chain;

        s.Strstart = 0;
        s.BlockStart = 0;
        s.Lookahead = 0;
        s.Insert = 0;
        s.MatchLength = s.PrevLength = minMatch - 1;
        s.MatchAvailable = false;
        s.InsH = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReturnWithError(ref ZStream strm, int err)
    {
        strm.Msg = SzErrmsg[err is < -6 or > 2 ? 9 : 2 - err];
        return err;
    }

    private static void FlushPending(ref ZStream strm, ref byte pendingBuf, ref byte pendingOut)
    {
        var s = strm.DeflateState;
        Tree.FlushBits(s, ref pendingBuf);
        var len = s.Pending;
        if (len > strm.AvailOut)
        {
            len = strm.AvailOut;
        }

        if (len == 0)
            return;

        netUnsafe.CopyBlockUnaligned(ref
#if NET7_0_OR_GREATER
            Unsafe.Add(ref strm.OutputPtr, strm.next_out),
#else
            MemoryMarshal.GetReference(strm._output.Slice((int)strm.next_out)),
#endif
            ref Unsafe.Add(ref pendingOut, s.PendingOutOffset),
            len);

        strm.next_out += len;
        s.PendingOutOffset += len;
        s.Pending -= len;
        if (s.Pending == 0)
        {
            //s.pending_out = s.pending_buf;
            s.PendingOutOffset = 0;
        }

        strm.total_out += len;
        strm.AvailOut -= len;
    }

    /// <summary>
    /// Rank Z_BLOCK between Z_NO_FLUSH and Z_PARTIAL_FLUSH.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Rank(int f)
    {
        return f * 2 - (f > 4 ? 9 : 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PutShort(DeflateState s, uint b, ref byte pendingBuf)
    {
        Unsafe.Add(ref pendingBuf, s.Pending++) = (byte)(b >> 8);
        Unsafe.Add(ref pendingBuf, s.Pending++) = (byte)(b & 0xff);
    }

    private static BlockState DeflateStored(ref ZStream strm, int flush, ref byte pendingBuf, ref byte pendingOut)
    {
        var s = strm.DeflateState;
        /* Smallest worthy block size when not flushing or finishing. By default
         * this is 32K. This can be as small as 507 bytes for memLevel == 1. For
         * large input and output buffers, the stored block size will be larger.
         */
        var minBlock = Math.Min(s.PendingBufSize - 5, s.WSize);

        /* Copy as many min_block or larger stored blocks directly to next_out as
         * possible. If flushing, copy the remaining available input to next_out as
         * stored blocks, if there is enough space.
         */
        uint len, left, have;
        uint last = 0;
        var used = strm.AvailIn;
#if NET7_0_OR_GREATER
        ref var refs = ref strm.DeflateRefs;
        if (netUnsafe.IsNullRef(ref refs.Window))
        {
            refs.Window = ref MemoryMarshal.GetReference<byte>(s.Window);
        }
#endif
        ref var window = ref
#if NET7_0_OR_GREATER
            refs.Window;
#else
            MemoryMarshal.GetReference<byte>(s.window);
#endif
        ref var nextOut = ref
#if NET7_0_OR_GREATER
            strm.OutputPtr;
#else
            MemoryMarshal.GetReference(strm._output);
#endif
        do
        {
            /* Set len to the maximum size block that we can copy directly with the
             * available input data and output space. Set left to how much of that
             * would be copied from what's left in the window.
             */
            len = MaxStored; // maximum deflate stored block length
            have = (uint)((s.BiValid + 42) >> 3); // number of header bytes
            if (strm.AvailOut < have) // need room for header
                break;
            // maximum stored block length that will fit in avail_out:
            have = strm.AvailOut - have;
            left = (uint)(s.Strstart - s.BlockStart); // bytes left in window
            if (len > left + strm.AvailIn)
            {
                len = left + strm.AvailIn; // limit len to the input
            }

            if (len > have)
            {
                len = have; // limit len to the output
            }

            /* If the stored block would be less than min_block in length, or if
             * unable to copy all of the available input when flushing, then try
             * copying to the window and the pending buffer instead. Also don't
             * write an empty block when flushing -- deflate() does that.
             */
            if (len < minBlock && ((len == 0 && flush != ZFinish) ||
                                    flush == ZNoFlush ||
                                    len != left + strm.AvailIn))
                break;

            /* Make a dummy stored block in pending to get the header bytes,
             * including any pending bits. This also updates the debugging counts.
             */
            last = flush == ZFinish && len == left + strm.AvailIn ? 1U : 0U;
            Tree.StoredBlock(s, ref netUnsafe.NullRef<byte>(), 0, last, ref pendingBuf);

            // Replace the lengths in the dummy stored block with len.
            Unsafe.Add(ref pendingBuf, s.Pending - 4) = (byte)len;
            Unsafe.Add(ref pendingBuf, s.Pending - 3) = (byte)(len >> 8);
            Unsafe.Add(ref pendingBuf, s.Pending - 2) = (byte)~len;
            Unsafe.Add(ref pendingBuf, s.Pending - 1) = (byte)(~len >> 8);

            // Write the stored block header bytes.
            FlushPending(ref strm, ref pendingBuf, ref pendingOut);
#if DEBUG
            // Update debugging counts for the data about to be copied.
            s.CompressedLen += len << 3;
            s.BitsSent += len << 3;
#endif
            // Copy uncompressed bytes from the window to next_out.
            if (left != 0)
            {
                if (left > len)
                {
                    left = len;
                }

                netUnsafe.CopyBlockUnaligned(ref Unsafe.Add(ref nextOut, strm.next_out),
                    ref Unsafe.Add(ref window, (uint)s.BlockStart), left);
                strm.next_out += left;
                strm.AvailOut -= left;
                strm.total_out += left;
                s.BlockStart += (int)left;
                len -= left;
            }

            // Copy uncompressed bytes directly from next_in to next_out, updating the check value.
            if (len != 0)
            {
                ReadBuf(ref strm, ref Unsafe.Add(ref nextOut, strm.next_out), len);
                strm.next_out += len;
                strm.AvailOut -= len;
                strm.total_out += len;
            }
        } while (last == 0);

        /* Update the sliding window with the last s.w_size bytes of the copied
         * data, or append all of the copied data to the existing window if less
         * than s.w_size bytes were copied. Also update the number of bytes to
         * insert in the hash tables, in the event that deflateParams() switches to
         * a non-zero compression level.
         */
        used -= strm.AvailIn; // number of input bytes directly copied
        if (used != 0)
        {
            ref var nextIn = ref
#if NET7_0_OR_GREATER
                Unsafe.Add(ref strm.InputPtr, strm.next_in);
#else
                MemoryMarshal.GetReference(strm._input.Slice((int)strm.next_in));
#endif
            /* If any input was used, then no unused input remains in the window,
             * therefore s.block_start == s.strstart.
             */
            if (used >= s.WSize) // supplant the previous history
            {
                s.Matches = 2; // clear hash
                netUnsafe.CopyBlockUnaligned(ref window, ref Unsafe.Subtract(ref nextIn, s.WSize), s.WSize);

                s.Strstart = s.WSize;
                s.Insert = s.Strstart;
            }
            else
            {
                if (s.WindowSize - s.Strstart <= used)
                {
                    // Slide the window down
                    s.Strstart -= s.WSize;
                    netUnsafe.CopyBlockUnaligned(ref window, ref Unsafe.Add(ref window, s.WSize), s.Strstart);
                    if (s.Matches < 2)
                    {
                        s.Matches++; // add a pending SlideHash()
                    }

                    if (s.Insert > s.Strstart)
                    {
                        s.Insert = s.Strstart;
                    }
                }

                netUnsafe.CopyBlockUnaligned(ref Unsafe.Add(ref window, s.Strstart), ref Unsafe.Subtract(ref nextIn, used), used);
                s.Strstart += used;
                s.Insert += Math.Min(used, s.WSize - s.Insert);
            }

            s.BlockStart = (int)s.Strstart;
        }

        if (s.HighWater < s.Strstart)
        {
            s.HighWater = s.Strstart;
        }

        // If the last block was written to next_out, then done.
        if (last != 0)
            return BlockState.FinishDone;

        // If flushing and all input has been consumed, then done.
        if (flush != ZNoFlush && flush != ZFinish &&
            strm.AvailIn == 0 && s.Strstart == s.BlockStart)
            return BlockState.BlockDone;

        // Fill the window with any remaining input.
        have = s.WindowSize - s.Strstart;
        if (strm.AvailIn > have && s.BlockStart >= s.WSize)
        {
            // Slide the window down.
            s.BlockStart -= (int)s.WSize;
            s.Strstart -= s.WSize;
            netUnsafe.CopyBlockUnaligned(ref window, ref Unsafe.Add(ref window, s.WSize), s.Strstart);
            if (s.Matches < 2)
            {
                s.Matches++; // add a pending SlideHash()
            }

            have += s.WSize; // more space now
            if (s.Insert > s.Strstart)
            {
                s.Insert = s.Strstart;
            }
        }

        if (have > strm.AvailIn)
        {
            have = strm.AvailIn;
        }

        if (have != 0)
        {
            ReadBuf(ref strm, ref Unsafe.Add(ref window, s.Strstart), have);
            s.Strstart += have;
            s.Insert += Math.Min(have, s.WSize - s.Insert);
        }

        if (s.HighWater < s.Strstart)
        {
            s.HighWater = s.Strstart;
        }

        /* There was not enough avail_out to write a complete worthy or flushed
         * stored block to next_out. Write a stored block to pending instead, if we
         * have enough input for a worthy block, or if flushing and there is enough
         * room for the remaining input as a stored block in the pending buffer.
         */
        have = (uint)((s.BiValid + 42) >> 3); // number of header bytes
        // maximum stored block length that will fit in pending:
        have = Math.Min(s.PendingBufSize - have, MaxStored);
        minBlock = Math.Min(have, s.WSize);
        left = (uint)(s.Strstart - s.BlockStart);
        if (left >= minBlock ||
            ((left != 0 || flush == ZFinish) && flush != ZNoFlush &&
             strm.AvailIn == 0 && left <= have))
        {
            len = Math.Min(left, have);
            last = flush == ZFinish && strm.AvailIn == 0 && len == left ? 1U : 0U;
            Tree.StoredBlock(s, ref Unsafe.Add(ref window, (uint)s.BlockStart), len, last, ref pendingBuf);
            s.BlockStart += (int)len;
            FlushPending(ref strm, ref pendingBuf, ref pendingOut);
        }

        // We've done all we can with the available input and output.
        return last != 0 ? BlockState.FinishStarted : BlockState.NeedMore;
    }

    private static uint ReadBuf(ref ZStream strm, ref byte buf, uint size)
    {
        var len = strm.AvailIn;

        if (len > size)
        {
            len = size;
        }

        if (len == 0)
            return 0;

        strm.AvailIn -= len;

        netUnsafe.CopyBlockUnaligned(ref buf, ref
#if NET7_0_OR_GREATER
            Unsafe.Add(ref strm.InputPtr, strm.next_in),
#else
            MemoryMarshal.GetReference(strm._input.Slice((int)strm.next_in)),
#endif
            len);
        if (strm.DeflateState.Wrap == 1)
        {
            strm.Adler = Adler32.Update(strm.Adler, ref buf, len);
        }

        strm.next_in += len;
        strm.total_in += len;

        return len;
    }

    private static BlockState DeflateHuff(ref ZStream strm, int flush, ref byte pendingBuf, ref byte pendingOut)
    {
        var s = strm.DeflateState;
#if NET7_0_OR_GREATER
        ref var refs = ref strm.DeflateRefs;
        InitRefFields(s, ref refs);
#endif
        ref var window = ref
#if NET7_0_OR_GREATER
            refs.Window;
#else
            MemoryMarshal.GetReference<byte>(s.window);
#endif
        ref var prev = ref
#if NET7_0_OR_GREATER
            refs.Prev;
#else
            MemoryMarshal.GetReference<ushort>(s.prev);
#endif
        ref var head = ref
#if NET7_0_OR_GREATER
            refs.Head;
#else
            MemoryMarshal.GetReference<ushort>(s.head);
#endif
        ref var blCount = ref
#if NET7_0_OR_GREATER
            refs.BlCount;
#else
            MemoryMarshal.GetReference<ushort>(s.bl_count);
#endif
        ref var heap = ref
#if NET7_0_OR_GREATER
            refs.Heap;
#else
            MemoryMarshal.GetReference<int>(s.heap);
#endif
        ref var depth = ref
#if NET7_0_OR_GREATER
            refs.Depth;
#else
            MemoryMarshal.GetReference<byte>(s.depth);
#endif

        ref var staLtree = ref
#if NET7_0_OR_GREATER
            refs.StaLtree;
#else
            MemoryMarshal.GetReference<TreeNode>(Tree.s_ltree);
#endif
        ref var staDtree = ref
#if NET7_0_OR_GREATER
            refs.StaDtree;
#else
            MemoryMarshal.GetReference<TreeNode>(Tree.s_dtree);
#endif
        ref var dynLtree = ref
#if NET7_0_OR_GREATER
            refs.DynLtree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.dyn_ltree);
#endif
        ref var dynDtree = ref
#if NET7_0_OR_GREATER
            refs.DynDtree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.dyn_dtree);
#endif
        ref var blTree = ref
#if NET7_0_OR_GREATER
            refs.BlTree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.bl_tree);
#endif
        ref var blOrder = ref
#if NET7_0_OR_GREATER
            refs.BlOrder;
#else
            MemoryMarshal.GetReference<ushort>(s_bl_order);
#endif
        ref var distCode = ref
#if NET7_0_OR_GREATER
            refs.DistCode;
#else
            MemoryMarshal.GetReference<byte>(s_dist_code);
#endif
        ref var lengthCode = ref
#if NET7_0_OR_GREATER
            refs.LengthCode;
#else
            MemoryMarshal.GetReference<byte>(s_length_code);
#endif
        ref var baseDist = ref
#if NET7_0_OR_GREATER
            refs.BaseDist;
#else
            MemoryMarshal.GetReference<int>(s_base_dist);
#endif
        ref var baseLength = ref
#if NET7_0_OR_GREATER
            refs.BaseLength;
#else
            MemoryMarshal.GetReference<int>(s_base_length);
#endif
        ref var extraDbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraDbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_dbits);
#endif
        ref var extraLbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraLbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_lbits);
#endif
        ref var extraBlbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraBlbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_blbits);
#endif
        BlockState state;
        for (;;)
        {
            // Make sure that we have a literal to write.
            if (s.Lookahead == 0)
            {
                FillWindow(ref strm, ref window, ref prev, ref head);
#pragma warning disable CA1508
                if (s.Lookahead == 0)
#pragma warning restore CA1508
                {
                    if (flush == ZNoFlush)
                        return BlockState.NeedMore;

                    break; // flush the current block
                }
            }

            // Output a literal byte
            s.MatchLength = 0;
            var c = Unsafe.Add(ref window, s.Strstart);
            Trace.Tracevv($"{Convert.ToChar(c)}");
            TreeTallyLit(s, c, out var bflush, ref pendingBuf, ref dynLtree, ref dynDtree,
                ref distCode, ref lengthCode);
            s.Lookahead--;
            s.Strstart++;
            if (bflush && FlushBlock(ref strm, 0, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
                return state;
        }

        s.Insert = 0;
        if (flush == ZFinish)
        {
            if (FlushBlock(ref strm, 1, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
                return state;

            return BlockState.FinishDone;
        }

        if (s.SymNext != 0 && FlushBlock(ref strm, 0, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
            return state;

        return BlockState.BlockDone;
    }

    private static void FillWindow(ref ZStream strm, ref byte window, ref ushort prev, ref ushort head)
    {
        var s = strm.DeflateState;
        var wsize = s.WSize;

        Debug.Assert(s.Lookahead < MinLookAhead, "already enough lookahead");

        do
        {
            var more = s.WindowSize - s.Lookahead - s.Strstart; // Amount of free space at the end of the window.

            /* If the window is almost full and there is insufficient lookahead,
             * move the upper half to the lower one to make room in the upper half.
             */
            if (s.Strstart >= wsize + s.WSize - MinLookAhead)
            {
                var sourceBytesToCopy = wsize - more;
                netUnsafe.CopyBlockUnaligned(ref window, ref Unsafe.Add(ref window, wsize), sourceBytesToCopy);
                s.MatchStart -= wsize;
                s.Strstart -= wsize; // we now have strstart >= MaxDist
                s.BlockStart -= (int)wsize;
                if (s.Insert > s.Strstart)
                {
                    s.Insert = s.Strstart;
                }

                SlideHash(s, ref prev, ref head);
                more += wsize;
            }

            if (strm.AvailIn == 0)
                break;

            /* If there was no sliding:
             *    strstart <= WSize+MaxDist-1 && lookahead <= MinLookAhead - 1 &&
             *    more == window_size - lookahead - strstart
             * => more >= window_size - (MinLookAhead-1 + WSize + MaxDist-1)
             * => more >= window_size - 2*WSize + 2
             * In the BIG_MEM or MMAP case (not yet supported),
             *   window_size == input_size + MinLookAhead  &&
             *   strstart + s->lookahead <= input_size => more >= MinLookAhead.
             * Otherwise, window_size == 2*WSize so more >= 2.
             * If there was sliding, more >= WSize. So in all cases, more >= 2.
             */
            Debug.Assert(more >= 2, "more < 2");

            var n = ReadBuf(ref strm, ref Unsafe.Add(ref window, s.Strstart + s.Lookahead), more);
            s.Lookahead += n;

            // Initialize the hash value now that we have some input:
            if (s.Lookahead + s.Insert >= MinMatch)
            {
                var str = s.Strstart - s.Insert;
                s.InsH = Unsafe.Add(ref window, str);
                UpdateHash(s, ref s.InsH, Unsafe.Add(ref window, str + 1));

                while (s.Insert != 0)
                {
                    UpdateHash(s, ref s.InsH, Unsafe.Add(ref window, str + MinMatch - 1));
                    ref var temp = ref Unsafe.Add(ref head, s.InsH);
                    Unsafe.Add(ref prev, str & s.WMask) = temp;
                    temp = (ushort)str;
                    str++;
                    s.Insert--;
                    if (s.Lookahead + s.Insert < MinMatch)
                        break;
                }
            }
            /* If the whole input has less than MinMatch bytes, ins_h is garbage,
             * but this is not important since only literal bytes will be emitted.
             */
        } while (s.Lookahead < MinLookAhead && strm.AvailIn != 0);

        /* If the WinInit bytes after the end of the current data have never been
         * written, then zero those bytes in order to avoid memory check reports of
         * the use of uninitialized (or uninitialised as Julian writes) bytes by
         * the longest match routines.  Update the high water mark for the next
         * time through here.  WinInit is set to MaxMatch since the longest match
         * routines allow scanning to strstart + MaxMatch, ignoring lookahead.
         */
        if (s.HighWater < s.WindowSize)
        {
            var curr = s.Strstart + s.Lookahead;
            uint init;

            if (s.HighWater < curr)
            {
                /* Previous high water mark below current data -- zero WinInit
                 * bytes or up to end of window, whichever is less.
                 */
                init = s.WindowSize - curr;
                if (init > WinInit)
                {
                    init = WinInit;
                }

                netUnsafe.InitBlockUnaligned(ref Unsafe.Add(ref window, curr), 0, init);
                s.HighWater = curr + init;
            }
            else if (s.HighWater < curr + WinInit)
            {
                /* High water mark at or above current data, but below current data
                 * plus WinInit -- zero out to current data plus WinInit, or up
                 * to end of window, whichever is less.
                 */
                init = curr + WinInit - s.HighWater;
                if (init > s.WindowSize - s.HighWater)
                {
                    init = s.WindowSize - s.HighWater;
                }

                netUnsafe.InitBlockUnaligned(ref Unsafe.Add(ref window, s.HighWater), 0, init);
                s.HighWater += init;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClearHash(ref ZStream strm) =>
        netUnsafe.InitBlock(ref netUnsafe.As<ushort, byte>(ref
#if NET7_0_OR_GREATER
            strm.DeflateRefs.Head
#else
        MemoryMarshal.GetReference<ushort>(strm.deflateState.head)
#endif
        ), 0, (uint)strm.DeflateState.Head.Length * sizeof(ushort));

    private static void SlideHash(DeflateState s, ref ushort prev, ref ushort head)
    {
        var wsize = s.WSize;
        var n = s.HashSize;
        uint m;

        ref var p = ref Unsafe.Add(ref head, n);
        do
        {
            p = ref Unsafe.Subtract(ref p, 1U);
            m = p;
            p = (ushort)(m >= wsize ? m - wsize : 0);
        } while (--n > 0);

        n = wsize;
        p = ref Unsafe.Add(ref prev, n);
        do
        {
            p = ref Unsafe.Subtract(ref p, 1U);
            m = p;
            p = (ushort)(m >= wsize ? m - wsize : 0);
            /* If n is not on any hash chain, prev[n] is garbage but
             * its value will never be used.
             */
        } while (--n > 0);
    }

    /// <summary>
    /// Updates a hash value with the given input byte
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateHash(DeflateState s, ref uint h, byte c)
    {
        h = (((h) << s.HashShift) ^ c) & s.HashMask;
    }

    private static void FlushBlockOnly(ref ZStream strm, uint last, ref byte pendingBuf, ref byte pendingOut,
        ref byte window, ref TreeNode staLtree, ref TreeNode staDtree, ref TreeNode dynLtree, ref TreeNode dynDtree,
        ref TreeNode blTree, ref ushort blCount, ref int heap, ref byte depth, ref ushort blOrder, ref byte distCode,
        ref byte lengthCode, ref int baseDist, ref int baseLength, ref int extraDbits, ref int extraLbits, ref int extraBlbits)
    {
        var s = strm.DeflateState;
        var blockStart = (uint)s.BlockStart;
        ref var buf = ref s.BlockStart >= 0L ? ref Unsafe.Add(ref window, blockStart) : ref netUnsafe.NullRef<byte>();
        Tree.FlushBlock(ref strm, ref buf, s.Strstart - blockStart, last,
            ref pendingBuf, ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount, ref heap, ref depth, ref blOrder,
            ref distCode, ref lengthCode, ref baseDist, ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits);
        s.BlockStart = (int)s.Strstart;
        FlushPending(ref strm, ref pendingBuf, ref pendingOut);
        Trace.Tracev("[FLUSH]");
    }

    private static bool FlushBlock(ref ZStream strm, uint last, ref byte window, out BlockState state,
        ref byte pendingBuf, ref byte pendingOut, ref TreeNode staLtree, ref TreeNode staDtree,
        ref TreeNode dynLtree, ref TreeNode dynDtree, ref TreeNode blTree, ref ushort blCount, ref int heap,
        ref byte depth, ref ushort blOrder, ref byte distCode, ref byte lengthCode, ref int baseDist, ref int baseLength,
        ref int extraDbits, ref int extraLbits, ref int extraBlbits)
    {
        FlushBlockOnly(ref strm, last, ref pendingBuf, ref pendingOut, ref window, ref staLtree, ref staDtree,
            ref dynLtree, ref dynDtree, ref blTree, ref blCount, ref heap, ref depth, ref blOrder, ref distCode,
            ref lengthCode, ref baseDist, ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits);
        if (strm.AvailOut == 0)
        {
            state = (last != 0) ? BlockState.FinishStarted : BlockState.NeedMore;
            return true;
        }

        state = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TreeTallyLit(DeflateState s, byte c, out bool flush,
        ref byte pendingBuf, ref TreeNode dynLtree, ref TreeNode dynDtree,
        ref byte distCode, ref byte lengthCode)
#if DEBUG
    {
        Unsafe.Add(ref pendingBuf, s.LitBufsize + s.SymNext++) = 0;
        Unsafe.Add(ref pendingBuf, s.LitBufsize + s.SymNext++) = 0;
        Unsafe.Add(ref pendingBuf, s.LitBufsize + s.SymNext++) = c;
        Unsafe.Add(ref dynLtree, c).fc++;
        flush = s.SymNext == s.SymEnd;
    }
#else
        => flush = Tree.Tally(s, 0, c, ref pending_buf, ref dyn_ltree, ref dyn_dtree,
            ref dist_code, ref length_code);
#endif

    private static BlockState DeflateRle(ref ZStream strm, int flush, ref byte pendingBuf, ref byte pendingOut)
    {
        var s = strm.DeflateState;
#if NET7_0_OR_GREATER
        ref var refs = ref strm.DeflateRefs;
        InitRefFields(s, ref refs);
#endif
        ref var window = ref
#if NET7_0_OR_GREATER
            refs.Window;
#else
            MemoryMarshal.GetReference<byte>(s.window);
#endif
        ref var sprev = ref
#if NET7_0_OR_GREATER
            refs.Prev;
#else
            MemoryMarshal.GetReference<ushort>(s.prev);
#endif
        ref var head = ref
#if NET7_0_OR_GREATER
            refs.Head;
#else
            MemoryMarshal.GetReference<ushort>(s.head);
#endif
        ref var blCount = ref
#if NET7_0_OR_GREATER
            refs.BlCount;
#else
            MemoryMarshal.GetReference<ushort>(s.bl_count);
#endif
        ref var heap = ref
#if NET7_0_OR_GREATER
            refs.Heap;
#else
            MemoryMarshal.GetReference<int>(s.heap);
#endif
        ref var depth = ref
#if NET7_0_OR_GREATER
            refs.Depth;
#else
            MemoryMarshal.GetReference<byte>(s.depth);
#endif

        ref var staLtree = ref
#if NET7_0_OR_GREATER
            refs.StaLtree;
#else
            MemoryMarshal.GetReference<TreeNode>(Tree.s_ltree);
#endif
        ref var staDtree = ref
#if NET7_0_OR_GREATER
            refs.StaDtree;
#else
            MemoryMarshal.GetReference<TreeNode>(Tree.s_dtree);
#endif
        ref var dynLtree = ref
#if NET7_0_OR_GREATER
            refs.DynLtree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.dyn_ltree);
#endif
        ref var dynDtree = ref
#if NET7_0_OR_GREATER
            refs.DynDtree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.dyn_dtree);
#endif
        ref var blTree = ref
#if NET7_0_OR_GREATER
            refs.BlTree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.bl_tree);
#endif
        ref var blOrder = ref
#if NET7_0_OR_GREATER
            refs.BlOrder;
#else
            MemoryMarshal.GetReference<ushort>(s_bl_order);
#endif
        ref var distCode = ref
#if NET7_0_OR_GREATER
            refs.DistCode;
#else
            MemoryMarshal.GetReference<byte>(s_dist_code);
#endif
        ref var lengthCode = ref
#if NET7_0_OR_GREATER
            refs.LengthCode;
#else
            MemoryMarshal.GetReference<byte>(s_length_code);
#endif
        ref var baseDist = ref
#if NET7_0_OR_GREATER
            refs.BaseDist;
#else
            MemoryMarshal.GetReference<int>(s_base_dist);
#endif
        ref var baseLength = ref
#if NET7_0_OR_GREATER
            refs.BaseLength;
#else
            MemoryMarshal.GetReference<int>(s_base_length);
#endif
        ref var extraDbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraDbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_dbits);
#endif
        ref var extraLbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraLbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_lbits);
#endif
        ref var extraBlbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraBlbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_blbits);
#endif
        BlockState state;
        for (;;)
        {
            /* Make sure that we always have enough lookahead, except
             * at the end of the input file. We need MaxMatch bytes
             * for the longest run, plus one for the unrolled loop.
             */
            if (s.Lookahead <= MaxMatch)
            {
                FillWindow(ref strm, ref window, ref sprev, ref head);
                if (s.Lookahead <= MaxMatch && flush == ZNoFlush)
                    return BlockState.NeedMore;

                if (s.Lookahead == 0)
                    break; // flush the current block
            }

            // See how many times the previous byte repeats
            s.MatchLength = 0;
            if (s.Lookahead >= MinMatch && s.Strstart > 0)
            {
                ref var scan = ref Unsafe.Add(ref window, s.Strstart - 1); // scan goes up to strend for length of run
                uint prev = scan; // byte at distance one to match
                if (prev == (scan = ref Unsafe.Add(ref scan, 1U))
                    && prev == (scan = ref Unsafe.Add(ref scan, 1U))
                    && prev == (scan = ref Unsafe.Add(ref scan, 1U)))
                {
                    ref var strend = ref Unsafe.Add(ref window, s.Strstart + MaxMatch);
                    do
                    {
                    } while (prev == (scan = ref Unsafe.Add(ref scan, 1U))
                        && prev == (scan = ref Unsafe.Add(ref scan, 1U))
                        && prev == (scan = ref Unsafe.Add(ref scan, 1U))
                        && prev == (scan = ref Unsafe.Add(ref scan, 1U))
                        && prev == (scan = ref Unsafe.Add(ref scan, 1U))
                        && prev == (scan = ref Unsafe.Add(ref scan, 1U))
                        && prev == (scan = ref Unsafe.Add(ref scan, 1U))
                        && prev == (scan = ref Unsafe.Add(ref scan, 1U))
                        && netUnsafe.IsAddressLessThan(ref scan, ref strend));

                    s.MatchLength = MaxMatch - (uint)netUnsafe.ByteOffset(ref scan, ref strend);
                    if (s.MatchLength > s.Lookahead)
                    {
                        s.MatchLength = s.Lookahead;
                    }
                }

                Debug.Assert(netUnsafe.IsAddressGreaterThan(ref Unsafe.Add(ref window, s.WindowSize - 1), ref scan), "wild scan");
            }

            // Emit match if have run of MinMatch or longer, else emit literal
            bool bflush; // set if current block must be flushed
            if (s.MatchLength >= MinMatch)
            {
                TreeTallyDist(s, 1, s.MatchLength - MinMatch, out bflush,
                    ref pendingBuf, ref dynLtree, ref dynDtree, ref distCode, ref lengthCode);

                s.Lookahead -= s.MatchLength;
                s.Strstart += s.MatchLength;
                s.MatchLength = 0;
            }
            else
            {
                // No match, output a literal byte
                var b = Unsafe.Add(ref window, s.Strstart);
                Trace.Tracevv($"{Convert.ToChar(b)}");
                TreeTallyLit(s, b, out bflush, ref pendingBuf, ref dynLtree, ref dynDtree,
                    ref distCode, ref lengthCode);
                s.Lookahead--;
                s.Strstart++;
            }

            if (bflush && FlushBlock(ref strm, 0, ref window, out state, ref pendingBuf, ref pendingOut,
                    ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                    ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                    ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
                return state;
        }

        s.Insert = 0;
        if (flush == ZFinish)
        {
            if (FlushBlock(ref strm, 1, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
                return state;

            return BlockState.FinishDone;
        }

        if (s.SymNext != 0 && FlushBlock(ref strm, 0, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
            return state;

        return BlockState.BlockDone;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TreeTallyDist(DeflateState s, uint distance, uint length, out bool flush,
        ref byte pendingBuf, ref TreeNode dynLtree, ref TreeNode dynDtree, ref byte distCode,
        ref byte lengthCode)
#if DEBUG
    {
        var len = (byte)length;
        var dist = (ushort)distance;
        Unsafe.Add(ref pendingBuf, s.LitBufsize + s.SymNext++) = (byte)dist;
        Unsafe.Add(ref pendingBuf, s.LitBufsize + s.SymNext++) = (byte)(dist >> 8);
        Unsafe.Add(ref pendingBuf, s.LitBufsize + s.SymNext++) = len;
        dist--;
        Unsafe.Add(ref dynLtree, (uint)(SLengthCode[len] + Literals + 1)).fc++;
        Unsafe.Add(ref dynDtree, Tree.DCode(dist, ref distCode)).fc++;
        flush = s.SymNext == s.SymEnd;
    }
#else
     => flush = Tree.Tally(s, distance, length, ref pending_buf, ref dyn_ltree, ref dyn_dtree,
         ref dist_code, ref length_code);
#endif

    private static BlockState DeflateFast(ref ZStream strm, int flush, ref byte pendingBuf, ref byte pendingOut)
    {
        var s = strm.DeflateState;
#if NET7_0_OR_GREATER
        ref var refs = ref strm.DeflateRefs;
        InitRefFields(s, ref refs);
#endif
        ref var window = ref
#if NET7_0_OR_GREATER
            refs.Window;
#else
            MemoryMarshal.GetReference<byte>(s.window);
#endif
        ref var prev = ref
#if NET7_0_OR_GREATER
            refs.Prev;
#else
            MemoryMarshal.GetReference<ushort>(s.prev);
#endif
        ref var head = ref
#if NET7_0_OR_GREATER
            refs.Head;
#else
            MemoryMarshal.GetReference<ushort>(s.head);
#endif
        ref var blCount = ref
#if NET7_0_OR_GREATER
            refs.BlCount;
#else
            MemoryMarshal.GetReference<ushort>(s.bl_count);
#endif
        ref var heap = ref
#if NET7_0_OR_GREATER
            refs.Heap;
#else
            MemoryMarshal.GetReference<int>(s.heap);
#endif
        ref var depth = ref
#if NET7_0_OR_GREATER
            refs.Depth;
#else
            MemoryMarshal.GetReference<byte>(s.depth);
#endif

        ref var staLtree = ref
#if NET7_0_OR_GREATER
            refs.StaLtree;
#else
            MemoryMarshal.GetReference<TreeNode>(Tree.s_ltree);
#endif
        ref var staDtree = ref
#if NET7_0_OR_GREATER
            refs.StaDtree;
#else
            MemoryMarshal.GetReference<TreeNode>(Tree.s_dtree);
#endif
        ref var dynLtree = ref
#if NET7_0_OR_GREATER
            refs.DynLtree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.dyn_ltree);
#endif
        ref var dynDtree = ref
#if NET7_0_OR_GREATER
            refs.DynDtree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.dyn_dtree);
#endif
        ref var blTree = ref
#if NET7_0_OR_GREATER
            refs.BlTree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.bl_tree);
#endif
        ref var blOrder = ref
#if NET7_0_OR_GREATER
            refs.BlOrder;
#else
            MemoryMarshal.GetReference<ushort>(s_bl_order);
#endif
        ref var distCode = ref
#if NET7_0_OR_GREATER
            refs.DistCode;
#else
            MemoryMarshal.GetReference<byte>(s_dist_code);
#endif
        ref var lengthCode = ref
#if NET7_0_OR_GREATER
            refs.LengthCode;
#else
            MemoryMarshal.GetReference<byte>(s_length_code);
#endif
        ref var baseDist = ref
#if NET7_0_OR_GREATER
            refs.BaseDist;
#else
            MemoryMarshal.GetReference<int>(s_base_dist);
#endif
        ref var baseLength = ref
#if NET7_0_OR_GREATER
            refs.BaseLength;
#else
            MemoryMarshal.GetReference<int>(s_base_length);
#endif
        ref var extraDbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraDbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_dbits);
#endif
        ref var extraLbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraLbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_lbits);
#endif
        ref var extraBlbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraBlbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_blbits);
#endif
        BlockState state;
        for (;;)
        {
            /* Make sure that we always have enough lookahead, except
             * at the end of the input file. We need MaxMatch bytes
             * for the next match, plus MinMatch bytes to insert the
             * string following the next match.
             */
            if (s.Lookahead < MinLookAhead)
            {
                FillWindow(ref strm, ref window, ref prev, ref head);
                if (s.Lookahead < MinLookAhead && flush == ZNoFlush)
                    return BlockState.NeedMore;

                if (s.Lookahead == 0)
                    break; // flush the current block
            }

            /* Insert the string window[strstart .. strstart+2] in the
             * dictionary, and set hash_head to the head of the hash chain:
             */
            uint hashHead = 0; // head of the hash chain
            if (s.Lookahead >= MinMatch)
                InsertString(s, s.Strstart, ref hashHead, ref window, ref prev, ref head);

            /* Find the longest match, discarding those <= prev_length.
             * At this point we have always match_length < MinMatch
             */
            if (hashHead != 0 && s.Strstart - hashHead <= MaxDist(s))
            {
                /* To simplify the code, we prevent matches with the string
                 * of window index 0 (in particular we have to avoid a match
                 * of the string with itself at the start of the input file).
                 */
                s.MatchLength = LongestMatch(s, hashHead, ref window, ref prev);
                // LongestMatch() sets match_start
            }

            bool bflush; // set if current block must be flushed
            if (s.MatchLength >= MinMatch)
            {
                TreeTallyDist(s, s.Strstart - s.MatchStart, s.MatchLength - MinMatch, out bflush, ref pendingBuf,
                    ref dynLtree, ref dynDtree, ref distCode, ref lengthCode);

                s.Lookahead -= s.MatchLength;

                /* Insert new strings in the hash table only if the match length
                 * is not too large. This saves time but degrades compression.
                 */
                if (s.MatchLength <= s.MaxLazyMatch &&
                    s.Lookahead >= MinMatch)
                {
                    s.MatchLength--; // string at strstart already in table
                    do
                    {
                        s.Strstart++;
                        InsertString(s, s.Strstart, ref hashHead, ref window, ref prev, ref head);
                        /* strstart never exceeds WSize-MaxMatch, so there are
                         * always MinMatch bytes ahead.
                         */
                    } while (--s.MatchLength != 0);

                    s.Strstart++;
                }
                else
                {
                    s.Strstart += s.MatchLength;
                    s.MatchLength = 0;
                    s.InsH = Unsafe.Add(ref window, s.Strstart);
                    UpdateHash(s, ref s.InsH, Unsafe.Add(ref window, s.Strstart + 1));

                    /* If lookahead < MinMatch, ins_h is garbage, but it does not
                     * matter since it will be recomputed at next deflate call.
                     */
                }
            }
            else
            {
                // No match, output a literal byte
                var b = Unsafe.Add(ref window, s.Strstart);
                Trace.Tracevv($"{Convert.ToChar(b)}");
                TreeTallyLit(s, b, out bflush, ref pendingBuf, ref dynLtree, ref dynDtree,
                    ref distCode, ref lengthCode);
                s.Lookahead--;
                s.Strstart++;
            }

            if (bflush && FlushBlock(ref strm, 0, ref window, out state, ref pendingBuf, ref pendingOut,
                    ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                    ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                    ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
                return state;
        }

        s.Insert = s.Strstart < MinMatch - 1 ? s.Strstart : MinMatch - 1;
        if (flush == ZFinish)
        {
            if (FlushBlock(ref strm, 1, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
                return state;

            return BlockState.FinishDone;
        }

        if (s.SymNext != 0 && FlushBlock(ref strm, 0, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
            return state;

        return BlockState.BlockDone;
    }

    private static void InsertString(DeflateState s, uint str, ref uint matchHead,
        ref byte window, ref ushort prev, ref ushort head)
    {
        UpdateHash(s, ref s.InsH, Unsafe.Add(ref window, str + (MinMatch - 1)));
        ref var temp = ref Unsafe.Add(ref head, s.InsH);
        matchHead = Unsafe.Add(ref prev, (str) & s.WMask) = temp;
        temp = (ushort)str;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint MaxDist(DeflateState s)
    {
        return s.WSize - MinLookAhead;
    }

    private static uint LongestMatch(DeflateState s, uint curMatch, ref byte window, ref ushort prev)
    {
        var chainLength = s.MaxChainLength; // max hash chain length
        ref var scan = ref Unsafe.Add(ref window, s.Strstart); // current string
        var bestLen = (int)s.PrevLength; // best match length so far
        var niceMatch = s.NiceMatch; // stop if match long enough
        var limit = s.Strstart > MaxDist(s) ? s.Strstart - MaxDist(s) : 0;
        /* Stop when cur_match becomes <= limit. To simplify the code,
         * we prevent matches with the string of window index 0.
         */
        var wmask = s.WMask;
        ref var strend = ref Unsafe.Add(ref window, s.Strstart + MaxMatch);
        var scanEnd1 = Unsafe.Add(ref scan, bestLen - 1);
        var scanEnd = Unsafe.Add(ref scan, bestLen);

        /* The code is optimized for HASH_BITS >= 8 and MaxMatch-2 multiple of 16.
         * It is easy to get rid of this optimization if necessary.
         */
        Debug.Assert(s.HashBits >= 8, "Code too clever");

        // Do not waste too much time if we already have a good match:
        if (s.PrevLength >= s.GoodMatch)
        {
            chainLength >>= 2;
        }

        /* Do not look for matches beyond the end of the input. This is necessary
         * to make deflate deterministic.
         */
        if (niceMatch > s.Lookahead)
        {
            niceMatch = (int)s.Lookahead;
        }

        Debug.Assert(s.Strstart <= s.WindowSize - MinLookAhead, "need lookahead");

        do
        {
            Debug.Assert(curMatch < s.Strstart, "no future");
            ref var match = ref Unsafe.Add(ref window, curMatch); // matched string

            /* Skip to next match if the match length cannot increase
             * or if the match length is less than 2.  Note that the checks below
             * for insufficient lookahead only occur occasionally for performance
             * reasons.  Therefore uninitialized memory will be accessed, and
             * conditional jumps will be made that depend on those values.
             * However the length of the match is limited to the lookahead, so
             * the output of deflate is not affected by the uninitialized values.
             */

            if (Unsafe.Add(ref match, bestLen) != scanEnd
                || Unsafe.Add(ref match, bestLen - 1) != scanEnd1
                || match != scan
                || (match = ref Unsafe.Add(ref match, 1U)) != Unsafe.Add(ref scan, 1U))
                continue;

            /* The check at best_len-1 can be removed because it will be made
             * again later. (This heuristic is not always a win.)
             * It is not necessary to compare scan[2] and match[2] since they
             * are always equal when the other bytes match, given that
             * the hash keys are equal and that HASH_BITS >= 8.
             */
            scan = ref Unsafe.Add(ref scan, 2U);
            match = ref Unsafe.Add(ref match, 1U);

            Debug.Assert(scan == match, "match[2]?");

            /* We check for insufficient lookahead only every 8th comparison;
             * the 256th check will be made at strstart + 258.
             */
            do
            {
            } while ((scan = ref Unsafe.Add(ref scan, 1U)) == (match = ref Unsafe.Add(ref match, 1U))
                && (scan = ref Unsafe.Add(ref scan, 1U)) == (match = ref Unsafe.Add(ref match, 1U))
                && (scan = ref Unsafe.Add(ref scan, 1U)) == (match = ref Unsafe.Add(ref match, 1U))
                && (scan = ref Unsafe.Add(ref scan, 1U)) == (match = ref Unsafe.Add(ref match, 1U))
                && (scan = ref Unsafe.Add(ref scan, 1U)) == (match = ref Unsafe.Add(ref match, 1U))
                && (scan = ref Unsafe.Add(ref scan, 1U)) == (match = ref Unsafe.Add(ref match, 1U))
                && (scan = ref Unsafe.Add(ref scan, 1U)) == (match = ref Unsafe.Add(ref match, 1U))
                && (scan = ref Unsafe.Add(ref scan, 1U)) == (match = ref Unsafe.Add(ref match, 1U))
                && netUnsafe.IsAddressLessThan(ref scan, ref strend));

            Debug.Assert(scan <= window + (s.WindowSize - 1), "wild scan");

            var len = MaxMatch - (int)netUnsafe.ByteOffset(ref scan, ref strend); // length of current match
            scan = ref Unsafe.Subtract(ref strend, (uint)MaxMatch);

            if (len > bestLen)
            {
                s.MatchStart = curMatch;
                bestLen = len;
                if (len >= niceMatch)
                    break;

                scanEnd1 = Unsafe.Add(ref scan, bestLen - 1);
                scanEnd = Unsafe.Add(ref scan, bestLen);
            }
        } while ((curMatch = Unsafe.Add(ref prev, curMatch & wmask)) > limit && --chainLength != 0);

        if (bestLen <= s.Lookahead)
            return (uint)bestLen;

        return s.Lookahead;
    }

    private static BlockState DeflateSlow(ref ZStream strm, int flush, ref byte pendingBuf, ref byte pendingOut)
    {
        var s = strm.DeflateState;
#if NET7_0_OR_GREATER
        ref var refs = ref strm.DeflateRefs;
        InitRefFields(s, ref refs);
#endif
        ref var window = ref
#if NET7_0_OR_GREATER
            refs.Window;
#else
            MemoryMarshal.GetReference<byte>(s.window);
#endif
        ref var prev = ref
#if NET7_0_OR_GREATER
            refs.Prev;
#else
            MemoryMarshal.GetReference<ushort>(s.prev);
#endif
        ref var head = ref
#if NET7_0_OR_GREATER
            refs.Head;
#else
            MemoryMarshal.GetReference<ushort>(s.head);
#endif
        ref var blCount = ref
#if NET7_0_OR_GREATER
            refs.BlCount;
#else
            MemoryMarshal.GetReference<ushort>(s.bl_count);
#endif
        ref var heap = ref
#if NET7_0_OR_GREATER
            refs.Heap;
#else
            MemoryMarshal.GetReference<int>(s.heap);
#endif
        ref var depth = ref
#if NET7_0_OR_GREATER
            refs.Depth;
#else
            MemoryMarshal.GetReference<byte>(s.depth);
#endif

        ref var staLtree = ref
#if NET7_0_OR_GREATER
            refs.StaLtree;
#else
            MemoryMarshal.GetReference<TreeNode>(Tree.s_ltree);
#endif
        ref var staDtree = ref
#if NET7_0_OR_GREATER
            refs.StaDtree;
#else
            MemoryMarshal.GetReference<TreeNode>(Tree.s_dtree);
#endif
        ref var dynLtree = ref
#if NET7_0_OR_GREATER
            refs.DynLtree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.dyn_ltree);
#endif
        ref var dynDtree = ref
#if NET7_0_OR_GREATER
            refs.DynDtree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.dyn_dtree);
#endif
        ref var blTree = ref
#if NET7_0_OR_GREATER
            refs.BlTree;
#else
            MemoryMarshal.GetReference<TreeNode>(s.bl_tree);
#endif
        ref var blOrder = ref
#if NET7_0_OR_GREATER
            refs.BlOrder;
#else
            MemoryMarshal.GetReference<ushort>(s_bl_order);
#endif
        ref var distCode = ref
#if NET7_0_OR_GREATER
            refs.DistCode;
#else
            MemoryMarshal.GetReference<byte>(s_dist_code);
#endif
        ref var lengthCode = ref
#if NET7_0_OR_GREATER
            refs.LengthCode;
#else
            MemoryMarshal.GetReference<byte>(s_length_code);
#endif
        ref var baseDist = ref
#if NET7_0_OR_GREATER
            refs.BaseDist;
#else
            MemoryMarshal.GetReference<int>(s_base_dist);
#endif
        ref var baseLength = ref
#if NET7_0_OR_GREATER
            refs.BaseLength;
#else
            MemoryMarshal.GetReference<int>(s_base_length);
#endif
        ref var extraDbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraDbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_dbits);
#endif
        ref var extraLbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraLbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_lbits);
#endif
        ref var extraBlbits = ref
#if NET7_0_OR_GREATER
            refs.ExtraBlbits;
#else
            MemoryMarshal.GetReference<int>(s_extra_blbits);
#endif
        BlockState state;
        // Process the input block.
        for (;;)
        {
            /* Make sure that we always have enough lookahead, except
             * at the end of the input file. We need MaxMatch bytes
             * for the next match, plus MinMatch bytes to insert the
             * string following the next match.
             */
            if (s.Lookahead < MinLookAhead)
            {
                FillWindow(ref strm, ref window, ref prev, ref head);
                if (s.Lookahead < MinLookAhead && flush == ZNoFlush)
                {
                    return BlockState.NeedMore;
                }

                if (s.Lookahead == 0)
                    break; // flush the current block
            }

            /* Insert the string window[strstart .. strstart+2] in the
             * dictionary, and set hash_head to the head of the hash chain:
             */
            uint hashHead = 0; // head of hash chain
            if (s.Lookahead >= MinMatch)
                InsertString(s, s.Strstart, ref hashHead, ref window, ref prev, ref head);

            // Find the longest match, discarding those <= prev_length.
            s.PrevLength = s.MatchLength;
            s.PrevMatch = s.MatchStart;
            s.MatchLength = MinMatch - 1;

            if (hashHead != 0 && s.PrevLength < s.MaxLazyMatch &&
                s.Strstart - hashHead <= MaxDist(s))
            {
                /* To simplify the code, we prevent matches with the string
                 * of window index 0 (in particular we have to avoid a match
                 * of the string with itself at the start of the input file).
                 */
                s.MatchLength = LongestMatch(s, hashHead, ref window, ref prev);
                // LongestMatch() sets match_start

                if (s.MatchLength <= 5 && (s.Strategy == ZFiltered
                    || (s.MatchLength == MinMatch && s.Strstart - s.MatchStart > TooFar)))
                {
                    /* If prev_match is also MinMatch, match_start is garbage
                     * but we will ignore the current match anyway.
                     */
                    s.MatchLength = MinMatch - 1;
                }
            }

            /* If there was a match at the previous step and the current
             * match is not better, output the previous match:
             */
            bool bflush; // set if current block must be flushed
            if (s.PrevLength >= MinMatch && s.MatchLength <= s.PrevLength)
            {
                var maxInsert = s.Strstart + s.Lookahead - MinMatch;
                // Do not insert strings in hash table beyond this.

                TreeTallyDist(s, s.Strstart - 1 - s.PrevMatch, s.PrevLength - MinMatch, out bflush, ref pendingBuf,
                    ref dynLtree, ref dynDtree, ref distCode, ref lengthCode);

                /* Insert in hash table all strings up to the end of the match.
                 * strstart-1 and strstart are already inserted. If there is not
                 * enough lookahead, the last two strings are not inserted in
                 * the hash table.
                 */
                s.Lookahead -= s.PrevLength - 1;
                s.PrevLength -= 2;
                do
                {
                    if (++s.Strstart <= maxInsert)
                        InsertString(s, s.Strstart, ref hashHead, ref window, ref prev, ref head);
                } while (--s.PrevLength != 0);

                s.MatchAvailable = false;
                s.MatchLength = MinMatch - 1;
                s.Strstart++;

                if (bflush && FlushBlock(ref strm, 0, ref window, out state, ref pendingBuf, ref pendingOut,
                    ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                    ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                    ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
                    return state;
            }
            else if (s.MatchAvailable)
            {
                /* If there was no match at the previous position, output a
                 * single literal. If there was a match but the current match
                 * is longer, truncate the previous match to a single literal.
                 */
                var c = Unsafe.Add(ref window, s.Strstart - 1);
                Trace.Tracevv($"{Convert.ToChar(c)}");
                TreeTallyLit(s, c, out bflush, ref pendingBuf, ref dynLtree, ref dynDtree,
                    ref distCode, ref lengthCode);
                if (bflush)
                    FlushBlockOnly(ref strm, 0, ref pendingBuf, ref pendingOut, ref window, ref staLtree,
                        ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount, ref heap,
                        ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist, ref baseLength,
                        ref extraDbits, ref extraLbits, ref extraBlbits);
                s.Strstart++;
                s.Lookahead--;
                if (strm.AvailOut == 0)
                    return BlockState.NeedMore;
            }
            else
            {
                // There is no previous match to compare with, wait for the next step to decide.
                s.MatchAvailable = true;
                s.Strstart++;
                s.Lookahead--;
            }
        }

        Debug.Assert(flush != ZNoFlush, "no flush?");
        if (s.MatchAvailable)
        {
            var b = Unsafe.Add(ref window, s.Strstart - 1);
            Trace.Tracevv($"{Convert.ToChar(b)}");
            TreeTallyLit(s, b, out _, ref pendingBuf, ref dynLtree, ref dynDtree,
                ref distCode, ref lengthCode);
            s.MatchAvailable = false;
        }

        s.Insert = s.Strstart < MinMatch - 1 ? s.Strstart : MinMatch - 1;
        if (flush == ZFinish)
        {
            if (FlushBlock(ref strm, 1, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
                return state;

            return BlockState.FinishDone;
        }

        if (s.SymNext != 0 && FlushBlock(ref strm, 0, ref window, out state, ref pendingBuf, ref pendingOut,
                ref staLtree, ref staDtree, ref dynLtree, ref dynDtree, ref blTree, ref blCount,
                ref heap, ref depth, ref blOrder, ref distCode, ref lengthCode, ref baseDist,
                ref baseLength, ref extraDbits, ref extraLbits, ref extraBlbits))
            return state;

        return BlockState.BlockDone;
    }

#if NET7_0_OR_GREATER
    private static void InitRefFields(DeflateState s, ref DeflateRefs refs)
    {
        if (netUnsafe.IsNullRef(ref refs.BlOrder))
        {
            refs.Window = ref MemoryMarshal.GetReference<byte>(s.Window);
            refs.Prev = ref MemoryMarshal.GetReference<ushort>(s.Prev);
            refs.BlCount = ref MemoryMarshal.GetReference<ushort>(s.BlCount);
            refs.Heap = ref MemoryMarshal.GetReference<int>(s.Heap);
            refs.Depth = ref MemoryMarshal.GetReference<byte>(s.Depth);
            refs.StaLtree = ref MemoryMarshal.GetReference<TreeNode>(Tree.SLtree);
            refs.StaDtree = ref MemoryMarshal.GetReference<TreeNode>(Tree.SDtree);
            refs.BlOrder = ref MemoryMarshal.GetReference<ushort>(SBlOrder);
            refs.DistCode = ref MemoryMarshal.GetReference<byte>(SDistCode);
            refs.LengthCode = ref MemoryMarshal.GetReference<byte>(SLengthCode);
            refs.BaseDist = ref MemoryMarshal.GetReference<int>(SBaseDist);
            refs.BaseLength = ref MemoryMarshal.GetReference<int>(SBaseLength);
            refs.ExtraDbits = ref MemoryMarshal.GetReference<int>(SExtraDbits);
            refs.ExtraLbits = ref MemoryMarshal.GetReference<int>(SExtraLbits);
            refs.ExtraBlbits = ref MemoryMarshal.GetReference<int>(SExtraBlbits);
        }
    }
#endif
}