namespace CHDSharpEncoder.LZMA;

/// <summary>Progress callback interface, ported from the LZMA SDK (public domain).</summary>
internal interface ICodeProgress
{
    /// <summary>Reports compression progress.</summary>
    /// <param name="inSize">Input size processed; -1 if unknown.</param>
    /// <param name="outSize">Output size produced; -1 if unknown.</param>
    void SetProgress(long inSize, long outSize);
}

/// <summary>Stream coder interface, ported from the LZMA SDK (public domain).</summary>
internal interface ICoder
{
    /// <summary>Codes streams.</summary>
    void Code(Stream inStream, Stream outStream, long inSize, long outSize, ICodeProgress? progress);
}

/// <summary>Provides the fields that represent property identifiers for compressing.</summary>
internal enum CoderPropID
{
    /// <summary>Specifies default property.</summary>
    DefaultProp = 0,

    /// <summary>Specifies size of dictionary.</summary>
    DictionarySize,

    /// <summary>Specifies size of memory for PPM*.</summary>
    UsedMemorySize,

    /// <summary>Specifies order for PPM methods.</summary>
    Order,

    /// <summary>Specifies Block Size.</summary>
    BlockSize,

    /// <summary>Specifies number of postion state bits for LZMA (0 &lt;= x &lt;= 4).</summary>
    PosStateBits,

    /// <summary>Specifies number of literal context bits for LZMA (0 &lt;= x &lt;= 8).</summary>
    LitContextBits,

    /// <summary>Specifies number of literal position bits for LZMA (0 &lt;= x &lt;= 4).</summary>
    LitPosBits,

    /// <summary>Specifies number of fast bytes for LZ*.</summary>
    NumFastBytes,

    /// <summary>Specifies match finder. LZMA: "BT2", "BT4" or "BT4B".</summary>
    MatchFinder,

    /// <summary>Specifies the number of match finder cyckes.</summary>
    MatchFinderCycles,

    /// <summary>Specifies number of passes.</summary>
    NumPasses,

    /// <summary>Specifies number of algorithm.</summary>
    Algorithm,

    /// <summary>Specifies the number of threads.</summary>
    NumThreads,

    /// <summary>Specifies mode with end marker.</summary>
    EndMarker,
}

/// <summary>Coder property configuration interface, ported from the LZMA SDK (public domain).</summary>
internal interface ISetCoderProperties
{
    /// <summary>Sets coder properties.</summary>
    void SetCoderProperties(CoderPropID[] propIDs, object[] properties);
}

/// <summary>Coder property writer interface, ported from the LZMA SDK (public domain).</summary>
internal interface IWriteCoderProperties
{
    /// <summary>Writes the coder properties to a stream.</summary>
    void WriteCoderProperties(Stream outStream);
}