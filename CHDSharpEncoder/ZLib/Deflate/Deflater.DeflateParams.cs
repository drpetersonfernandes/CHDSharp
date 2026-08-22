#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly and Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

using System.Runtime.InteropServices;

namespace CHDSharpEncoder.ZLib.Deflate;

internal static partial class Deflater
{
    internal static int DeflateParams(ref ZStream strm, int level, int strategy)
    {
        if (DeflateStateCheck(ref strm))
            return ZStreamError;

        var s = strm.DeflateState;

        if (level == ZDefaultCompression)
        {
            level = 6;
        }

        if (level < 0 || level > 9 || strategy < 0 || strategy > ZFixed)
            return ZStreamError;

        ref var configurationTable = ref
#if NET7_0_OR_GREATER
            strm.DeflateRefs.ConfigurationTable;
#else
            MemoryMarshal.GetReference<Config>(s_configuration_table);
#endif
        var deflateType = Unsafe.Add(ref configurationTable, (uint)s.Level).deflate_type;
        ref var config = ref Unsafe.Add(ref configurationTable, (uint)level);
        if ((strategy != s.Strategy || deflateType != config.deflate_type)
            && s.LastFlush != -2)
        {
            // Flush the last buffer:
            var err = Deflate(ref strm, ZBlock);
            if (err == ZStreamError)
                return err;
            if (strm.AvailIn != 0 || s.Strstart - s.BlockStart + s.Lookahead != 0)
                return ZBufError;
        }

        if (s.Level != level)
        {
            if (s.Level == 0 && s.Matches != 0)
            {
                if (s.Matches == 1)
                {
#if NET7_0_OR_GREATER
                    ref var refs = ref strm.DeflateRefs;
                    if (netUnsafe.IsNullRef(ref refs.Prev))
                    {
                        refs.Prev = ref MemoryMarshal.GetReference<ushort>(s.Prev);
                    }
#endif
                    ref var prev = ref
#if NET7_0_OR_GREATER
                        refs.Prev;
#else
                    MemoryMarshal.GetReference<ushort>(s.prev);
#endif

                    SlideHash(s, ref prev, ref
#if NET7_0_OR_GREATER
                        refs.Head
#else
                    MemoryMarshal.GetReference<ushort>(s.head)
#endif
                    );
                }
                else
                {
                    ClearHash(ref strm);
                }

                s.Matches = 0;
            }

            s.Level = level;
            s.MaxLazyMatch = config.max_lazy;
            s.GoodMatch = config.good_length;
            s.NiceMatch = config.nice_length;
            s.MaxChainLength = config.max_chain;
        }

        s.Strategy = strategy;
        return ZOk;
    }
}