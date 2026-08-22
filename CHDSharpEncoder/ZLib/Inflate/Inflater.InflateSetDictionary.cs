#nullable disable
// Original code and comments Copyright (C) 1995-2024 Mark Adler
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Inflate;

internal static partial class Inflater
{
    internal static int InflateSetDictionary(ref ZStream strm, ref byte dictionary, uint dictLength)
    {
        if (InflateStateCheck(ref strm))
            return Z_STREAM_ERROR;

        var state = strm.inflateState;
        if (state.Wrap != 0 && state.Mode != InflateMode.Dict)
            return Z_STREAM_ERROR;

        // check for correct dictionary identifier
        if (state.Mode == InflateMode.Dict)
        {
            var dictid = Adler32.Update(0, ref netUnsafe.NullRef<byte>(), 0);
            dictid = Adler32.Update(dictid, ref dictionary, dictLength);
            if (dictid != state.Check)
                return Z_DATA_ERROR;
        }

        // copy dictionary to window using updatewindow(), which will amend the existing dictionary if appropriate
        try
        {
            UpdateWindow(ref strm, ref Unsafe.Add(ref dictionary, dictLength), dictLength,
                ref netUnsafe.NullRef<byte>());
        }
        catch (OutOfMemoryException)
        {
            state.Mode = InflateMode.Mem;
            return Z_MEM_ERROR;
        }

        state.Havedict = 1;
        Trace.Tracev("inflate:   dictionary set\n");
        return Z_OK;
    }
}