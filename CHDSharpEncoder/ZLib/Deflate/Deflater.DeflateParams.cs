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
            return Z_STREAM_ERROR;

        var s = strm.deflateState;

        if (level == Z_DEFAULT_COMPRESSION)
        {
            level = 6;
        }

        if (level < 0 || level > 9 || strategy < 0 || strategy > Z_FIXED)
            return Z_STREAM_ERROR;

        ref var configuration_table = ref
#if NET7_0_OR_GREATER
            strm.deflateRefs.ConfigurationTable;
#else
            MemoryMarshal.GetReference<Config>(s_configuration_table);
#endif
        var deflate_type = Unsafe.Add(ref configuration_table, (uint)s.Level).deflate_type;
        ref var config = ref Unsafe.Add(ref configuration_table, (uint)level);
        if ((strategy != s.Strategy || deflate_type != config.deflate_type)
            && s.LastFlush != -2)
        {
            // Flush the last buffer:
            var err = Deflate(ref strm, Z_BLOCK);
            if (err == Z_STREAM_ERROR)
                return err;
            if (strm.avail_in != 0 || s.Strstart - s.BlockStart + s.Lookahead != 0)
                return Z_BUF_ERROR;
        }
        if (s.Level != level)
        {
            if (s.Level == 0 && s.Matches != 0)
            {
                if (s.Matches == 1)
                {
#if NET7_0_OR_GREATER
                    ref var refs = ref strm.deflateRefs;
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
        return Z_OK;
    }
}