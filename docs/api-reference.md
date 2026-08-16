# API Reference

Complete reference for the public API of the `CHDSharp` package. All types live in the `CHDSharp` namespace (models in `CHDSharp.Models`).

---

## `Chd` — static class

Entry point for verification, quick checks, and global settings.

| Member | Signature | Description |
|--------|-----------|-------------|
| `LoggerFactory` | `static ILoggerFactory?` | Set to enable internal logging. See [Logging](logging.md). |
| `TaskCount` | `static int` (default 8) | Number of parallel workers for `CheckFile` (1–64). Set **before** calling. |
| `CheckFile` | `static ChdResult CheckFile(Stream s, string filename, bool deepCheck, IProgress<ChdProgress>? progress = null)` | Verify a standalone CHD. `deepCheck: true` decompresses every hunk and validates hashes; `false` is header-only. Reports progress per hunk when `progress` is supplied. |
| `CheckFile` | `static ChdError CheckFile(Stream, string, bool, out uint? version, out byte[]? sha1, out byte[]? md5, IProgress<ChdProgress>? progress = null)` | Out-parameter variant. |
| `CheckFileWithParent` | `static ChdResult CheckFileWithParent(string filename, string? parentFilename, IProgress<ChdProgress>? progress = null)` | Verify a (possibly child) CHD, resolving parent references. Pass `null` for standalone. Single-threaded. |
| `CheckFileWithParent` | `static ChdError CheckFileWithParent(string, string?, out uint?, out byte[]?, out byte[]?, IProgress<ChdProgress>? progress = null)` | Out-parameter variant. |
| `IsChdFile` | `static bool IsChdFile(string)` / `static bool IsChdFile(string, out uint version)` | Quick magic/version sniff. Never throws. |
| `CheckHeader` | `static bool CheckHeader(Stream, out uint length, out uint version)` | Validate signature + version; stream must be at position 0. |
| `ReadHeader` | `static ChdError ReadHeader(string, out ChdHeaderInfo? header)` | Parse the **full** header DTO from disk without keeping the file open (libchdr `chd_read_header` parity). |
| `ReadHeader` | `static ChdError ReadHeader(Stream, out ChdHeaderInfo? header)` | Header DTO from an existing seekable stream (stream left open). |
| `ReadHeaderAsync` | `static Task<(ChdError, ChdHeaderInfo?)> ReadHeaderAsync(string)` | Async variant. |
| `Classify` | `static ChdError Classify(string, out string? classification)` | Classify as `"cd"`, `"dvd"`, `"hdd"`, `"gd-rom"`, or `null` (unknown). |

---

## `ChdFile` — random-access reader

`public sealed class ChdFile : IDisposable, IAsyncDisposable`

Open a CHD once, then read hunks or byte ranges on demand. **Not thread-safe** — serialize all calls on one instance.

### Static factory methods

| Overload | Description |
|----------|-------------|
| `Open(string path, out ChdFile? chd)` | Standalone CHD from disk. |
| `Open(string path, string parentPath, out ChdFile? chd)` | Child CHD; the parent is opened internally and owned by the child. |
| `Open(string path, ChdFile? parent, out ChdFile? chd)` | Child with an external parent instance (caller keeps ownership; may be shared). Pass `null` for standalone. |
| `Open(Stream stream, bool leaveOpen, out ChdFile? chd)` | From any **seekable** readable stream. |
| `Open(Stream stream, bool leaveOpen, ChdFile? parent, out ChdFile? chd)` | From a stream with an external parent. |
| `OpenAsync(...)` | Async twins of **all** five overloads above. |

All overloads seek from the start. Failure codes: `Chderrfilenotfound`, `Chderrcannotopenfile`, `Chderrinvalidparameter`, `Chderrinvalidfile`, `Chderrreaderror`, `Chderrrequiresparent`, `Chderrinvalidparent`, `Chderrunsupportedversion`, `Chderrinvaliddata`.

### Instance methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `ReadHunk` | `ChdError ReadHunk(uint hunknum, byte[] buffer)` | Decompress one hunk into `buffer` (≥ `HunkBytes`). Serves cached hunks when `CacheSize > 1`. |
| `Read` | `ChdError Read(ulong byteOffset, byte[] destination, int destinationOffset, int count)` | Read an arbitrary byte range, crossing hunk boundaries. Caches the last hunk. |
| `ReadAllBytes` | `ChdError ReadAllBytes(out byte[] data, IProgress<ChdProgress>? progress = null)` | Decompress the whole image into one array. Reports per hunk when `progress` is supplied. `Chderroutofmemory` if the image exceeds 2 GiB. |
| `ConfigureCache` | `void ConfigureCache(int maxHunks)` | Set the multi-hunk LRU cache size (decompressed hunks retained). `<= 1` disables it (single-slot behaviour). See `CacheSize`. |
| `Precache` | `ChdError Precache()` | Read the **entire compressed file** into memory; subsequent hunk reads are served from RAM. Idempotent; restores stream position; `Chderroutofmemory` for files > 2 GiB, `Chderrreaderror` on IO failure. |
| `ReadAllBytes` | `ChdError ReadAllBytes(out byte[] data)` | Decompress the whole image into one array. `Chderroutofmemory` if the image exceeds 2 GiB. |
| `EnumerateHunks` | `IEnumerable<byte[]> EnumerateHunks(IProgress<ChdProgress>? progress = null)` | Yield each decompressed hunk in order. **The array is reused** — copy it if you need to keep it. Throws `InvalidDataException` on failure. Reports per hunk when `progress` is supplied. |
| `ReadHunkAsync` | `Task<ChdError> ReadHunkAsync(uint, byte[])` | Async hunk read. |
| `ReadAsync` | `Task<ChdError> ReadAsync(ulong, byte[], int, int)` | Async byte-range read. |
| `GetMetadata` | `ChdError GetMetadata(string? tag, uint index, out ChdMetadataEntry? entry)` | Search metadata by 4-char tag and occurrence index; `null`/empty tag = wildcard. Returns `Chderrmetadatanotfound` when absent. |
| `GenerateCueSheet` | `string GenerateCueSheet(string binFileName)` | CUE sheet (single-bin) for CD CHDs. |
| `GenerateGdiDescriptor` | `string GenerateGdiDescriptor(string[] trackFiles)` | GDI descriptor for GD-ROM CHDs. |
| `ExportToc` | `string ExportToc()` | Human-readable TOC dump. |
| `ExtractToDirectory` | `List<string> ExtractToDirectory(string outputDir, string baseFileName, IProgress<ChdProgress>? progress = null)` | Extract to files; returns created paths. Throws `InvalidDataException` on track failures. Reports per hunk when `progress` is supplied. |
| `ExtractToDirectoryWithReporting` | `ExtractResult ExtractToDirectoryWithReporting(string outputDir, string baseFileName, IProgress<ChdProgress>? progress = null)` | Reporting variant (per-track results, no exceptions). |
| `Dispose` / `DisposeAsync` | — | Release the stream (unless `leaveOpen`) and any internally-owned parent. |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Version` | `uint` | CHD format version (1–5). |
| `TotalBytes` | `ulong` | Decompressed image size. |
| `HunkBytes` | `uint` | Size of one hunk. |
| `CacheSize` | `int` | Number of decompressed hunks retained by the multi-hunk LRU cache (default 1). Set via `ConfigureCache(int)`. Memory capped at `CacheSize * HunkBytes`. |
| `MaxCompressedBlockBytes` | `uint` | Max allowed on-disk length of one compressed hunk. Defaults to `HunkBytes * 2`; a hunk claiming more is rejected with `Chderrinvaliddata` before allocation (OOM guard). Settable; floors at `HunkBytes`, set to `0` to reset. |
| `HunkCount` | `uint` | Total number of hunks. |
| `UnitBytes` | `uint` | Unit size for parent-block translation. V5: from header; V1–V4: derived from metadata (GDDD `BPS`, CD frame 2448, or `HunkBytes`). |
| `Sha1` | `byte[]?` | Combined SHA1 (raw data + checksummed metadata). |
| `RawSha1` | `byte[]?` | SHA1 of the raw image data only. |
| `Md5` | `byte[]?` | MD5 of the raw image (V1–V3). |
| `RequiresParent` | `bool` | True if this is a differential child. |
| `IsChild` | `bool` | Alias for `RequiresParent`. |
| `Tracks` | `IReadOnlyList<ChdTrackInfo>?` | CD/GD-ROM track layout; `null` for non-disc images. |
| `IsCd` | `bool` | CD-ROM track metadata present. |
| `IsGdRom` | `bool` | GD-ROM (Sega Dreamcast) image. |
| `IsLittleEndianAudio` | `bool` | True for legacy GD-ROMs (detected by the `CHGT` tag / `CD_FLAG_GDROMLE`) whose CDDA audio tracks are stored little-endian. AUDIO tracks are byte-swapped to big-endian order when extracted. |
| `IsDvd` | `bool` | DVD metadata present. |
| `IsHdd` | `bool` | Hard-disk geometry metadata present (V1/V2: via synthesized GDDD). |
| `Metadata` | `IReadOnlyList<ChdMetadataEntry>` | All metadata entries, lazy-loaded. V1/V2 include a synthesized `GDDD` entry. |

---

## `ChdResult` — verification result (record)

| Property | Type | Description |
|----------|------|-------------|
| `Error` | `ChdError` | Result code. |
| `Version` | `uint?` | CHD version (1–5). |
| `Sha1` | `byte[]?` | SHA1 from the header. |
| `Md5` | `byte[]?` | MD5 from the header. |
| `IsSuccess` | `bool` | `Error == Chderrnone`. |
| `Sha1Hex` | `string` | Lowercase hex, or `"(none)"`. |
| `Md5Hex` | `string` | Lowercase hex, or `"(none)"`. |

Supports deconstruction: `var (err, ver, sha1, md5) = result;`

---

## `ChdHeaderInfo` — full header DTO (record)

Returned by `Chd.ReadHeader(...)`. A snapshot of everything in the CHD header, parsed without opening the file for hunk reads and without keeping a file handle open.

| Property | Type | Description |
|----------|------|-------------|
| `Length` | `uint` | On-disk header length (76 / 80 / 120 / 108 / 124 for V1–V5). |
| `Version` | `uint` | CHD format version (1–5). |
| `Flags` | `uint` | Raw flags field (V1–V4): bit 0 = has parent, bit 1 = writable. Always 0 for V5. |
| `Compression` | `ChdCodec[]` | Codec slots (up to 4 for V5; V1–V4 use slot 0). All `None` = uncompressed V5. |
| `HunkBytes` | `uint` | Size of one hunk. |
| `TotalHunks` | `uint` | Total number of hunks. |
| `TotalBytes` | `ulong` | Decompressed image size. |
| `MetaOffset` | `ulong` | File offset of the first metadata entry (0 = none). |
| `MapOffset` | `ulong` | File offset of the block map (V5 only; 0 for V1–V4). |
| `Md5` / `ParentMd5` | `byte[]?` | MD5 hashes (V1–V3; `null` for V4/V5). |
| `Sha1` / `RawSha1` / `ParentSha1` | `byte[]?` | SHA1 hashes (V3–V5; `null` for V1/V2). |
| `UnitBytes` | `uint` | Unit size. V5: from header; V1–V4: derived from metadata (matches `ChdFile.UnitBytes`). |
| `UnitCount` | `ulong` | `ceil(TotalBytes / UnitBytes)` (0 if `UnitBytes` is 0). |
| `HasParent` | `bool` | True if this is a differential child (derived from parent hashes). |
| `ObsoleteCylinders` / `ObsoleteHeads` / `ObsoleteSectors` / `ObsoleteHunksize` | `uint` | Obsolete V1/V2 hard-disk geometry (0 for V3+). |

---

## `ChdProgress` — long-operation progress (record)

Pass an `IProgress<ChdProgress>` to `Chd.CheckFile`, `Chd.CheckFileWithParent`, `ChdFile.ReadAllBytes`, `ChdFile.EnumerateHunks`, or `ChdFile.ExtractToDirectory` to receive a report **after each decompressed hunk**. Wrap it in `new Progress<ChdProgress>(...)` for UI binding or logging.

| Property | Type | Description |
|----------|------|-------------|
| `CurrentHunk` | `long` | Hunks processed so far (1-based; equals `TotalHunks` when done). |
| `TotalHunks` | `long` | Total hunks in the image. |
| `BytesProcessed` | `long` | Decompressed bytes processed so far. |
| `TotalBytes` | `long` | Total decompressed image size. |
| `Elapsed` | `TimeSpan` | Wall-clock time since the operation started. |
| `Percent` | `double` | `CurrentHunk / TotalHunks × 100` (0–100). |

```csharp
var progress = new Progress<ChdProgress>(p =>
    Console.WriteLine($"{p.Percent:F0}% — {p.BytesProcessed:N0}/{p.TotalBytes:N0} bytes ({p.Elapsed.TotalSeconds:F1}s)"));

var result = Chd.CheckFile(File.OpenRead("game.chd"), "game.chd", deepCheck: true, progress);
```

For `Chd.CheckFile(deepCheck: true)`, reports arrive in hunk order from the internal hashing thread — an `IProgress<T>` built with `new Progress<ChdProgress>(...)` marshals them back to the capturing context automatically. All parameters default to `null`, so existing callers are unaffected.

---

## `ChdMetadataEntry` — metadata record

`public record ChdMetadataEntry(string Tag, byte[] Data)`

| Member | Type | Description |
|--------|------|-------------|
| `Tag` | `string` | 4-char tag, e.g. `"GAME"`, `"DISC"`, `"HARD"`, `"GDDD"`, `"CHT2"`. |
| `Data` | `byte[]` | Raw payload bytes (ASCII text or binary). |
| `Flags` | `byte` (init) | Entry flags from the header (bit 0 = checksummed). |
| `IsText` | `bool` | True if the data is printable ASCII. |
| `GetText()` | `string` | ASCII text representation (empty for oversized data). |
| `ToString()` | `string` | `GAME: gauntlet` or `TAG: N bytes`. |

Equality is based on `Tag` + `Data` only (the `Flags` byte is excluded).

---

## `ChdTrackInfo` — CD/GD-ROM track (class)

| Property | Type | Description |
|----------|------|-------------|
| `TrackNumber` | `int` | 1-based track number. |
| `TrackType` | `ChdTrackType` | Mode1, Mode1Raw, Mode2, Mode2Form1, Mode2Form2, Mode2FormMix, Mode2Raw, Audio. |
| `SubType` | `ChdSubType` | None, Normal, Raw. |
| `DataSize` | `int` | Bytes per sector (2048, 2352, …). |
| `SubSize` | `int` | Subcode bytes per sector (0 or 96). |
| `Frames` | `int` | Frames in the track. |
| `ExtraFrames` | `int` | Padding frames (4-frame alignment). |
| `PreGap` | `int` | Pregap frames (index 00 → 01). |
| `PostGap` | `int` | Postgap frames. |
| `PreGapType` / `PreGapSubType` | `ChdTrackType` / `ChdSubType` | Pregap sector format. |
| `PreGapDataSize` / `PreGapSubSize` | `int` | Pregap sector sizes. |
| `PadFrames` | `int` | GD-ROM pad frames. |
| `StartFrame` | `ulong` | CHD frame offset where the track starts. |
| `GetTypeString()` | `string` | e.g. `"MODE1/2048"`, `"AUDIO"`. |
| `GetSubTypeString()` | `string` | e.g. `"RW"`, `"RW_RAW"`, `"NONE"`. |

---

## `ExtractResult` / `TrackExtractResult`

`ExtractResult` (record): `CreatedFiles` (`IReadOnlyList<string>`), `TrackResults` (`IReadOnlyList<TrackExtractResult>`), `Error` (`ChdError`), `IsCompleteSuccess`, `HasTrackFailures`.

`TrackExtractResult` (record): `TrackNumber`, `FilePath` (`string?`), `Error`, `IsSuccess`.

---

## Enums

### `ChdCodec` — 4-char codec tags

`None = 0`, `Zlib` (`zlib`), `Lzma` (`lzma`), `Huffman` (`huff`), `Flac` (`flac`), `Zstd` (`zstd`), `Cdzlib` (`cdzl`), `Cdlzma` (`cdlz`), `Cdflac` (`cdfl`), `Cdzstd` (`cdzs`), `Avhuff` (`avhu`), `Error`.

### `ChdTrackType`

`Mode1 = 0`, `Mode1Raw = 1`, `Mode2 = 2`, `Mode2Form1 = 3`, `Mode2Form2 = 4`, `Mode2FormMix = 5`, `Mode2Raw = 6`, `Audio = 7`.

### `ChdSubType`

`None = 0`, `Normal = 1`, `Raw = 2`.

### `CompressionType` — per-hunk map entry types

`Compressiontype0..3` (codec slots), `Compressionnone`, `Compressionself`, `Compressionparent`, `Compressionrlesmall`, `Compressionrlelarge`, `Compressionself0/1`, `Compressionparentself`, `Compressionparent0/1`, `Compressionmini`, `Compressionerror`, `Compressionzero`, `Compressiontype2Nd`.

### `ChdError`

The complete 29-value error enum — see [Error Codes](error-codes.md). Every value has a human-readable message via the `GetMessage()` extension:

```csharp
ChdError err = ChdFile.Open("missing.chd", out _);
Console.WriteLine(err.GetMessage());   // "File not found"
```

---

## Extension methods

`ChdErrorExtensions.GetMessage(this ChdError)` — human-readable error text. `ChdSharp` also exposes big-endian helpers (`BigEndian`/`EndianHelpers`) internally for the test suite.
