using CHDSharp.Models;
using CHDSharp.Utils;

namespace CHDSharp;

/// <summary>
/// Provides read-only random access to a CHD (Compressed Hunks of Data) file,
/// supporting format versions 1-5 and parent/child differential CHD chains.
/// </summary>
/// <remarks>
/// <para>
/// Open a standalone CHD with <see cref="Open(string, out ChdFile)"/>. For a
/// child (differential) CHD, supply its parent with
/// <see cref="Open(string, string, out ChdFile)"/> or
/// <see cref="Open(string, ChdFile, out ChdFile)"/>. Then decompress individual
/// hunks with <see cref="ReadHunk"/>, read arbitrary byte ranges with
/// <see cref="Read"/>, or iterate the whole image with <see cref="EnumerateHunks"/>.
/// Async variants of every operation are available (<see cref="OpenAsync(string)"/>,
/// <see cref="ReadHunkAsync"/>, <see cref="ReadAsync"/>).
/// </para>
/// <para>
/// Always dispose the instance (<c>using</c> / <c>await using</c>); this closes the
/// underlying stream (unless opened with <c>leaveOpen: true</c>) and any internally
/// opened parent CHD.
/// </para>
/// <para>
/// <b>Thread safety:</b> an instance is NOT thread-safe. It seeks a shared stream and
/// mutates shared per-hunk buffers, so all calls must be serialized by the caller.
/// Multiple <see cref="ChdFile"/> instances over separate streams may be used in parallel.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var err = ChdFile.Open("game.chd", out var chd);
/// if (err != ChdError.Chderrnone) return;
/// using (chd)
/// {
///     var hunk = new byte[chd.HunkBytes];
///     chd.ReadHunk(0, hunk);          // first decompressed hunk
///
///     var buf = new byte[1024];
///     chd.Read(0x10000, buf, 0, buf.Length); // arbitrary byte range
/// }
/// </code>
/// </example>
public sealed class ChdFile : IDisposable, IAsyncDisposable
{
    private readonly Stream _stream;

    private readonly bool _leaveOpen;

    private readonly ChdHeader _chd;

    private readonly ChdCodecState _codec;

    private ChdFile? _parent;

    private bool _ownsParent;

    private byte[]? _hunkBuffer;

    private long _cachedHunk = -1;

    private byte[]? _parentScratch;

    private List<ChdMetadataEntry>? _metadata;
    private bool _metadataLoaded;
    private uint? _unitBytes;

    private ChdFile(Stream stream, bool leaveOpen, ChdHeader chd, uint version)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _chd = chd;
        _codec = new ChdCodecState();
        Version = version;
    }

    /// <summary>CHD format version (1-5).</summary>
    public uint Version { get; }

    /// <summary>Total size in bytes of the decompressed image.</summary>
    public ulong TotalBytes => _chd.Totalbytes;

    /// <summary>Size in bytes of a single hunk (block).</summary>
    public uint HunkBytes => _chd.Blocksize;

    /// <summary>
    /// Size in bytes of a unit used for parent block address translation.
    /// In V5 this is read from the header. In V1-V4 the concept does not
    /// exist in the header, so it is derived from metadata: hard disk metadata
    /// ("GDDD" tag) provides the bytes-per-sector value; CD/GD-ROM metadata
    /// ("CHCD", "CHTR", "CHT2", "CHGT", "CHGD" tags) produces the CD frame
    /// size (2448); otherwise defaults to <see cref="HunkBytes"/>.
    /// </summary>
    public uint UnitBytes
    {
        get
        {
            if (_unitBytes.HasValue)
                return _unitBytes.Value;

            if (Version >= 5)
                _unitBytes = _chd.Unitbytes;
            else
                _unitBytes = GuessUnitBytes();

            return _unitBytes.Value;
        }
    }

    /// <summary>Number of hunks (blocks) in the image.</summary>
    public uint HunkCount => _chd.Totalblocks;

    /// <summary>
    /// SHA1 of the full image including metadata (V4/V5), or the raw SHA1 when that is
    /// all the format provides (V3). All-zero or <c>null</c> for V1/V2, which predate SHA1 hashes.
    /// </summary>
    public byte[] Sha1 => _chd.Sha1;

    /// <summary>
    /// SHA1 of ONLY the raw (decompressed) image data, excluding metadata (V3-V5).
    /// This is what a full sequential read of the image hashes to.
    /// All-zero or <c>null</c> for V1/V2.
    /// </summary>
    public byte[] RawSha1 => _chd.Rawsha1;

    /// <summary>MD5 of the raw image data (V1-V3). All-zero or <c>null</c> for V4/V5, which dropped MD5.</summary>
    public byte[] Md5 => _chd.Md5;

    /// <summary>True if this CHD is a differential child that requires a parent CHD to read.</summary>
    public bool RequiresParent => !Util.IsAllZeroArray(_chd.Parentmd5) || !Util.IsAllZeroArray(_chd.Parentsha1);

    /// <summary>True if this CHD is a differential child. Alias for <see cref="RequiresParent"/>.</summary>
    public bool IsChild => RequiresParent;

    /// <summary>
    /// Gets the list of metadata entries from the CHD header (game name,
    /// disc info, etc.). Lazy-loaded on first access; empty list if the CHD
    /// has no metadata or an error occurs.
    /// </summary>
    public IReadOnlyList<ChdMetadataEntry> Metadata
    {
        get
        {
            EnsureMetadataLoaded();
            return _metadata!;
        }
    }

    /// <summary>
    /// Returns a string representation of the CHD file including version,
    /// size, and hunk count.
    /// </summary>
    public override string ToString()
    {
        return $"V{Version}: {TotalBytes} bytes, {HunkCount} hunks x {HunkBytes}";
    }

    private void EnsureMetadataLoaded()
    {
        if (_metadataLoaded)
            return;

        _metadataLoaded = true;
        _metadata = [];
        try
        {
            if (_chd.Metaoffset != 0)
            {
                ChdMetaData.ReadMetaDataEntries(_stream, _chd, out _metadata);
            }
        }
        catch
        {
            // Silently return empty list if metadata can't be read.
            _metadata = [];
        }
    }

    private uint GuessUnitBytes()
    {
        EnsureMetadataLoaded();

        if (_metadata is { Count: > 0 })
        {
            foreach (var entry in _metadata)
            {
                if (entry.Tag == "GDDD" && entry.IsText)
                {
                    var parts = entry.GetText().Split(',');
                    foreach (var p in parts)
                    {
                        var trimmed = p.Trim();
                        if (trimmed.StartsWith("BPS:", StringComparison.Ordinal) &&
                            uint.TryParse(trimmed.AsSpan(4), out var bps) && bps > 0)
                            return bps;
                    }

                    break;
                }
            }

            foreach (var entry in _metadata)
            {
                if (entry.Tag is "CHCD" or "CHTR" or "CHT2" or "CHGT" or "CHGD")
                    return ChdReaders.CdFrameSize;
            }
        }

        return _chd.Blocksize;
    }

    /// <inheritdoc cref="Open(string,out ChdFile)"/>
    /// <summary>Asynchronously opens a standalone CHD file from disk (see <see cref="Open(string,out ChdFile)"/>).</summary>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(string filename)
    {
        return Task.Run(() =>
        {
            var err = Open(filename, out var chd);
            return (err, chd);
        });
    }

    /// <inheritdoc cref="Open(string,string,out ChdFile)"/>
    /// <summary>Asynchronously opens a (possibly child) CHD from disk, resolving parent references against
    /// the CHD at <paramref name="parentFilename"/> (see <see cref="Open(string,string,out ChdFile)"/>).</summary>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(string filename, string? parentFilename)
    {
        return Task.Run(() =>
        {
            var err = Open(filename, parentFilename, out var chd);
            return (err, chd);
        });
    }

    /// <inheritdoc cref="Open(string,ChdFile,out ChdFile)"/>
    /// <summary>Asynchronously opens a (possibly child) CHD from disk against an already-open parent
    /// (see <see cref="Open(string,ChdFile,out ChdFile)"/>).</summary>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(string filename, ChdFile? parent)
    {
        return Task.Run(() =>
        {
            var err = Open(filename, parent, out var chd);
            return (err, chd);
        });
    }

    /// <inheritdoc cref="Open(Stream,bool,out ChdFile)"/>
    /// <summary>Asynchronously opens a standalone CHD from an existing seekable stream
    /// (see <see cref="Open(Stream,bool,out ChdFile)"/>).</summary>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(Stream stream, bool leaveOpen)
    {
        return Task.Run(() =>
        {
            var err = Open(stream, leaveOpen, out var chd);
            return (err, chd);
        });
    }

    /// <inheritdoc cref="ReadHunk"/>
    /// <summary>Asynchronously decompresses a single hunk into <paramref name="buffer"/> (see <see cref="ReadHunk"/>).</summary>
    /// <returns>A task producing the <see cref="ChdError"/> result.</returns>
    public Task<ChdError> ReadHunkAsync(uint hunknum, byte[] buffer)
    {
        return Task.Run(() => ReadHunk(hunknum, buffer));
    }

    /// <inheritdoc cref="Read"/>
    /// <summary>Asynchronously reads a byte range from the decompressed image (see <see cref="Read"/>).</summary>
    /// <returns>A task producing the <see cref="ChdError"/> result.</returns>
    public Task<ChdError> ReadAsync(ulong byteOffset, byte[] destination, int destinationOffset, int count)
    {
        return Task.Run(() => Read(byteOffset, destination, destinationOffset, count));
    }

    /// <summary>
    /// Decompresses the entire CHD image into a single byte array.
    /// </summary>
    /// <param name="data">When this method returns, contains the full decompressed image on success; an empty array on failure.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; <see cref="ChdError.Chderroutofmemory"/>
    /// if the image is larger than 2 GiB (<see cref="int.MaxValue"/> bytes); otherwise a read/decompression error code.</returns>
    /// <remarks>Be cautious: CHD images can be tens of gigabytes. Prefer <see cref="EnumerateHunks"/> or <see cref="Read"/> for large images.</remarks>
    public ChdError ReadAllBytes(out byte[] data)
    {
        data = [];
        if (_chd.Totalbytes > int.MaxValue)
            return ChdError.Chderroutofmemory;

        data = new byte[_chd.Totalbytes];
        return Read(0, data, 0, data.Length);
    }

    /// <summary>
    /// Yields each decompressed hunk in order. The returned array is reused
    /// between iterations. Copy it if you need to keep the data beyond the
    /// current iteration.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when a hunk fails to decompress, with the <see cref="ChdError"/> in the message.</exception>
    public IEnumerable<byte[]> EnumerateHunks()
    {
        var buffer = new byte[_chd.Blocksize];
        for (uint i = 0; i < _chd.Totalblocks; i++)
        {
            var err = ReadHunk(i, buffer);
            if (err != ChdError.Chderrnone)
                throw new InvalidDataException($"Failed to read hunk {i}: {err.GetMessage()} ({err})");

            yield return buffer;
        }
    }

    /// <summary>
    /// Opens a standalone CHD file from disk for random access. Fails with
    /// <see cref="ChdError.Chderrrequiresparent"/> if the file is a child CHD.
    /// </summary>
    /// <param name="filename">Path to the CHD file to open.</param>
    /// <param name="chdFile">When this method returns, contains the opened <see cref="ChdFile"/>, or <c>null</c> on error.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code
    /// (e.g. <see cref="ChdError.Chderrfilenotfound"/>, <see cref="ChdError.Chderrinvalidfile"/>,
    /// <see cref="ChdError.Chderrrequiresparent"/>).</returns>
    public static ChdError Open(string filename, out ChdFile? chdFile)
    {
        return Open(filename, (ChdFile?)null, out chdFile);
    }

    /// <summary>
    /// Opens a (possibly child) CHD from disk, resolving parent references
    /// against the parent CHD at <paramref name="parentFilename"/>. The parent is
    /// opened internally and disposed together with the returned instance.
    /// </summary>
    /// <param name="filename">Path to the CHD file to open.</param>
    /// <param name="parentFilename">Path to the parent CHD, or <c>null</c>/empty for a standalone CHD.</param>
    /// <param name="chdFile">When this method returns, contains the opened <see cref="ChdFile"/>, or <c>null</c> on error.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code
    /// (e.g. <see cref="ChdError.Chderrinvalidparent"/> if the parent's hashes do not match).</returns>
    public static ChdError Open(string filename, string? parentFilename, out ChdFile? chdFile)
    {
        chdFile = null;

        ChdFile? parent = null;
        if (!string.IsNullOrEmpty(parentFilename))
        {
            var perr = Open(parentFilename, (ChdFile?)null, out parent);
            if (perr != ChdError.Chderrnone)
                return perr;
        }

        var err = Open(filename, parent, out chdFile);
        if (err != ChdError.Chderrnone)
        {
            parent?.Dispose();
            return err;
        }

        // Transfer ownership of the internally-opened parent to the child.
        if (parent != null)
        {
            chdFile!._ownsParent = true;
        }

        return ChdError.Chderrnone;
    }

    /// <summary>
    /// Opens a (possibly child) CHD from disk, resolving parent references
    /// against an already-open <paramref name="parent"/>. The caller retains
    /// ownership of <paramref name="parent"/> (it is not disposed by this
    /// instance). Pass null for a standalone CHD.
    /// </summary>
    /// <param name="filename">Path to the CHD file to open.</param>
    /// <param name="parent">An already-open parent <see cref="ChdFile"/>, or <c>null</c> for a standalone CHD.
    /// The same parent instance may be shared by multiple children as long as all access is single-threaded.</param>
    /// <param name="chdFile">When this method returns, contains the opened <see cref="ChdFile"/>, or <c>null</c> on error.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    public static ChdError Open(string filename, ChdFile? parent, out ChdFile? chdFile)
    {
        chdFile = null;
        if (!File.Exists(filename))
            return ChdError.Chderrfilenotfound;

        FileStream fs;
        try
        {
            fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
        }
        catch
        {
            return ChdError.Chderrcannotopenfile;
        }

        var err = Open(fs, false, parent, out chdFile);
        if (err != ChdError.Chderrnone)
            fs.Dispose();
        return err;
    }

    /// <summary>
    /// Opens a standalone CHD from an existing seekable stream for random access.
    /// </summary>
    /// <param name="stream">Seekable, readable stream positioned anywhere; it will be seeked as needed.</param>
    /// <param name="leaveOpen">If false, the stream is disposed when this instance is disposed.</param>
    /// <param name="chdFile">When this method returns, contains the opened <see cref="ChdFile"/> instance, or <c>null</c> on error.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    public static ChdError Open(Stream stream, bool leaveOpen, out ChdFile? chdFile)
    {
        return Open(stream, leaveOpen, null, out chdFile);
    }

    /// <summary>
    /// Opens a (possibly child) CHD from an existing seekable stream, resolving
    /// parent references against <paramref name="parent"/> (null = standalone).
    /// </summary>
    /// <param name="stream">Seekable, readable stream positioned anywhere; it will be seeked as needed.</param>
    /// <param name="leaveOpen">If false, the stream is disposed when this instance is disposed.</param>
    /// <param name="parent">An already-open parent <see cref="ChdFile"/>, or <c>null</c> for a standalone CHD. The caller retains ownership.</param>
    /// <param name="chdFile">When this method returns, contains the opened <see cref="ChdFile"/> instance, or <c>null</c> on error.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    public static ChdError Open(Stream stream, bool leaveOpen, ChdFile? parent, out ChdFile? chdFile)
    {
        chdFile = null;
        if (stream is not { CanRead: true } || !stream.CanSeek)
            return ChdError.Chderrinvalidparameter;

        stream.Seek(0, SeekOrigin.Begin);
        if (!Chd.CheckHeader(stream, out _, out var version))
        {
            return ChdError.Chderrinvalidfile;
        }

        ChdError valid;
        ChdHeader chd;
        try
        {
            switch (version)
            {
                case 1: valid = ChdHeaders.ReadHeaderV1(stream, out chd); break;
                case 2: valid = ChdHeaders.ReadHeaderV2(stream, out chd); break;
                case 3: valid = ChdHeaders.ReadHeaderV3(stream, out chd); break;
                case 4: valid = ChdHeaders.ReadHeaderV4(stream, out chd); break;
                case 5: valid = ChdHeaders.ReadHeaderV5(stream, out chd); break;
                default:
                    return ChdError.Chderrunsupportedversion;
            }
        }
        catch (Exception)
        {
            return ChdError.Chderrinvaliddata;
        }

        if (valid != ChdError.Chderrnone)
            return valid;

        var needsParent = !Util.IsAllZeroArray(chd.Parentmd5) || !Util.IsAllZeroArray(chd.Parentsha1);
        if (needsParent)
        {
            if (parent == null)
                return ChdError.Chderrrequiresparent;

            var verr = ValidateParent(chd, parent._chd);
            if (verr != ChdError.Chderrnone)
                return verr;
        }

        // Build the codec delegate array for each compression slot.
        ChdBlockRead.FindBlockReaders(chd);

        // Link COMPRESSION_SELF entries to their source map entry so ReadBlock
        // can resolve them. (Full repeat-block caching used by CheckFile is not
        // needed for random access and is deliberately skipped.)
        LinkSelfBlocks(chd);

        chdFile = new ChdFile(stream, leaveOpen, chd, version);
        chdFile._parent = needsParent ? parent : null;
        return ChdError.Chderrnone;
    }

    private static ChdError ValidateParent(ChdHeader child, ChdHeader parent)
    {
        var childMd5 = child.Parentmd5;
        var parentMd5 = parent.Md5;
        if (!Util.IsAllZeroArray(childMd5) && !Util.IsAllZeroArray(parentMd5) &&
            !Util.ByteArrEquals(childMd5, parentMd5))
            return ChdError.Chderrinvalidparent;

        var childSha1 = child.Parentsha1;
        var parentSha1 = parent.Sha1;
        if (!Util.IsAllZeroArray(childSha1) && !Util.IsAllZeroArray(parentSha1) &&
            !Util.ByteArrEquals(childSha1, parentSha1))
            return ChdError.Chderrinvalidparent;

        return ChdError.Chderrnone;
    }

    private static void LinkSelfBlocks(ChdHeader chd)
    {
        foreach (var me in chd.Map)
        {
            if (me.Comptype == CompressionType.Compressionself)
            {
                me.SelfMapEntry = chd.Map[me.Offset];
            }
        }
    }

    /// <summary>
    /// Decompresses a single hunk into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="hunknum">Zero-based hunk index (0 to <see cref="HunkCount"/> - 1).</param>
    /// <param name="buffer">Destination buffer of at least <see cref="HunkBytes"/> bytes.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success;
    /// <see cref="ChdError.Chderrhunkoutofrange"/> if <paramref name="hunknum"/> is out of range;
    /// <see cref="ChdError.Chderrinvalidparameter"/> if <paramref name="buffer"/> is too small;
    /// <see cref="ChdError.Chderrrequiresparent"/> if the hunk references a missing parent;
    /// <see cref="ChdError.Chderrdecompressionerror"/> if the compressed data is corrupt.</returns>
    /// <remarks>The final hunk of an image whose size is not a multiple of <see cref="HunkBytes"/> is
    /// still <see cref="HunkBytes"/> long (padded as stored in the file).</remarks>
    public ChdError ReadHunk(uint hunknum, byte[] buffer)
    {
        if (hunknum >= _chd.Totalblocks)
            return ChdError.Chderrhunkoutofrange;
        if (buffer.Length < _chd.Blocksize)
            return ChdError.Chderrinvalidparameter;

        var me = _chd.Map[hunknum];

        // Parent-referenced hunk: resolve against the parent CHD.
        if (me.Comptype == CompressionType.Compressionparent)
            return ReadParentHunk(me, buffer);

        // Resolve the entry that actually holds compressed data (follow SELF links).
        var dataEntry = me;
        while (dataEntry is { Comptype: CompressionType.Compressionself })
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

            var rbErr = ChdBlockRead.ReadBlock(me, null!, _chd.ChdReader, _codec, buffer, (int)_chd.Blocksize);
            return rbErr;
        }
        catch (Exception)
        {
            return ChdError.Chderrdecompressionerror;
        }
        finally
        {
            if (loaded)
            {
                dataEntry.BuffIn = null!;
            }
        }
    }

    private ChdError ReadParentHunk(MapEntry me, byte[] buffer)
    {
        if (_parent == null)
            return ChdError.Chderrrequiresparent;

        var unitbytes = _chd.Unitbytes;
        var hunkbytes = _chd.Blocksize;

        // Direct-index cases: V1-V4 parent hunks, and the V5 uncompressed map
        // (which we normalised to a direct hunk index during parsing).
        var directIndex = Version < 5 || _chd.UncompressedMap;
        if (directIndex || unitbytes == 0 || unitbytes == hunkbytes)
        {
            if (me.Offset >= _parent.HunkCount)
                return ChdError.Chderrinvalidparent;

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
                return ChdError.Chderrinvalidparent;

            return _parent.ReadHunk((uint)parentHunk, buffer);
        }

        // Unaligned: stitch two adjacent parent hunks at the unit boundary.
        if (parentHunk + 1 >= _parent.HunkCount)
            return ChdError.Chderrinvalidparent;

        _parentScratch ??= new byte[hunkbytes];

        // First part: tail of parent hunk 'parentHunk'.
        var e1 = _parent.ReadHunk((uint)parentHunk, _parentScratch);
        if (e1 != ChdError.Chderrnone)
            return e1;

        var firstBytes = (int)((unitsInHunk - unitInHunk) * unitbytes);
        Array.Copy(_parentScratch, (int)(unitInHunk * unitbytes), buffer, 0, firstBytes);

        // Second part: head of parent hunk 'parentHunk + 1'.
        var e2 = _parent.ReadHunk((uint)parentHunk + 1, _parentScratch);
        if (e2 != ChdError.Chderrnone)
            return e2;

        var secondBytes = (int)(unitInHunk * unitbytes);
        Array.Copy(_parentScratch, 0, buffer, firstBytes, secondBytes);

        return ChdError.Chderrnone;
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes from the decompressed image starting
    /// at <paramref name="byteOffset"/>, decompressing hunks on demand. A single
    /// hunk is cached, so sequential reads within the same hunk avoid re-decoding.
    /// </summary>
    /// <param name="byteOffset">Byte offset into the decompressed image (0 to <see cref="TotalBytes"/> - 1).</param>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="destinationOffset">Offset in <paramref name="destination"/> at which to start writing.</param>
    /// <param name="count">Number of bytes to read.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success;
    /// <see cref="ChdError.Chderrinvalidparameter"/> if the requested range is outside the image or
    /// the destination bounds; otherwise a decompression error code.</returns>
    public ChdError Read(ulong byteOffset, byte[] destination, int destinationOffset, int count)
    {
        if (destinationOffset < 0 || count < 0 ||
            count > destination.Length - destinationOffset ||
            byteOffset > _chd.Totalbytes || (ulong)count > _chd.Totalbytes - byteOffset)
            return ChdError.Chderrinvalidparameter;

        _hunkBuffer ??= new byte[_chd.Blocksize];

        while (count > 0)
        {
            var hunk = (long)(byteOffset / _chd.Blocksize);
            var within = (int)(byteOffset % _chd.Blocksize);
            var chunk = Math.Min(count, (int)_chd.Blocksize - within);

            if (hunk != _cachedHunk)
            {
                var err = ReadHunk((uint)hunk, _hunkBuffer);
                if (err != ChdError.Chderrnone)
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

        return ChdError.Chderrnone;
    }

    /// <summary>Asynchronously releases the underlying stream (unless opened with <c>leaveOpen: true</c>) and any internally-owned parent instance.</summary>
    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
            await CastAndDispose(_stream);

        if (_ownsParent && _parent != null)
            await _parent.DisposeAsync();
        return;

        static ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
                return resourceAsyncDisposable.DisposeAsync();
            else
                resource.Dispose();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Releases the underlying stream (unless opened with <c>leaveOpen: true</c>) and any internally-owned parent instance.</summary>
    public void Dispose()
    {
        if (!_leaveOpen)
            _stream?.Dispose();
        if (_ownsParent)
            _parent?.Dispose();
    }
}
