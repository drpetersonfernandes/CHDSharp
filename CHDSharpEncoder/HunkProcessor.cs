namespace CHDSharpEncoder;

/// <summary>Processes raw hunk data for CHD v5 encoding, handling compression and map entry generation.</summary>
public class HunkProcessor
{
    private readonly uint _hunkBytes;
    private readonly IChdCodec[] _codecs;

    /// <summary>Initializes a new <see cref="HunkProcessor"/> for the specified hunk size.</summary>
    /// <param name="hunkBytes">The expected size of each hunk in bytes.</param>
    public HunkProcessor(uint hunkBytes)
        : this(hunkBytes, [new ZlibCodec()])
    {
    }

    /// <summary>Initializes a new <see cref="HunkProcessor"/> with the given codecs.</summary>
    /// <param name="hunkBytes">The expected size of each hunk in bytes.</param>
    /// <param name="codecs">The codecs to try per hunk, in order; the smallest output wins
    /// (compression types 0..3 map to codec indices, like MAME's <c>find_best_compressor</c>).</param>
    public HunkProcessor(uint hunkBytes, IReadOnlyList<IChdCodec> codecs)
    {
        _hunkBytes = hunkBytes;
        _codecs = codecs.ToArray();
    }

    /// <summary>Compresses a raw hunk with the best available codec and produces its map entry and output data.</summary>
    /// <param name="rawHunk">The uncompressed hunk data.</param>
    /// <param name="fileOffset">The byte offset of this hunk in the output file.</param>
    /// <returns>A tuple containing the map entry and the data to write (compressed or raw).</returns>
    public (MapEntry Entry, byte[] Data) ProcessHunk(byte[] rawHunk, long fileOffset)
    {
        if (rawHunk.Length != _hunkBytes)
            throw new ArgumentException($"Hunk size mismatch: expected {_hunkBytes}, got {rawHunk.Length}");

        var crc16 = Crc16.Compute(rawHunk);

        // try every codec and keep the smallest result that saves space
        int bestCodec = -1;
        byte[]? bestData = null;
        for (int i = 0; i < _codecs.Length; i++)
        {
            var candidate = _codecs[i].Compress(rawHunk);
            if (candidate != null && (bestData == null || candidate.Length < bestData.Length))
            {
                bestCodec = i;
                bestData = candidate;
            }
        }

        if (bestCodec >= 0)
        {
            return (
                new MapEntry
                {
                    Compression = (byte)bestCodec,
                    CompLength = (uint)bestData!.Length,
                    Offset = (ulong)fileOffset,
                    Crc16 = crc16,
                },
                bestData
            );
        }

        return (
            new MapEntry
            {
                Compression = MapEntry.COMPRESSION_NONE,
                CompLength = _hunkBytes,
                Offset = (ulong)fileOffset,
                Crc16 = crc16,
            },
            (byte[])rawHunk.Clone()
        );
    }
}