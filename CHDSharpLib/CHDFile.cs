using System.Diagnostics;
using System.Globalization;
using System.Text;
using CHDSharp.Models;
using CHDSharp.Utils;
using Microsoft.Extensions.Logging;

namespace CHDSharp;

/// <summary>
/// Provides read-only random access to a CHD (Compressed Hunks of Data) file,
/// supporting format versions 1-5 and parent/child differential CHD chains.
/// </summary>
/// <remarks>
/// <para>
/// Open a standalone CHD with <see cref="Open(string, out ChdFile, System.Threading.CancellationToken)"/>. For a
    /// child (differential) CHD, supply its parent with
    /// <see cref="Open(string, string, out ChdFile, System.Threading.CancellationToken)"/> or
    /// <see cref="Open(string, ChdFile, out ChdFile, System.Threading.CancellationToken)"/>. Then decompress individual
    /// hunks with <see cref="ReadHunk"/>, read arbitrary byte ranges with
    /// <see cref="Read"/>, or iterate the whole image with <see cref="EnumerateHunks"/>.
    /// Async variants of every operation are available (<see cref="OpenAsync(string, System.Threading.CancellationToken)"/>,
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
    private static readonly ILogger Log = ChdLogger.GetLogger(nameof(ChdFile));

    private readonly Stream _stream;

    private readonly bool _leaveOpen;

    private readonly ChdHeader _chd;

    private readonly ChdCodecState _codec;

    private ChdFile? _parent;

    private bool _ownsParent;

    private byte[]? _hunkBuffer;

    private long _cachedHunk = -1;

    // Configurable multi-hunk LRU cache (libchdr #36). When CacheSize > 1, decompressed hunks
    // are retained so random reads that revisit hunks avoid re-decompression. Memory is capped
    // at CacheSize * HunkBytes (one full decompressed copy per slot). Like all ChdFile state,
    // the cache is NOT thread-safe: callers must serialize access, exactly as required for the
    // existing single-hunk _cachedHunk slot.
    private int _cacheSize = 1;
    private Dictionary<uint, LinkedListNode<CachedHunk>>? _lruIndex;
    private LinkedList<CachedHunk>? _lruOrder;

    private byte[]? _parentScratch;

    private List<ChdMetadataEntry>? _metadata;
    private bool _metadataLoaded;
    private ChdError _metadataError;
    private uint? _unitBytes;

    private byte[]? _precache;

    private List<ChdTrackInfo>? _tracks;
    private bool _tracksLoaded;
    private bool _isCd;
    private bool _isGdRom;
    private bool _isLegacyGdRom;
    private bool _isDvd;
    private bool _isHdd;

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
    /// The maximum allowed on-disk length (in bytes) of a single compressed hunk.
    /// Normalized to <c>HunkBytes</c> if set below it, so it is always an upper bound on
    /// the on-disk length. Defaults to <c>HunkBytes * 2</c> (see <see cref="ChdHeaders.DefaultMaxCompressedMultiple"/>).
    /// A malicious hunk-map entry claiming a compressed hunk longer than this cap is rejected with
    /// <see cref="ChdError.Chderrinvaliddata"/> before any allocation, preventing out-of-memory on crafted files.
    /// Valid CHDs created at low compression levels whose compressed size slightly exceeds the hunk size
    /// remain usable (they fall within the default 2x cap).
    /// </summary>
    public uint MaxCompressedBlockBytes
    {
        get => _chd.MaxCompressedBlockCap;
        set => _chd.MaxCompressedBlockCap = value == 0 ? checked(_chd.Blocksize * ChdHeaders.DefaultMaxCompressedMultiple) : Math.Max(value, _chd.Blocksize);
    }

    /// <summary>
    /// Number of decompressed hunks retained by the multi-hunk LRU cache (libchdr #36).
    /// Defaults to 1, which keeps the same behaviour as the single-hunk <c>_cachedHunk</c> slot
    /// (one hunk held between reads). Setting it to a value &gt; 1 makes <see cref="ReadHunk"/>
    /// keep the last <see cref="CacheSize"/> distinct hunks decompressed, so random reads that
    /// revisit hunks avoid re-decompression. Memory is capped at <c>CacheSize * HunkBytes</c>.
    /// Set to 0 or 1 to disable the multi-hunk cache (back to single-slot behaviour).
    /// </summary>
    public int CacheSize
    {
        get => _cacheSize;
        set => ConfigureCache(value);
    }

    /// <summary>
    /// Configures the multi-hunk LRU cache size (number of decompressed hunks to retain).
    /// A value &lt;= 1 reverts to the default single-hunk behaviour and releases any cached
    /// hunks. See <see cref="CacheSize"/>.
    /// </summary>
    /// <param name="maxHunks">Maximum number of hunks to keep decompressed.</param>
    public void ConfigureCache(int maxHunks)
    {
        if (maxHunks <= 0)
            maxHunks = 1;

        _cacheSize = maxHunks;

        if (_cacheSize <= 1)
        {
            _lruIndex = null;
            _lruOrder = null;
            _cachedHunk = -1;
            return;
        }

        _lruIndex ??= new Dictionary<uint, LinkedListNode<CachedHunk>>();
        _lruOrder ??= new LinkedList<CachedHunk>();

        // Shrink to the new capacity if it was reduced, evicting least-recently-used entries.
        while (_lruOrder.Count > _cacheSize)
        {
            var node = _lruOrder.First!;
            _lruOrder.RemoveFirst();
            _lruIndex.Remove(node.Value.Hunk);
        }
    }

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
            {
                _unitBytes = _chd.Unitbytes;
            }
            else
            {
                _unitBytes = GuessUnitBytes();
            }

            return _unitBytes.Value;
        }
    }

    /// <summary>Number of hunks (blocks) in the image.</summary>
    public uint HunkCount => _chd.Totalblocks;

    /// <summary>
    /// SHA1 of the full image including metadata (V4/V5), or the raw SHA1 when that is
    /// all the format provides (V3). All-zero or <c>null</c> for V1/V2, which predate SHA1 hashes.
    /// </summary>
    public byte[] Sha1 => _chd.Sha1!;

    /// <summary>
    /// SHA1 of ONLY the raw (decompressed) image data, excluding metadata (V3-V5).
    /// This is what a full sequential read of the image hashes to.
    /// All-zero or <c>null</c> for V1/V2.
    /// </summary>
    public byte[] RawSha1 => _chd.Rawsha1!;

    /// <summary>MD5 of the raw image data (V1-V3). All-zero or <c>null</c> for V4/V5, which dropped MD5.</summary>
    public byte[] Md5 => _chd.Md5!;

    /// <summary>True if this CHD is a differential child that requires a parent CHD to read.</summary>
    public bool RequiresParent => !Util.IsAllZeroArray(_chd.Parentmd5) || !Util.IsAllZeroArray(_chd.Parentsha1);

    /// <summary>True if this CHD is a differential child. Alias for <see cref="RequiresParent"/>.</summary>
    public bool IsChild => RequiresParent;

    /// <summary>Track layout information. <c>null</c> if this CHD is not a CD/GD-ROM image.</summary>
    public IReadOnlyList<ChdTrackInfo>? Tracks
    {
        get
        {
            EnsureTracksLoaded();
            return _tracks?.AsReadOnly();
        }
    }

    /// <summary><c>true</c> if this CHD contains CD-ROM track metadata.</summary>
    public bool IsCd
    {
        get
        {
            EnsureTracksLoaded();
            return _isCd;
        }
    }

    /// <summary><c>true</c> if this CHD is a GD-ROM (Sega Dreamcast) image.</summary>
    public bool IsGdRom
    {
        get
        {
            EnsureTracksLoaded();
            return _isGdRom;
        }
    }

    /// <summary>
    /// <c>true</c> if this is a legacy GD-ROM whose CDDA audio tracks are stored in little-endian
    /// byte order (<c>CD_FLAG_GDROMLE</c>, detected by the old "CHGT" metadata tag). For such discs,
    /// AUDIO track samples must be 16-bit byte-swapped when extracted/played back.
    /// Always <c>false</c> for non-GD-ROM images.
    /// </summary>
    public bool IsLittleEndianAudio
    {
        get
        {
            EnsureTracksLoaded();
            return _isLegacyGdRom;
        }
    }

    /// <summary><c>true</c> if this CHD contains DVD metadata.</summary>
    public bool IsDvd
    {
        get
        {
            EnsureTracksLoaded();
            return _isDvd;
        }
    }

    /// <summary><c>true</c> if this CHD contains hard disk geometry metadata.</summary>
    public bool IsHdd
    {
        get
        {
            EnsureTracksLoaded();
            return _isHdd;
        }
    }

    /// <summary>
    /// Gets the list of metadata entries from the CHD header (game name,
    /// disc info, etc.). Lazy-loaded on first access; empty list if the CHD
    /// has no metadata or an error occurs. For V1/V2 CHDs (which have no
    /// metadata section) a synthesized "GDDD" hard-disk entry is included,
    /// matching libchdr behaviour.
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
    /// Searches the metadata chain for an entry with the given four-character
    /// <paramref name="tag"/> and occurrence <paramref name="index"/> (libchdr
    /// <c>chd_get_metadata</c> parity). Pass <c>null</c> or an empty string as
    /// <paramref name="tag"/> to match entries of any tag.
    /// </summary>
    /// <param name="tag">Four-character tag to search for (e.g. "GDDD", "CHT2"), or <c>null</c>/empty for a wildcard match.</param>
    /// <param name="index">Zero-based occurrence index among the entries with the matching tag.</param>
    /// <param name="entry">The matching entry, or <c>null</c> when not found or on error.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success;
    /// <see cref="ChdError.Chderrmetadatanotfound"/> if no entry matches;
    /// <see cref="ChdError.Chderrinvaliddata"/> or <see cref="ChdError.Chderrreaderror"/> if the metadata could not be read.</returns>
    public ChdError GetMetadata(string? tag, uint index, out ChdMetadataEntry? entry)
    {
        entry = null;
        var err = EnsureMetadataLoaded();
        if (err != ChdError.Chderrnone)
            return err;

        foreach (var e in _metadata!)
        {
            if (string.IsNullOrEmpty(tag) || string.Equals(e.Tag, tag, StringComparison.Ordinal))
            {
                if (index == 0)
                {
                    entry = e;
                    return ChdError.Chderrnone;
                }

                index--;
            }
        }

        return ChdError.Chderrmetadatanotfound;
    }

    /// <summary>
    /// Reads the entire compressed CHD file into memory so that subsequent hunk
    /// reads are served from RAM instead of the underlying stream (libchdr
    /// <c>chd_precache</c> parity). Useful for random-access workloads over
    /// slow or remote streams. Idempotent: calling it again is a no-op.
    /// The underlying stream's position is restored after precaching.
    /// </summary>
    /// <remarks>Like all <see cref="ChdFile"/> members, <c>Precache</c> must not be
    /// called concurrently with other operations on the same instance.</remarks>
    /// <returns><see cref="ChdError.Chderrnone"/> on success (or if already precached);
    /// <see cref="ChdError.Chderroutofmemory"/> if the file is larger than 2 GiB or cannot be allocated;
    /// <see cref="ChdError.Chderrreaderror"/> if the file could not be read.</returns>
    public ChdError Precache()
    {
        if (_precache != null)
            return ChdError.Chderrnone;

        try
        {
            var length = _stream.Length;
            if (length > int.MaxValue)
                return ChdError.Chderroutofmemory;

            var buffer = new byte[(int)length];
            var pos = _stream.Position;
            try
            {
                _stream.Seek(0, SeekOrigin.Begin);
                _stream.ReadExactly(buffer, 0, buffer.Length);
            }
            finally
            {
                _stream.Seek(pos, SeekOrigin.Begin);
            }

            _precache = buffer;
            return ChdError.Chderrnone;
        }
        catch (OutOfMemoryException)
        {
            return ChdError.Chderroutofmemory;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Failed to precache CHD file into memory");
            return ChdError.Chderrreaderror;
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

    private ChdError EnsureMetadataLoaded()
    {
        if (_metadataLoaded)
            return _metadataError;

        _metadataLoaded = true;
        _metadata = [];
        _metadataError = ChdError.Chderrnone;

        // V1/V2 CHDs have no metadata section. Synthesize a GDDD hard-disk
        // entry from the obsolete header geometry fields (libchdr parity).
        if (Version < 3 && _chd.ObsoleteHunksize > 0)
        {
            var bps = _chd.Blocksize / _chd.ObsoleteHunksize;
            var gddd = $"CYLS:{_chd.ObsoleteCylinders},HEADS:{_chd.ObsoleteHeads},SECS:{_chd.ObsoleteSectors},BPS:{bps}";
            _metadata.Add(new ChdMetadataEntry("GDDD", Encoding.ASCII.GetBytes(gddd)));
        }

        if (_chd.Metaoffset == 0)
            return _metadataError;

        try
        {
            var err = ChdMetaData.ReadMetaDataEntries(_stream, _chd, out var entries);
            if (err != ChdError.Chderrnone)
            {
                _metadataError = err;
                return err;
            }

            _metadata.AddRange(entries);
        }
        catch (IOException ex)
        {
            Log.LogWarning(ex, "Failed to read CHD metadata (IO error)");
            _metadataError = ChdError.Chderrreaderror;
        }
        catch (InvalidDataException ex)
        {
            Log.LogWarning(ex, "Failed to read CHD metadata (invalid data)");
            _metadataError = ChdError.Chderrinvaliddata;
        }

        return _metadataError;
    }

    private uint GuessUnitBytes()
    {
        EnsureMetadataLoaded();

        return _metadata is { Count: > 0 }
            ? GuessUnitBytesFromMetadata(_metadata, _chd)
            : _chd.Blocksize;
    }

    /// <summary>
    /// Guesses the unit size (bytes per unit) from metadata entries for pre-V5 CHDs
    /// (libchdr <c>header_guess_unitbytes</c> parity): a "GDDD" hard-disk entry provides
    /// <c>BPS</c> (bytes per sector); CD/GD-ROM entries (CHCD/CHTR/CHT2/CHGT/CHGD) produce
    /// the CD frame size; otherwise falls back to the hunk size. Shared by
    /// <see cref="Chd.ReadHeader(string, out CHDSharp.Models.ChdHeaderInfo?)"/> so a header-only
    /// read reports the same unit size as an open <see cref="ChdFile"/>.
    /// </summary>
    internal static uint GuessUnitBytesFromMetadata(IReadOnlyList<ChdMetadataEntry> metadata, ChdHeader chd)
    {
        foreach (var entry in metadata)
        {
            if (entry is { Tag: "GDDD", IsText: true })
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

        foreach (var entry in metadata)
        {
            if (entry.Tag is "CHCD" or "CHTR" or "CHT2" or "CHGT" or "CHGD")
                return ChdReaders.CdFrameSize;
        }

        return chd.Blocksize;
    }

    private void EnsureTracksLoaded()
    {
        if (_tracksLoaded) return;

        _tracksLoaded = true;
        EnsureMetadataLoaded();

        _tracks = ChdTocParser.ParseTracks(_metadata!, out _isGdRom, out _isLegacyGdRom);
        _isCd = _tracks != null && !_isGdRom;
        _isDvd = ChdTocParser.HasDvdMetadata(_metadata!);
        _isHdd = ChdTocParser.HasHddMetadata(_metadata!);
    }

    /// <summary>Asynchronously opens a standalone CHD file from disk (see <see cref="Open(string,out ChdFile,System.Threading.CancellationToken)"/>).</summary>
    /// <param name="filename">Path to the CHD file to open.</param>
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/>
    /// is thrown (or the returned task is cancelled) if cancellation is requested.</param>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(string filename, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var err = Open(filename, out var chd, cancellationToken);
            return (err, chd);
        }, cancellationToken);
    }

    /// <summary>Asynchronously opens a (possibly child) CHD from disk, resolving parent references against
    /// the CHD at <paramref name="parentFilename"/> (see <see cref="Open(string,string,out ChdFile,System.Threading.CancellationToken)"/>).</summary>
    /// <param name="filename">Path to the CHD file to open.</param>
    /// <param name="parentFilename">Path to the parent CHD, or <c>null</c>/empty for a standalone CHD.</param>
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/>
    /// is thrown (or the returned task is cancelled) if cancellation is requested.</param>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(string filename, string? parentFilename, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var err = Open(filename, parentFilename, out var chd, cancellationToken);
            return (err, chd);
        }, cancellationToken);
    }

    /// <summary>Asynchronously opens a (possibly child) CHD from disk against an already-open parent
    /// (see <see cref="Open(string,ChdFile,out ChdFile,System.Threading.CancellationToken)"/>).</summary>
    /// <param name="filename">Path to the CHD file to open.</param>
    /// <param name="parent">An already-open parent <see cref="ChdFile"/>, or <c>null</c> for a standalone CHD. The caller retains ownership.</param>
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/>
    /// is thrown (or the returned task is cancelled) if cancellation is requested.</param>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(string filename, ChdFile? parent, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var err = Open(filename, parent, out var chd, cancellationToken);
            return (err, chd);
        }, cancellationToken);
    }

    /// <summary>Asynchronously opens a standalone CHD from an existing seekable stream
    /// (see <see cref="Open(Stream,bool,out ChdFile,System.Threading.CancellationToken)"/>).</summary>
    /// <param name="stream">Seekable, readable stream positioned anywhere; it will be seeked as needed.</param>
    /// <param name="leaveOpen">If false, the stream is disposed when this instance is disposed.</param>
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/>
    /// is thrown (or the returned task is cancelled) if cancellation is requested.</param>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(Stream stream, bool leaveOpen, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var err = Open(stream, leaveOpen, out var chd, cancellationToken);
            return (err, chd);
        }, cancellationToken);
    }

    /// <summary>Asynchronously opens a (possibly child) CHD from an existing seekable stream
    /// against an already-open parent (see <see cref="Open(Stream,bool,ChdFile,out ChdFile,System.Threading.CancellationToken)"/>).</summary>
    /// <param name="stream">Seekable, readable stream positioned anywhere; it will be seeked as needed.</param>
    /// <param name="leaveOpen">If false, the stream is disposed when this instance is disposed.</param>
    /// <param name="parent">An already-open parent <see cref="ChdFile"/>, or <c>null</c> for a standalone CHD. The caller retains ownership.</param>
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/>
    /// is thrown (or the returned task is cancelled) if cancellation is requested.</param>
    /// <returns>A task producing a tuple of the <see cref="ChdError"/> result and the opened <see cref="ChdFile"/> (or <c>null</c> on error).</returns>
    public static Task<(ChdError error, ChdFile? file)> OpenAsync(Stream stream, bool leaveOpen, ChdFile? parent, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var err = Open(stream, leaveOpen, parent, out var chd, cancellationToken);
            return (err, chd);
        }, cancellationToken);
    }

    /// <summary>Asynchronously decompresses a single hunk into <paramref name="buffer"/> (see <see cref="ReadHunk"/>).</summary>
    /// <param name="hunknum">Zero-based hunk index (0 to <see cref="HunkCount"/> - 1).</param>
    /// <param name="buffer">Destination buffer of at least <see cref="HunkBytes"/> bytes.</param>
    /// <param name="cancellationToken">A token to cancel the read. <see cref="OperationCanceledException"/> is thrown if cancellation is requested.</param>
    /// <returns>A task producing the <see cref="ChdError"/> result.</returns>
    public Task<ChdError> ReadHunkAsync(uint hunknum, byte[] buffer, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadHunk(hunknum, buffer, cancellationToken), cancellationToken);
    }

    /// <summary>Asynchronously reads a byte range from the decompressed image (see <see cref="Read"/>).</summary>
    /// <param name="byteOffset">Byte offset into the decompressed image (0 to <see cref="TotalBytes"/> - 1).</param>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="destinationOffset">Offset in <paramref name="destination"/> at which to start writing.</param>
    /// <param name="count">Number of bytes to read.</param>
    /// <param name="cancellationToken">A token to cancel the read. <see cref="OperationCanceledException"/> is thrown if cancellation is requested.</param>
    /// <returns>A task producing the <see cref="ChdError"/> result.</returns>
    public Task<ChdError> ReadAsync(ulong byteOffset, byte[] destination, int destinationOffset, int count, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Read(byteOffset, destination, destinationOffset, count, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Decompresses the entire CHD image into a single byte array.
    /// </summary>
    /// <param name="data">When this method returns, contains the full decompressed image on success; an empty array on failure.</param>
    /// <param name="progress">An optional <see cref="IProgress{T}"/> receiving a <see cref="ChdProgress"/>
    /// report after each decompressed hunk. <c>null</c> (default) disables progress reporting.</param>
    /// <param name="cancellationToken">A token to cancel the read. <see cref="OperationCanceledException"/>
    /// is thrown if cancellation is requested before a hunk is decompressed.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; <see cref="ChdError.Chderroutofmemory"/>
    /// if the image is larger than 2 GiB (<see cref="int.MaxValue"/> bytes); otherwise a read/decompression error code.</returns>
    /// <remarks>Be cautious: CHD images can be tens of gigabytes. Prefer <see cref="EnumerateHunks"/> or <see cref="Read"/> for large images.</remarks>
    public ChdError ReadAllBytes(out byte[] data, IProgress<ChdProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        data = [];
        cancellationToken.ThrowIfCancellationRequested();
        if (_chd.Totalbytes > int.MaxValue)
            return ChdError.Chderroutofmemory;

        data = new byte[_chd.Totalbytes];
        if (progress == null)
            return Read(0, data, 0, data.Length, cancellationToken);

        var sw = Stopwatch.StartNew();
        var bytesRead = 0;
        while (bytesRead < data.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min((ulong)_chd.Blocksize, (ulong)(data.Length - bytesRead));
            var err = Read((ulong)bytesRead, data, bytesRead, count, cancellationToken);
            if (err != ChdError.Chderrnone)
                return err;

            bytesRead += count;
            var currentHunk = (long)bytesRead / _chd.Blocksize;
            if ((long)bytesRead % _chd.Blocksize != 0)
                currentHunk++;
            progress.Report(new ChdProgress(currentHunk, _chd.Totalblocks, bytesRead, (long)_chd.Totalbytes, sw.Elapsed));
        }

        return ChdError.Chderrnone;
    }

    /// <summary>
    /// Yields each decompressed hunk in order. The returned array is reused
    /// between iterations. Copy it if you need to keep the data beyond the
    /// current iteration.
    /// </summary>
    /// <param name="progress">An optional <see cref="IProgress{T}"/> receiving a <see cref="ChdProgress"/>
    /// report after each decompressed hunk. <c>null</c> (default) disables progress reporting.</param>
    /// <exception cref="InvalidDataException">Thrown when a hunk fails to decompress, with the <see cref="ChdError"/> in the message.</exception>
    public IEnumerable<byte[]> EnumerateHunks(IProgress<ChdProgress>? progress = null)
    {
        var sw = progress != null ? Stopwatch.StartNew() : null;
        var buffer = new byte[_chd.Blocksize];
        for (uint i = 0; i < _chd.Totalblocks; i++)
        {
            var err = ReadHunk(i, buffer);
            if (err != ChdError.Chderrnone)
                throw new InvalidDataException($"Failed to read hunk {i}: {err.GetMessage()} ({err})");

            progress?.Report(new ChdProgress(
                i + 1,
                _chd.Totalblocks,
                (long)Math.Min((i + 1) * (ulong)_chd.Blocksize, _chd.Totalbytes),
                (long)_chd.Totalbytes,
                sw!.Elapsed));
            yield return buffer;
        }
    }

    /// <summary>
    /// Opens a standalone CHD file from disk for random access. Fails with
    /// <see cref="ChdError.Chderrrequiresparent"/> if the file is a child CHD.
    /// </summary>
    /// <param name="filename">Path to the CHD file to open.</param>
    /// <param name="chdFile">When this method returns, contains the opened <see cref="ChdFile"/>, or <c>null</c> on error.</param>
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/> is thrown if cancellation is requested.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code
    /// (e.g. <see cref="ChdError.Chderrfilenotfound"/>, <see cref="ChdError.Chderrinvalidfile"/>,
    /// <see cref="ChdError.Chderrrequiresparent"/>).</returns>
    public static ChdError Open(string filename, out ChdFile? chdFile, CancellationToken cancellationToken = default)
    {
        return Open(filename, (ChdFile?)null, out chdFile, cancellationToken);
    }

    /// <summary>
    /// Opens a (possibly child) CHD from disk, resolving parent references
    /// against the parent CHD at <paramref name="parentFilename"/>. The parent is
    /// opened internally and disposed together with the returned instance.
    /// </summary>
    /// <param name="filename">Path to the CHD file to open.</param>
    /// <param name="parentFilename">Path to the parent CHD, or <c>null</c>/empty for a standalone CHD.</param>
    /// <param name="chdFile">When this method returns, contains the opened <see cref="ChdFile"/>, or <c>null</c> on error.</param>
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/> is thrown if cancellation is requested.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code
    /// (e.g. <see cref="ChdError.Chderrinvalidparent"/> if the parent's hashes do not match).</returns>
    public static ChdError Open(string filename, string? parentFilename, out ChdFile? chdFile, CancellationToken cancellationToken = default)
    {
        chdFile = null;
        cancellationToken.ThrowIfCancellationRequested();

        ChdFile? parent = null;
        if (!string.IsNullOrEmpty(parentFilename))
        {
            var perr = Open(parentFilename, (ChdFile?)null, out parent, cancellationToken);
            if (perr != ChdError.Chderrnone)
                return perr;
        }

        var err = Open(filename, parent, out chdFile, cancellationToken);
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
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/> is thrown if cancellation is requested.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    public static ChdError Open(string filename, ChdFile? parent, out ChdFile? chdFile, CancellationToken cancellationToken = default)
    {
        chdFile = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filename))
            return ChdError.Chderrfilenotfound;

        FileStream fs;
        try
        {
            fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 4096);
        }
        catch (FileNotFoundException)
        {
            return ChdError.Chderrfilenotfound;
        }
        catch (UnauthorizedAccessException)
        {
            return ChdError.Chderrcannotopenfile;
        }
        catch (IOException)
        {
            return ChdError.Chderrcannotopenfile;
        }

        var err = Open(fs, false, parent, out chdFile, cancellationToken);
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
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/> is thrown if cancellation is requested.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    public static ChdError Open(Stream stream, bool leaveOpen, out ChdFile? chdFile, CancellationToken cancellationToken = default)
    {
        return Open(stream, leaveOpen, null, out chdFile, cancellationToken);
    }

    /// <summary>
    /// Opens a (possibly child) CHD from an existing seekable stream, resolving
    /// parent references against <paramref name="parent"/> (null = standalone).
    /// </summary>
    /// <param name="stream">Seekable, readable stream positioned anywhere; it will be seeked as needed.</param>
    /// <param name="leaveOpen">If false, the stream is disposed when this instance is disposed.</param>
    /// <param name="parent">An already-open parent <see cref="ChdFile"/>, or <c>null</c> for a standalone CHD. The caller retains ownership.</param>
    /// <param name="chdFile">When this method returns, contains the opened <see cref="ChdFile"/> instance, or <c>null</c> on error.</param>
    /// <param name="cancellationToken">A token to cancel the open. <see cref="OperationCanceledException"/> is thrown if cancellation is requested.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code.</returns>
    public static ChdError Open(Stream stream, bool leaveOpen, ChdFile? parent, out ChdFile? chdFile, CancellationToken cancellationToken = default)
    {
        chdFile = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (stream is not { CanRead: true } || !stream.CanSeek)
            return ChdError.Chderrinvalidparameter;

        uint version;
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            if (!Chd.CheckHeader(stream, out _, out version))
            {
                return ChdError.Chderrinvalidfile;
            }
        }
        catch (IOException ex)
        {
            Log.LogWarning(ex, "Failed to read CHD header from stream");
            return ChdError.Chderrreaderror;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Failed to read CHD header from stream");
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

        valid = ChdHeaders.ValidateSizeLimits(chd);
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
        var linkErr = LinkSelfBlocks(chd);
        if (linkErr != ChdError.Chderrnone)
            return linkErr;

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

    private static ChdError LinkSelfBlocks(ChdHeader chd)
    {
        foreach (var me in chd.Map)
        {
            if (me.Comptype == CompressionType.Compressionself)
            {
                if (me.Offset >= (ulong)chd.Map.Length)
                    return ChdError.Chderrinvaliddata;

                var self = chd.Map[me.Offset];
                me.SelfMapEntry = self;
                if (self.Comptype == CompressionType.Compressiontype2Nd)
                {
                    me.SecondaryReader = self.SecondaryReader;
                }
            }
        }

        return ChdError.Chderrnone;
    }

    /// <summary>
    /// Decompresses a single hunk into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="hunknum">Zero-based hunk index (0 to <see cref="HunkCount"/> - 1).</param>
    /// <param name="buffer">Destination buffer of at least <see cref="HunkBytes"/> bytes.</param>
    /// <param name="cancellationToken">A token to cancel the decompression. <see cref="OperationCanceledException"/>
    /// is thrown if cancellation is requested before the hunk is decompressed.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success;
    /// <see cref="ChdError.Chderrhunkoutofrange"/> if <paramref name="hunknum"/> is out of range;
    /// <see cref="ChdError.Chderrinvalidparameter"/> if <paramref name="buffer"/> is too small;
    /// <see cref="ChdError.Chderrrequiresparent"/> if the hunk references a missing parent;
    /// <see cref="ChdError.Chderrdecompressionerror"/> if the compressed data is corrupt.</returns>
    /// <remarks>The final hunk of an image whose size is not a multiple of <see cref="HunkBytes"/> is
    /// still <see cref="HunkBytes"/> long (padded as stored in the file).</remarks>
    public ChdError ReadHunk(uint hunknum, byte[] buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (hunknum >= _chd.Totalblocks)
            return ChdError.Chderrhunkoutofrange;
        if (buffer.Length < _chd.Blocksize)
            return ChdError.Chderrinvalidparameter;

        var me = _chd.Map[hunknum];

        // Multi-hunk LRU cache: serve the cached decompressed hunk directly if present.
        if (_cacheSize > 1 && TryGetCachedHunk(hunknum, buffer))
            return ChdError.Chderrnone;

        // Parent-referenced hunk: resolve against the parent CHD.
        if (me.Comptype == CompressionType.Compressionparent)
        {
            var err = ReadParentHunk(me, buffer);
            if (err == ChdError.Chderrnone && _cacheSize > 1)
                AddToCache(hunknum, buffer);
            return err;
        }

        // Resolve the entry that actually holds compressed data (follow SELF links).
        MapEntry? dataEntry = me;
        while (dataEntry is { Comptype: CompressionType.Compressionself })
        {
            dataEntry = dataEntry.SelfMapEntry;
        }

        if (dataEntry is null)
            return ChdError.Chderrinvaliddata;

        var loaded = false;
        try
        {
            if (dataEntry.Length > 0)
            {
                // Bounds check: the compressed length is attacker-controlled data from the hunk
                // map. Enforce the cap before any allocation so a malicious entry cannot trigger
                // an out-of-memory allocation of unbounded size.
                if (dataEntry.Length > _chd.MaxCompressedBlockCap)
                {
                    Log.LogWarning("Hunk {HunkNumber} compressed length {Length} exceeds cap {Cap}", hunknum, dataEntry.Length, _chd.MaxCompressedBlockCap);
                    return ChdError.Chderrinvaliddata;
                }

                if (dataEntry.BuffIn == null || dataEntry.BuffIn.Length < dataEntry.Length)
                    dataEntry.BuffIn = new byte[dataEntry.Length];

                if (_precache != null)
                {
                    Array.Copy(_precache, (int)dataEntry.Offset, dataEntry.BuffIn, 0, (int)dataEntry.Length);
                }
                else
                {
                    _stream.Seek((long)dataEntry.Offset, SeekOrigin.Begin);
                    _stream.ReadExactly(dataEntry.BuffIn, 0, (int)dataEntry.Length);
                }

                loaded = true;
            }

            var rbErr = ChdBlockRead.ReadBlock(me, new ArrayPool(_chd.Blocksize), _chd.ChdReader, _codec, buffer, (int)_chd.Blocksize);
            if (rbErr == ChdError.Chderrnone && _cacheSize > 1)
                AddToCache(hunknum, buffer);
            return rbErr;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Failed to decompress hunk {HunkNumber}", hunknum);
            return ChdError.Chderrdecompressionerror;
        }
        finally
        {
            if (loaded)
            {
                dataEntry.BuffIn = null;
            }
        }
    }

    /// <summary>
    /// Copies the cached decompressed hunk <paramref name="hunknum"/> into <paramref name="buffer"/>
    /// (promoting it to most-recently-used) and returns <c>true</c> on a cache hit.
    /// </summary>
    private bool TryGetCachedHunk(uint hunknum, byte[] buffer)
    {
        var index = _lruIndex;
        var order = _lruOrder;
        if (index == null || order == null)
            return false;

        if (!index.TryGetValue(hunknum, out var node))
            return false;

        // Promote to most-recently-used.
        order.Remove(node);
        order.AddLast(node);
        Array.Copy(node.Value.Data, 0, buffer, 0, _chd.Blocksize);
        return true;
    }

    /// <summary>Inserts a freshly decompressed hunk into the LRU cache, evicting the least-recently-used entry when over capacity.</summary>
    private void AddToCache(uint hunknum, byte[] buffer)
    {
        var index = _lruIndex;
        var order = _lruOrder;
        if (index == null || order == null)
            return;

        if (index.TryGetValue(hunknum, out var existing))
        {
            order.Remove(existing);
            index.Remove(hunknum);
        }

        // Copy the decompressed data so callers can reuse/mutate their buffer freely.
        var cached = new byte[_chd.Blocksize];
        Array.Copy(buffer, 0, cached, 0, _chd.Blocksize);
        var node = order.AddLast(new CachedHunk(hunknum, cached));
        index[hunknum] = node;

        // Evict least-recently-used while over capacity.
        while (order.Count > _cacheSize)
        {
            var first = order.First!;
            order.RemoveFirst();
            index.Remove(first.Value.Hunk);
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
    /// <param name="cancellationToken">A token to cancel the read. <see cref="OperationCanceledException"/>
    /// is thrown if cancellation is requested before a hunk is decompressed.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success;
    /// <see cref="ChdError.Chderrinvalidparameter"/> if the requested range is outside the image or
    /// the destination bounds; otherwise a decompression error code.</returns>
    public ChdError Read(ulong byteOffset, byte[] destination, int destinationOffset, int count, CancellationToken cancellationToken = default)
    {
        if (destinationOffset < 0 || count < 0 ||
            count > destination.Length - destinationOffset ||
            byteOffset > _chd.Totalbytes || (ulong)count > _chd.Totalbytes - byteOffset)
            return ChdError.Chderrinvalidparameter;

        cancellationToken.ThrowIfCancellationRequested();

        _hunkBuffer ??= new byte[_chd.Blocksize];

        while (count > 0)
        {
            var hunk = (long)(byteOffset / _chd.Blocksize);
            var within = (int)(byteOffset % _chd.Blocksize);
            var chunk = Math.Min(count, (int)_chd.Blocksize - within);

            if (hunk != _cachedHunk)
            {
                var err = ReadHunk((uint)hunk, _hunkBuffer, cancellationToken);
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
        _codec.Dispose();
        if (!_leaveOpen)
            await CastAndDispose(_stream).ConfigureAwait(false);

        if (_ownsParent && _parent != null)
            await _parent.DisposeAsync().ConfigureAwait(false);
        return;

        static ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
                return resourceAsyncDisposable.DisposeAsync();

            resource.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Releases the underlying stream (unless opened with <c>leaveOpen: true</c>), the parent reference if owned, and codec resources.</summary>
    public void Dispose()
    {
        _codec.Dispose();
        if (!_leaveOpen)
            _stream.Dispose();
        if (_ownsParent)
            _parent?.Dispose();
    }

    /// <summary>
    /// Generates a standard CUE sheet for this CD-ROM CHD using single-bin format.
    /// </summary>
    /// <param name="binFileName">The filename of the binary data file to reference in the CUE sheet.</param>
    /// <returns>A CUE sheet string.</returns>
    public string GenerateCueSheet(string binFileName)
    {
        EnsureTracksLoaded();
        if (_tracks == null || _tracks.Count == 0)
            throw new InvalidOperationException("This CHD does not contain CD track metadata.");

        var sb = new StringBuilder();

        sb.AppendLine("REM Generated by CHDSharp");
        sb.AppendLine(CultureInfo.InvariantCulture, $"REM Tracks: {_tracks.Count}");
        sb.AppendLine();

        for (var i = 0; i < _tracks.Count; i++)
        {
            var track = _tracks[i];

            if (i == 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"FILE \"{binFileName}\" BINARY");

            var modeStr = track.TrackType switch
            {
                ChdTrackType.Mode1 or ChdTrackType.Mode1Raw => $"MODE1/{track.DataSize:D4}",
                ChdTrackType.Mode2 => $"MODE2/{track.DataSize:D4}",
                ChdTrackType.Mode2Form1 => $"MODE2/{track.DataSize:D4}",
                ChdTrackType.Mode2Form2 => $"MODE2/{track.DataSize:D4}",
                ChdTrackType.Mode2FormMix => $"MODE2/{track.DataSize:D4}",
                ChdTrackType.Mode2Raw => $"MODE2/{track.DataSize:D4}",
                ChdTrackType.Audio => "AUDIO",
                _ => $"MODE1/{track.DataSize:D4}"
            };

            sb.AppendLine(CultureInfo.InvariantCulture, $"  TRACK {track.TrackNumber:D2} {modeStr}");

            switch (track.PreGap)
            {
                case > 0 when track.PreGapDataSize == 0:
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    PREGAP {FramesToMsf(track.PreGap)}");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    INDEX 01 {FramesToMsf(track.StartFrame)}");
                    break;
                case > 0 when track.PreGapDataSize > 0:
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    INDEX 00 {FramesToMsf(track.StartFrame)}");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    INDEX 01 {FramesToMsf(track.StartFrame + (ulong)track.PreGap)}");
                    break;
                default:
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    INDEX 01 {FramesToMsf(track.StartFrame)}");
                    break;
            }

            if (track.PostGap > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"    POSTGAP {FramesToMsf(track.PostGap)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a GDI descriptor for this GD-ROM CHD.
    /// </summary>
    /// <param name="trackFiles">Array of filenames for each track's binary data file. Must match track count.</param>
    /// <returns>A GDI descriptor string.</returns>
    public string GenerateGdiDescriptor(string[] trackFiles)
    {
        EnsureTracksLoaded();
        if (!_isGdRom || _tracks == null || _tracks.Count == 0)
            throw new InvalidOperationException("This CHD does not contain GD-ROM track metadata.");
        if (trackFiles.Length != _tracks.Count)
            throw new ArgumentException($"Expected {_tracks.Count} track filenames, got {trackFiles.Length}.");

        var sb = new StringBuilder();
        sb.AppendLine(_tracks.Count.ToString(CultureInfo.InvariantCulture));

        for (var i = 0; i < _tracks.Count; i++)
        {
            var track = _tracks[i];
            var trackType = track.TrackType == ChdTrackType.Audio ? 0 : 4;
            var quotedName = trackFiles[i].Contains(' ') ? $"\"{trackFiles[i]}\"" : trackFiles[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"{track.TrackNumber} {(uint)track.StartFrame} {trackType} {track.DataSize} {quotedName} 0");
        }

        return sb.ToString();
    }

    /// <summary>Returns a human-readable table-of-contents summary.</summary>
    public string ExportToc()
    {
        EnsureTracksLoaded();
        if (_tracks == null || _tracks.Count == 0)
            return "No CD/GD-ROM track metadata found.";

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Version: V{Version}, Total bytes: {TotalBytes:N0}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Type: {(_isGdRom ? "GD-ROM" : _isCd ? "CD-ROM" : "Unknown")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Hunk size: {HunkBytes:N0}, Unit size: {UnitBytes:N0}");

        if (_isDvd) sb.AppendLine("DVD metadata present.");
        if (_isHdd) sb.AppendLine("HDD metadata present.");

        sb.AppendLine();
        sb.AppendLine("Track  Type              Frames     Start      Sector Size");
        sb.AppendLine("-----  ----------------  ---------  ---------  -----------");

        foreach (var t in _tracks)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{t.TrackNumber,3:D2}    {t.GetTypeString(),-16}  {t.Frames,9:N0}  {t.StartFrame,9}  {t.DataSize,11}");
            if (t.PreGap > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"       Pregap: {t.PreGap:N0} frames{(t.PreGapDataSize > 0 ? " (data in file)" : "")}");
            if (t.PostGap > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"       Postgap: {t.PostGap:N0} frames");
            if (t.ExtraFrames > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"       Padding: {t.ExtraFrames:N0} frames");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts the entire CHD image to the specified directory.
    /// For CD/GD-ROM images, also writes a CUE sheet or GDI descriptor.
    /// Throws <see cref="InvalidDataException"/> on any extraction failure.
    /// </summary>
    /// <param name="outputDir">Target directory. Created if it doesn't exist.</param>
    /// <param name="baseFileName">Base filename (without extension) for output files.</param>
    /// <param name="progress">An optional <see cref="IProgress{T}"/> receiving a <see cref="ChdProgress"/>
    /// report after each decompressed hunk. <c>null</c> (default) disables progress reporting.</param>
    /// <param name="cancellationToken">A token to cancel the extraction. <see cref="OperationCanceledException"/>
    /// is thrown if cancellation is requested between hunk writes.</param>
    /// <returns>List of created file paths.</returns>
    public IReadOnlyList<string> ExtractToDirectory(string outputDir, string baseFileName, IProgress<ChdProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = ExtractToDirectoryWithReporting(outputDir, baseFileName, progress, cancellationToken);
        if (result.Error != ChdError.Chderrnone)
            throw new InvalidDataException($"Extraction failed: {result.Error}");

        if (result.HasTrackFailures)
        {
            var failed = result.TrackResults.Where(t => !t.IsSuccess).Select(t => $"track {t.TrackNumber}: {t.Error}");
            throw new InvalidDataException($"Track extraction failures: {string.Join(", ", failed)}");
        }

        return result.CreatedFiles;
    }

    /// <summary>
    /// Extracts the entire CHD image to the specified directory with per-track error reporting.
    /// For GD-ROM images, each track is extracted individually and failures are reported per-track
    /// rather than stopping at the first error. For all other image types, extraction is all-or-nothing.
    /// </summary>
    /// <param name="outputDir">Target directory. Created if it doesn't exist.</param>
    /// <param name="baseFileName">Base filename (without extension) for output files.</param>
    /// <param name="progress">An optional <see cref="IProgress{T}"/> receiving a <see cref="ChdProgress"/>
    /// report after each decompressed hunk. <c>null</c> (default) disables progress reporting.</param>
    /// <param name="cancellationToken">A token to cancel the extraction. <see cref="OperationCanceledException"/>
    /// is thrown if cancellation is requested between hunk writes.</param>
    /// <returns>An <see cref="ExtractResult"/> with created files, per-track results, and overall error.</returns>
    public ExtractResult ExtractToDirectoryWithReporting(string outputDir, string baseFileName, IProgress<ChdProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var created = new List<string>();
        var trackResults = new List<TrackExtractResult>();
        Directory.CreateDirectory(outputDir);

        if (IsGdRom)
        {
            foreach (var track in Tracks!)
            {
                var trackFile = Path.Combine(outputDir, $"track{track.TrackNumber:D2}.bin");
                var err = TryWriteTrackToFile(track, trackFile, progress, cancellationToken);
                trackResults.Add(new TrackExtractResult(track.TrackNumber, trackFile, err));
                if (err == ChdError.Chderrnone)
                    created.Add(trackFile);
            }

            try
            {
                var trackNames = Tracks.Select(t => $"track{t.TrackNumber:D2}.bin").ToArray();
                var gdiFile = Path.Combine(outputDir, $"{baseFileName}.gdi");
                File.WriteAllText(gdiFile, GenerateGdiDescriptor(trackNames));
                created.Add(gdiFile);
            }
            catch (Exception)
            {
                return new ExtractResult(created, trackResults, ChdError.Chderrwriteerror);
            }

            return new ExtractResult(created, trackResults, ChdError.Chderrnone);
        }

        try
        {
            string imageFile;

            if (IsCd)
            {
                imageFile = Path.Combine(outputDir, $"{baseFileName}.bin");
                WriteAllBytesSlow(imageFile, progress, cancellationToken);
                created.Add(imageFile);

                var descriptorFile = Path.Combine(outputDir, $"{baseFileName}.cue");
                File.WriteAllText(descriptorFile, GenerateCueSheet(Path.GetFileName(imageFile)));
                created.Add(descriptorFile);
            }
            else if (IsDvd)
            {
                imageFile = Path.Combine(outputDir, $"{baseFileName}.iso");
                WriteAllBytesSlow(imageFile, progress, cancellationToken);
                created.Add(imageFile);
            }
            else if (IsHdd)
            {
                imageFile = Path.Combine(outputDir, $"{baseFileName}.img");
                WriteAllBytesSlow(imageFile, progress, cancellationToken);
                created.Add(imageFile);
            }
            else
            {
                imageFile = Path.Combine(outputDir, $"{baseFileName}.raw");
                WriteAllBytesSlow(imageFile, progress, cancellationToken);
                created.Add(imageFile);
            }

            return new ExtractResult(created, trackResults, ChdError.Chderrnone);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ExtractResult(created, trackResults,
                ex is InvalidDataException ? ChdError.Chderrdecompressionerror : ChdError.Chderrwriteerror);
        }
    }

    private void WriteAllBytesSlow(string path, IProgress<ChdProgress>? progress, CancellationToken cancellationToken)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
        var sw = progress != null ? Stopwatch.StartNew() : null;
        var buf = new byte[HunkBytes];
        for (uint i = 0; i < HunkCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var err = ReadHunk(i, buf, cancellationToken);
            if (err != ChdError.Chderrnone)
                throw new InvalidDataException($"Failed to read hunk {i}: {err}");

            var bytesToWrite = (i == HunkCount - 1)
                ? (int)(TotalBytes - (ulong)i * HunkBytes)
                : (int)HunkBytes;
            fs.Write(buf, 0, bytesToWrite);

            progress?.Report(new ChdProgress(
                i + 1,
                HunkCount,
                (long)Math.Min((i + 1) * (ulong)HunkBytes, TotalBytes),
                (long)TotalBytes,
                sw!.Elapsed));
        }
    }

    private ChdError TryWriteTrackToFile(ChdTrackInfo track, string path, IProgress<ChdProgress>? progress, CancellationToken cancellationToken)
    {
        var unitBytes = UnitBytes;
        var startByte = track.StartFrame * unitBytes;
        var totalBytes = (ulong)(track.Frames + track.ExtraFrames) * unitBytes;

        // Legacy GD-ROMs (CD_FLAG_GDROMLE) store CDDA audio little-endian. MAME byte-swaps only
        // the AUDIO track's 16-bit samples when reading them (cdrom.cpp:402), so do the same here.
        var swapCdda = _isLegacyGdRom &&
                       track.TrackType == ChdTrackType.Audio &&
                       unitBytes == ChdReaders.CdFrameSize;

        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
            var sw = progress != null ? Stopwatch.StartNew() : null;
            var buf = new byte[HunkBytes];
            var hunkSize = HunkBytes;
            var remaining = totalBytes;
            var offset = startByte;

            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var toRead = (int)Math.Min(hunkSize, remaining);
                var err = Read(offset, buf, 0, toRead, cancellationToken);
                if (err != ChdError.Chderrnone)
                    return err;

                if (swapCdda)
                {
                    // Swap only the 2352-byte sector-data portion of each 2448-byte frame.
                    ChdReaders.SwapCdda16(buf, toRead, ChdReaders.CdMaxSectorData, ChdReaders.CdFrameSize);
                }

                fs.Write(buf, 0, toRead);
                offset += (ulong)toRead;
                remaining -= (ulong)toRead;

                if (progress != null)
                {
                    var processed = (long)Math.Min(offset, TotalBytes);
                    var currentHunk = processed / hunkSize;
                    if (processed % hunkSize != 0)
                        currentHunk++;
                    progress.Report(new ChdProgress(currentHunk, HunkCount, processed, (long)TotalBytes, sw!.Elapsed));
                }
            }

            return ChdError.Chderrnone;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ChdError.Chderrwriteerror;
        }
    }

    private static string FramesToMsf(ulong frames)
    {
        var totalFrames = frames;
        var m = totalFrames / (60 * 75);
        totalFrames -= m * (60 * 75);
        var s = totalFrames / 75;
        var f = totalFrames % 75;
        return $"{m:D2}:{s:D2}:{f:D2}";
    }

    private static string FramesToMsf(int frames)
    {
        return FramesToMsf((ulong)frames);
    }

    /// <summary>An entry in the multi-hunk LRU cache: a decompressed hunk value keyed by hunk index.</summary>
    private sealed class CachedHunk
    {
        internal CachedHunk(uint hunk, byte[] data)
        {
            Hunk = hunk;
            Data = data;
        }

        /// <summary>Hunk index this entry holds.</summary>
        internal uint Hunk { get; }

        /// <summary>The cached decompressed hunk data (always <see cref="HunkBytes"/> long).</summary>
        internal byte[] Data { get; }
    }
}
