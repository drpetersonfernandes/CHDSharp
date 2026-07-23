namespace CHDSharpEncoder;

public class HunkProcessor
{
    private readonly uint _hunkBytes;

    public HunkProcessor(uint hunkBytes)
    {
        _hunkBytes = hunkBytes;
    }

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
