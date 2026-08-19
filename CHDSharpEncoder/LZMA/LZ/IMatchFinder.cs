namespace CHDSharpEncoder.LZMA.LZ;

/// <summary>Input window stream interface, ported from the LZMA SDK (public domain).</summary>
internal interface IInWindowStream
{
    /// <summary>Sets the input stream.</summary>
    void SetStream(Stream inStream);

    /// <summary>Initialises the window.</summary>
    void Init();

    /// <summary>Releases the input stream.</summary>
    void ReleaseStream();

    /// <summary>Gets a byte from the window at the given index relative to the current position.</summary>
    byte GetIndexByte(int index);

    /// <summary>Gets the length of a match at the given index, distance and limit.</summary>
    uint GetMatchLen(int index, uint distance, uint limit);

    /// <summary>Gets the number of bytes available in the window.</summary>
    uint GetNumAvailableBytes();
}

/// <summary>Match finder interface, ported from the LZMA SDK (public domain).</summary>
internal interface IMatchFinder : IInWindowStream
{
    /// <summary>Creates the match finder's internal buffers.</summary>
    void Create(uint historySize, uint keepAddBufferBefore, uint matchMaxLen, uint keepAddBufferAfter);

    /// <summary>Fills <paramref name="distances"/> with (length, distance) pairs; returns the number of values written.</summary>
    uint GetMatches(uint[] distances);

    /// <summary>Skips <paramref name="num"/> positions without emitting matches.</summary>
    void Skip(uint num);
}