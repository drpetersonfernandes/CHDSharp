namespace CHDSharpEncoder;

/// <summary>Processes raw hunk data for CHD v5 encoding, handling compression and map entry generation.</summary>
public class HunkProcessor
{
    private readonly uint _hunkBytes;

    /// <summary>Initializes a new <see cref="HunkProcessor"/> for the specified hunk size.</summary>
    /// <param name="hunkBytes">The expected size of each hunk in bytes.</param>
    public HunkProcessor(uint hunkBytes)
    {
        _hunkBytes = hunkBytes;
    }

    /// <summary>Compresses a raw hunk and produces its map entry and output data.</summary>
    /// <param name="rawHunk">The uncompressed hunk data.</param>
    /// <param name="fileOffset">The byte offset of this hunk in the output file.</param>
    /// <returns>A tuple containing the map entry and the data to write (compressed or raw).</returns>
    public (MapEntry Entry, byte[] Data) ProcessHunk(byte[] rawHunk, long fileOffset)
    {
        if (rawHunk.Length != _hunkBytes)
            throw new ArgumentException($"Hunk size mismatch: expected {_hunkBytes}, got {rawHunk.Length}");

        var crc16 = Crc16.Compute(rawHunk);
        var compressed = RawDeflate.Compress(rawHunk);

        if (compressed != null && compressed.Length < _hunkBytes)
        {
            return (
                new MapEntry
                {
                    Compression = MapEntry.COMPRESSION_TYPE_0,
                    CompLength = (uint)compressed.Length,
                    Offset = (ulong)fileOffset,
                    Crc16 = crc16,
                },
                compressed
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
