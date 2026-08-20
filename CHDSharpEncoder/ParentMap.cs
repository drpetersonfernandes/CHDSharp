using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoder;

/// <summary>
/// Builds the parent-hunk hash map used to create differential (delta) CHDs. The parent CHD
/// is decompressed once, and every unit-aligned window of its data (one hunk's worth of bytes
/// starting at each unit boundary) is hashed; during encoding, a child hunk whose full-hunk
/// (CRC-16, SHA-1) matches a window is emitted as a <see cref="MapEntry.CompressionParent"/>
/// reference to that parent unit — mirroring MAME's <c>chd_file_compressor</c> parent walk
/// (<c>chd.cpp</c>: hashes every parent unit window, then matches child hunks against it).
/// </summary>
/// <remarks>
/// The walk is sequential (the parent's hunks must be decompressed once; each read is cached
/// by the reader). The map itself is read-only after construction, so it is safe to consult
/// from the encoder's single consumer thread during the parallel pipeline.
/// </remarks>
public sealed class ParentMap : IDisposable
{
    private readonly ChdFile? _parent;
    private readonly Dictionary<(ushort Crc16, string Sha1Hex), uint> _map;

    /// <summary>Initializes a new parent map by decompressing and hashing the parent CHD.</summary>
    /// <param name="parentPath">Path of the parent CHD file.</param>
    /// <param name="hunkBytes">The hunk size of the child being created.</param>
    /// <param name="unitBytes">The unit size of the child being created.</param>
    /// <exception cref="ArgumentException"><paramref name="parentPath"/> is <c>null</c> or the
    /// parent's hunk/unit sizes do not match <paramref name="hunkBytes"/>/<paramref name="unitBytes"/>.</exception>
    /// <exception cref="IOException">The parent CHD cannot be opened.</exception>
    public ParentMap(string parentPath, uint hunkBytes, uint unitBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentPath);
        if (hunkBytes == 0 || unitBytes == 0 || hunkBytes % unitBytes != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of unitBytes ({unitBytes})");

        var err = ChdFile.Open(parentPath, out var parent);
        if (err != ChdError.Chderrnone || parent == null)
            throw new IOException($"Unable to open parent CHD '{parentPath}' ({err})");

        try
        {
            if (parent.HunkBytes != hunkBytes || parent.UnitBytes != unitBytes)
            {
                throw new ArgumentException(
                    $"Parent CHD hunk/unit size mismatch: parent is {parent.HunkBytes}/{parent.UnitBytes} bytes, " +
                    $"requested {hunkBytes}/{unitBytes} bytes. The parent's hunk and unit sizes must match the child's.");
            }

            _parent = parent;
            HunkCount = parent.HunkCount;
            HunkBytes = parent.HunkBytes;
            UnitBytes = parent.UnitBytes;
            UnitsPerHunk = hunkBytes / unitBytes;
            ParentSha1 = parent.Sha1;

            _map = BuildMap(parent);
        }
        catch
        {
            parent.Dispose();
            throw;
        }
    }

    /// <summary>The hunk count of the parent CHD.</summary>
    public uint HunkCount { get; }

    /// <summary>The hunk size of the parent CHD (equal to the child's).</summary>
    public uint HunkBytes { get; }

    /// <summary>The unit size of the parent CHD (equal to the child's).</summary>
    public uint UnitBytes { get; }

    /// <summary>The number of units per hunk.</summary>
    public uint UnitsPerHunk { get; }

    /// <summary>The parent's overall SHA-1 (header field), stored in the child's parent-SHA-1 field.</summary>
    public byte[] ParentSha1 { get; }

    /// <summary>Looks up a child hunk's (CRC-16, SHA-1) in the parent map.</summary>
    /// <param name="crc16">CRC-16 of the child hunk's data.</param>
    /// <param name="sha1Hex">Hexadecimal SHA-1 of the child hunk's data.</param>
    /// <param name="parentUnit">When <c>true</c> is returned, the parent unit index (0-based,
    /// in units) whose data matches the child hunk.</param>
    /// <returns><c>true</c> if a matching parent unit exists; otherwise <c>false</c>.</returns>
    public bool TryGetParentUnit(ushort crc16, string sha1Hex, out uint parentUnit)
    {
        return _map.TryGetValue((crc16, sha1Hex), out parentUnit);
    }

    private Dictionary<(ushort, string), uint> BuildMap(ChdFile parent)
    {
        var map = new Dictionary<(ushort, string), uint>();
        var window = new byte[HunkBytes];
        var hunk = new byte[HunkBytes];
        byte[]? next = null;

        for (uint h = 0; h < HunkCount; h++)
        {
            var readErr = parent.ReadHunk(h, hunk);
            if (readErr != ChdError.Chderrnone)
                throw new IOException($"Failed to decompress parent CHD hunk {h} ({readErr})");

            // the last hunk (or an uncompressed map) only hashes unit 0: windows past the
            // end of the parent's data cannot be referenced, exactly like MAME
            var units = h == HunkCount - 1 ? 1u : UnitsPerHunk;
            for (uint u = 0; u < units; u++)
            {
                var windowOffset = (int)(u * UnitBytes);
                var take = (int)(HunkBytes - windowOffset);
                Array.Copy(hunk, windowOffset, window, 0, take);

                // a window starting past unit 0 spills into the next hunk
                if (take < HunkBytes)
                {
                    next ??= new byte[HunkBytes];
                    var nextErr = parent.ReadHunk(h + 1, next);
                    if (nextErr != ChdError.Chderrnone)
                        throw new IOException($"Failed to decompress parent CHD hunk {h + 1} ({nextErr})");

                    Array.Copy(next, 0, window, take, HunkBytes - take);
                }

                var crc16 = Crc16.Compute(window);
                var sha1Hex = Convert.ToHexString(Sha1.Compute(window));
                map.TryAdd((crc16, sha1Hex), h * UnitsPerHunk + u);
            }
        }

        return map;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _parent?.Dispose();
    }
}