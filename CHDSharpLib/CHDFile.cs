using CHDSharp.Utils;

namespace CHDSharp;

/// <summary>
/// Provides read-only random access to a CHD file (V1-V5), including parent /
/// child differential CHD chains.
///
/// Open a standalone CHD with <see cref="Open(string, out CHDFile)"/>. For a
/// child (differential) CHD, supply its parent with
/// <see cref="Open(string, string, out CHDFile)"/> or
/// <see cref="Open(string, CHDFile, out CHDFile)"/>. Then decompress individual
/// hunks with <see cref="ReadHunk"/> or read arbitrary byte ranges with
/// <see cref="Read"/>.
///
/// NOTE: an instance is NOT thread-safe. It seeks a shared stream and mutates
/// shared per-hunk buffers, so all calls must be serialized by the caller.
/// </summary>
public sealed class CHDFile : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly ChdHeader _chd;
    private readonly CHDCodec _codec;

    // Parent CHD for differential (child) files. Null for standalone CHDs.
    private CHDFile? _parent;
    private bool _ownsParent;

    // Reusable buffer + cache for byte-range reads.
    private byte[]? _hunkBuffer;
    private long _cachedHunk = -1;

    // Scratch buffer used when stitching two parent hunks for an unaligned
    // V5 COMPRESSION_PARENT reference.
    private byte[]? _parentScratch;

    private CHDFile(Stream stream, bool leaveOpen, ChdHeader chd, uint version)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _chd = chd;
        _codec = new CHDCodec();
        Version = version;
    }

    /// <summary>CHD format version (1-5).</summary>
    public uint Version { get; }

    /// <summary>Total size in bytes of the decompressed image.</summary>
    public ulong TotalBytes => _chd.Totalbytes;

    /// <summary>Size in bytes of a single hunk (block).</summary>
    public uint HunkBytes => _chd.Blocksize;

    /// <summary>Number of hunks (blocks) in the image.</summary>
    public uint HunkCount => _chd.Totalblocks;

    /// <summary>SHA1 of the full image (including metadata for V4/V5), or the raw SHA1 when that is all that is available. May be null for V1/V2.</summary>
    public byte[] SHA1 => _chd.Sha1 ?? _chd.Rawsha1;

    /// <summary>SHA1 of ONLY the raw (decompressed) image data, excluding metadata. This is what a full sequential read of the image hashes to. Null for V1/V2.</summary>
    public byte[] RawSHA1 => _chd.Rawsha1;

    /// <summary>MD5 of the raw image data (V1-V3). Null for V4/V5.</summary>
    public byte[] MD5 => _chd.Md5;

    /// <summary>True if this CHD is a differential child that requires a parent CHD to read.</summary>
    public bool RequiresParent => !Util.IsAllZeroArray(_chd.Parentmd5) || !Util.IsAllZeroArray(_chd.Parentsha1);

    /// <summary>
    /// Opens a standalone CHD file from disk for random access. Fails with
    /// <see cref="chd_error.CHDERR_REQUIRES_PARENT"/> if the file is a child CHD.
    /// </summary>
    public static chd_error Open(string filename, out CHDFile? chdFile)
        => Open(filename, (CHDFile?)null, out chdFile);

    /// <summary>
    /// Opens a (possibly child) CHD from disk, resolving parent references
    /// against the parent CHD at <paramref name="parentFilename"/>. The parent is
    /// opened internally and disposed together with the returned instance.
    /// </summary>
    public static chd_error Open(string filename, string parentFilename, out CHDFile? chdFile)
    {
        chdFile = null;

        CHDFile? parent = null;
        if (!string.IsNullOrEmpty(parentFilename))
        {
            var perr = Open(parentFilename, (CHDFile?)null, out parent);
            if (perr != chd_error.CHDERR_NONE)
                return perr;
        }

        var err = Open(filename, parent, out chdFile);
        if (err != chd_error.CHDERR_NONE)
        {
            parent?.Dispose();
            return err;
        }

        // Transfer ownership of the internally-opened parent to the child.
        if (parent != null)
        {
            chdFile!._ownsParent = true;
        }

        return chd_error.CHDERR_NONE;
    }

    /// <summary>
    /// Opens a (possibly child) CHD from disk, resolving parent references
    /// against an already-open <paramref name="parent"/>. The caller retains
    /// ownership of <paramref name="parent"/> (it is not disposed by this
    /// instance). Pass null for a standalone CHD.
    /// </summary>
    public static chd_error Open(string filename, CHDFile? parent, out CHDFile? chdFile)
    {
        chdFile = null;
        if (!File.Exists(filename))
            return chd_error.CHDERR_FILE_NOT_FOUND;

        FileStream fs;
        try
        {
            fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
        }
        catch
        {
            return chd_error.CHDERR_CANNOT_OPEN_FILE;
        }

        var err = Open(fs, false, parent, out chdFile);
        if (err != chd_error.CHDERR_NONE)
            fs.Dispose();
        return err;
    }

    /// <summary>
    /// Opens a standalone CHD from an existing seekable stream for random access.
    /// </summary>
    /// <param name="stream">Seekable, readable stream positioned anywhere; it will be seeked as needed.</param>
    /// <param name="leaveOpen">If false, the stream is disposed when this instance is disposed.</param>
    public static chd_error Open(Stream stream, bool leaveOpen, out CHDFile? chdFile)
        => Open(stream, leaveOpen, null, out chdFile);

    /// <summary>
    /// Opens a (possibly child) CHD from an existing seekable stream, resolving
    /// parent references against <paramref name="parent"/> (null = standalone).
    /// </summary>
    public static chd_error Open(Stream stream, bool leaveOpen, CHDFile? parent, out CHDFile? chdFile)
    {
        chdFile = null;
        if (stream == null || !stream.CanRead || !stream.CanSeek)
            return chd_error.CHDERR_INVALID_PARAMETER;

        stream.Seek(0, SeekOrigin.Begin);
        if (!Chd.CheckHeader(stream, out _, out var version))
            return chd_error.CHDERR_INVALID_FILE;

        chd_error valid;
        ChdHeader chd;
        try
        {
            switch (version)
            {
                case 1: valid = CHDHeaders.ReadHeaderV1(stream, out chd); break;
                case 2: valid = CHDHeaders.ReadHeaderV2(stream, out chd); break;
                case 3: valid = CHDHeaders.ReadHeaderV3(stream, out chd); break;
                case 4: valid = CHDHeaders.ReadHeaderV4(stream, out chd); break;
                case 5: valid = CHDHeaders.ReadHeaderV5(stream, out chd); break;
                default: return chd_error.CHDERR_UNSUPPORTED_VERSION;
            }
        }
        catch
        {
            return chd_error.CHDERR_INVALID_DATA;
        }

        if (valid != chd_error.CHDERR_NONE)
            return valid;

        var needsParent = !Util.IsAllZeroArray(chd.Parentmd5) || !Util.IsAllZeroArray(chd.Parentsha1);
        if (needsParent)
        {
            if (parent == null)
                return chd_error.CHDERR_REQUIRES_PARENT;

            var verr = ValidateParent(chd, parent._chd);
            if (verr != chd_error.CHDERR_NONE)
                return verr;
        }

        // Build the codec delegate array for each compression slot.
        ChdBlockRead.FindBlockReaders(chd);

        // Link COMPRESSION_SELF entries to their source map entry so ReadBlock
        // can resolve them. (Full repeat-block caching used by CheckFile is not
        // needed for random access and is deliberately skipped.)
        LinkSelfBlocks(chd);

        chdFile = new CHDFile(stream, leaveOpen, chd, version);
        chdFile._parent = needsParent ? parent : null;
        return chd_error.CHDERR_NONE;
    }

    // Parent hash validation, matching libchdr: a check passes if the child's
    // stored hash is all-zero, OR the parent's hash is all-zero, OR they match.
    private static chd_error ValidateParent(ChdHeader child, ChdHeader parent)
    {
        var childMd5 = child.Parentmd5;
        var parentMd5 = parent.Md5;
        if (childMd5 != null && parentMd5 != null &&
            !Util.IsAllZeroArray(childMd5) && !Util.IsAllZeroArray(parentMd5) &&
            !Util.ByteArrEquals(childMd5, parentMd5))
            return chd_error.CHDERR_INVALID_PARENT;

        var childSha1 = child.Parentsha1;
        var parentSha1 = parent.Sha1 ?? parent.Rawsha1;
        if (childSha1 != null && parentSha1 != null &&
            !Util.IsAllZeroArray(childSha1) && !Util.IsAllZeroArray(parentSha1) &&
            !Util.ByteArrEquals(childSha1, parentSha1))
            return chd_error.CHDERR_INVALID_PARENT;

        return chd_error.CHDERR_NONE;
    }

    private static void LinkSelfBlocks(ChdHeader chd)
    {
        for (var i = 0; i < chd.Map.Length; i++)
        {
            var me = chd.Map[i];
            if (me.Comptype == compression_type.COMPRESSION_SELF)
            {
                me.SelfMapEntry = chd.Map[me.Offset];
            }
        }
    }

    /// <summary>
    /// Decompresses a single hunk into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="hunknum">Zero-based hunk index.</param>
    /// <param name="buffer">Destination buffer of at least <see cref="HunkBytes"/> bytes.</param>
    public chd_error ReadHunk(uint hunknum, byte[] buffer)
    {
        if (hunknum >= _chd.Totalblocks)
            return chd_error.CHDERR_HUNK_OUT_OF_RANGE;
        if (buffer == null || buffer.Length < _chd.Blocksize)
            return chd_error.CHDERR_INVALID_PARAMETER;

        var me = _chd.Map[hunknum];

        // Parent-referenced hunk: resolve against the parent CHD.
        if (me.Comptype == compression_type.COMPRESSION_PARENT)
            return ReadParentHunk(hunknum, me, buffer);

        // Resolve the entry that actually holds compressed data (follow SELF links).
        var dataEntry = me;
        while (dataEntry.Comptype == compression_type.COMPRESSION_SELF && dataEntry.SelfMapEntry != null)
        {
            dataEntry = dataEntry.SelfMapEntry;
        }

        var loaded = false;
        try
        {
            if (dataEntry.Length > 0)
            {
                if (dataEntry.BuffIn == null || dataEntry.BuffIn.Length < dataEntry.Length)
                {
                    dataEntry.BuffIn = new byte[dataEntry.Length];
                }

                _stream.Seek((long)dataEntry.Offset, SeekOrigin.Begin);
                _stream.ReadExactly(dataEntry.BuffIn, 0, (int)dataEntry.Length);
                loaded = true;
            }

            return ChdBlockRead.ReadBlock(me, null!, _chd.ChdReader, _codec, buffer, (int)_chd.Blocksize);
        }
        catch
        {
            return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }
        finally
        {
            if (loaded)
            {
                dataEntry.BuffIn = null!;
            }
        }
    }

    // Resolves a COMPRESSION_PARENT hunk against the parent CHD.
    //   - V1-V4 parent entries and V5 uncompressed-map parent entries store a
    //     DIRECT parent hunk index in MapEntry.offset.
    //   - V5 compressed-map COMPRESSION_PARENT entries store an offset in UNITS.
    //     Let units_in_hunk = hunkbytes / unitbytes. If the unit offset is
    //     hunk-aligned, one whole parent hunk is read; otherwise two adjacent
    //     parent hunks are stitched at the unit boundary.
    // Note: parent map entries carry no per-hunk CRC of their own (the map slot
    // holds the offset instead), so no CRC check is done here - matching libchdr,
    // which only verifies block CRCs for compressed/uncompressed entries.
    private chd_error ReadParentHunk(uint hunknum, MapEntry me, byte[] buffer)
    {
        if (_parent == null)
            return chd_error.CHDERR_REQUIRES_PARENT;

        var unitbytes = _chd.Unitbytes;
        var hunkbytes = _chd.Blocksize;

        // Direct-index cases: V1-V4 parent hunks, and the V5 uncompressed map
        // (which we normalised to a direct hunk index during parsing).
        var directIndex = Version < 5 || _chd.UncompressedMap;
        if (directIndex || unitbytes == 0 || unitbytes == hunkbytes)
        {
            if (me.Offset >= _parent.HunkCount)
                return chd_error.CHDERR_INVALID_PARENT;

            return _parent.ReadHunk((uint)me.Offset, buffer);
        }

        // V5 compressed unit-based parent reference.
        var unitsInHunk = hunkbytes / unitbytes;
        var blockoffs = me.Offset; // in units
        var parentHunk = blockoffs / unitsInHunk;
        var unitInHunk = (uint)(blockoffs % unitsInHunk);

        if (unitInHunk == 0)
        {
            if (parentHunk >= _parent.HunkCount)
                return chd_error.CHDERR_INVALID_PARENT;

            return _parent.ReadHunk((uint)parentHunk, buffer);
        }

        // Unaligned: stitch two adjacent parent hunks at the unit boundary.
        if (parentHunk + 1 >= _parent.HunkCount)
            return chd_error.CHDERR_INVALID_PARENT;

        _parentScratch ??= new byte[hunkbytes];

        // First part: tail of parent hunk 'parentHunk'.
        var e1 = _parent.ReadHunk((uint)parentHunk, _parentScratch);
        if (e1 != chd_error.CHDERR_NONE)
            return e1;

        var firstBytes = (int)((unitsInHunk - unitInHunk) * unitbytes);
        Array.Copy(_parentScratch, (int)(unitInHunk * unitbytes), buffer, 0, firstBytes);

        // Second part: head of parent hunk 'parentHunk + 1'.
        var e2 = _parent.ReadHunk((uint)parentHunk + 1, _parentScratch);
        if (e2 != chd_error.CHDERR_NONE)
            return e2;

        var secondBytes = (int)(unitInHunk * unitbytes);
        Array.Copy(_parentScratch, 0, buffer, firstBytes, secondBytes);

        return chd_error.CHDERR_NONE;
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes from the decompressed image starting
    /// at <paramref name="byteOffset"/>, decompressing hunks on demand. A single
    /// hunk is cached so sequential reads within the same hunk avoid re-decoding.
    /// </summary>
    public chd_error Read(ulong byteOffset, byte[] destination, int destinationOffset, int count)
    {
        if (destination == null || destinationOffset < 0 || count < 0 ||
            destinationOffset + count > destination.Length)
            return chd_error.CHDERR_INVALID_PARAMETER;
        if (byteOffset + (ulong)count > _chd.Totalbytes)
            return chd_error.CHDERR_INVALID_PARAMETER;

        _hunkBuffer ??= new byte[_chd.Blocksize];

        while (count > 0)
        {
            var hunk = (long)(byteOffset / _chd.Blocksize);
            var within = (int)(byteOffset % _chd.Blocksize);
            var chunk = Math.Min(count, (int)_chd.Blocksize - within);

            if (hunk != _cachedHunk)
            {
                var err = ReadHunk((uint)hunk, _hunkBuffer);
                if (err != chd_error.CHDERR_NONE)
                {
                    _cachedHunk = -1;
                    return err;
                }
                _cachedHunk = hunk;
            }

            Array.Copy(_hunkBuffer, within, destination, destinationOffset, chunk);
            destinationOffset += chunk;
            byteOffset += (ulong)chunk;
            count -= chunk;
        }

        return chd_error.CHDERR_NONE;
    }

    public void Dispose()
    {
        if (!_leaveOpen)
            _stream?.Dispose();
        if (_ownsParent)
            _parent?.Dispose();
    }
}
