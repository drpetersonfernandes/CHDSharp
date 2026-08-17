[![.NET](https://img.shields.io/badge/.NET-8.0_|_9.0_|_10.0-blueviolet)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/CHDSharp?color=blue)](https://www.nuget.org/packages/CHDSharp/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

# CHDSharpLib

**Pure C# read-only CHD (Compressed Hunks of Data) library — V1–V5, all 10 codecs, parent/child chaining, parallel verification, 100% match with MAME chdman.**

> Fork of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp) by [Gordon Jefferyes](https://github.com/gjefferyes), extended with Zstd, AVHuff, V5 compressed map, random-access API, parent/child chaining, and parallel verification.

---

## What's New in v1.2.0

- **CD/GD-ROM track (TOC) parsing** — Full track layout via `Tracks` property backed by `ChdTocParser`, exposing `ChdTrackInfo` with track type, sector sizes, pregap/postgap, and GD-ROM support. Legacy GD-ROMs (`CHGT` / `CD_FLAG_GDROMLE`) are detected via `IsLittleEndianAudio` and their AUDIO tracks byte-swapped during extraction. Includes `GenerateCueSheet()`, `GenerateGdiDescriptor()`, `ExportToc()`, `ExtractToDirectory()`.
- **`UnitBytes` property** — Derives sector size from metadata for all CHD versions: V5 reads from header, V1-V4 detects HDD (512B) or CD (2448B) from metadata tags
- **New enums** — `ChdTrackType` (matches MAME `cdrom.h`: Mode1, Mode2, Audio, etc.) and `ChdSubType` (None, Normal, Raw)
- **Deterministic reproducible builds** — Byte-for-byte reproducible via `<Deterministic>true</Deterministic>` with embedded SourceLink and debug symbols

---

## Installation

```bash
dotnet add package CHDSharp
```

Targets `net8.0`, `net9.0`, and `net10.0`. No native dependencies — all codecs (except Zstd via the pure-C# `ZstdSharp.Port`) are implemented from scratch in C#.

---

## Quick Start

### Verify a standalone CHD (parallel, fast)

```csharp
using CHDSharp;
using CHDSharp.Models;

using Stream s = File.OpenRead("game.chd");
var result = Chd.CheckFile(s, "game.chd", deepCheck: true);

if (result.IsSuccess)
    Console.WriteLine($"V{result.Version} — SHA1: {result.Sha1Hex}");
else
    Console.WriteLine($"Error: {result.Error.GetMessage()}");
```

### Verify a child (differential) CHD against its parent

```csharp
var result = Chd.CheckFileWithParent("child.chd", "parent.chd");
```

### Random-access reading

```csharp
var err = ChdFile.Open("game.chd", out var chd);
if (err != ChdError.Chderrnone) return;

using (chd)
{
    // Inspect metadata (game name, disc label, etc.)
    foreach (var meta in chd.Metadata)
        Console.WriteLine(meta.ToString());

    // Read a single decompressed hunk
    byte[] hunk = new byte[chd.HunkBytes];
    chd.ReadHunk(42, hunk);

    // Read arbitrary byte range (handles hunk boundaries)
    byte[] buf = new byte[1024];
    chd.Read(offset: 0x10000, buf, 0, buf.Length);
}
```

### Async random-access reading

```csharp
var (err, chd) = await ChdFile.OpenAsync("game.chd");
if (err != ChdError.Chderrnone) return;

await using (chd)
{
    byte[] hunk = new byte[chd.HunkBytes];
    await chd.ReadHunkAsync(42, hunk);
}
```

### Quick file checking

```csharp
bool isChd = Chd.IsChdFile("game.chd", out uint version);
// isChd=true, version=5 for a V5 CHD

// Or just yes/no:
bool yesNo = Chd.IsChdFile("game.chd");
```

### Read the full header without opening the file (libchdr `chd_read_header` parity)

```csharp
var err = Chd.ReadHeader("game.chd", out ChdHeaderInfo? header);
if (err == ChdError.Chderrnone)
{
    Console.WriteLine($"V{header.Version}, {header.TotalBytes:N0} bytes, " +
                      $"{header.TotalHunks} hunks x {header.HunkBytes}");
    Console.WriteLine($"Codecs: {string.Join(", ", header.Compression)}");
    Console.WriteLine($"Parent required: {header.HasParent}");
}

// Async + stream variants:
var (aerr, aHeader) = await Chd.ReadHeaderAsync("game.chd");
Chd.ReadHeader(File.OpenRead("game.chd"), out ChdHeaderInfo? sHeader); // stream left open
```

The file is opened, parsed, and closed again — no handle is kept alive. Stream and async variants are also available.

### Decompress entire image to a byte array

```csharp
ChdFile.Open("game.chd", out var chd);
using (chd)
{
    chd.ReadAllBytes(out byte[] image);
    // image now contains the full decompressed image
}
```

### Get CD/GD-ROM track layout (TOC)

```csharp
ChdFile.Open("game.chd", out var chd);
using (chd)
{
    if (chd.Tracks is not { } tracks) return;
    foreach (var track in tracks)
    {
        Console.WriteLine($"Track {track.TrackNumber}: {track.GetTypeString()} " +
                          $"{track.Frames} frames, pregap={track.PreGap}");
    }
}
```

### Iterate hunks one at a time

```csharp
ChdFile.Open("game.chd", out var chd);
using (chd)
{
    foreach (byte[] hunk in chd.EnumerateHunks())
    {
        // Process each decompressed hunk; buffer is reused — copy if needed
    }
}
```

---

## Logging

The library uses `Microsoft.Extensions.Logging.Abstractions`. By default, logging is discarded. To enable logging (e.g., with Serilog):

```csharp
using Serilog;
using Serilog.Extensions.Logging;

var serilogLogger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

Chd.LoggerFactory = new SerilogLoggerFactory(serilogLogger);

// All subsequent Chd/ChdFile operations will log through Serilog
```

You can use any `ILoggerFactory`-compatible provider (NLog, Microsoft.Extensions.Logging.Console, etc.).

---

## API Reference

### `Chd` — Static class

| Member | Signature | Description |
|--------|-----------|-------------|
| **LoggerFactory** | `ILoggerFactory?` (static property) | Set to enable internal logging. |
| **TaskCount** | `int` (static property, default 8) | Number of parallel workers for `CheckFile` (1-64). Change before calling. |
| **CheckFile** | `ChdResult CheckFile(Stream, string, bool, IProgress<ChdProgress>? = null, CancellationToken = default)` | Full parallel verification. Returns error, version, SHA1, MD5. Reports progress per hunk; cancellable. |
| **CheckFileWithParent** | `ChdResult CheckFileWithParent(string, string, IProgress<ChdProgress>? = null, CancellationToken = default)` | Verify child CHD against parent. Pass `null` for second arg for standalone. Reports progress per hunk; cancellable. |
| **CheckHeader** | `bool CheckHeader(Stream, out uint length, out uint version)` | Sniff magic + version. Stream must be at position 0. |
| **IsChdFile** | `bool IsChdFile(string)` / `bool IsChdFile(string, out uint)` | Quick check if a file is a valid CHD. |
| **ReadHeader** | `ChdError ReadHeader(string, out ChdHeaderInfo?)` / `ChdError ReadHeader(Stream, out ChdHeaderInfo?)` / `Task<(ChdError, ChdHeaderInfo?)> ReadHeaderAsync(string)` | Parse the **full** header DTO (version, flags, codec slots, sizes, hashes, unit info, parent linkage) without opening the file for reads or keeping a handle. libchdr `chd_read_header` parity. |

### `ChdResult` — Verification result

| Property | Type | Description |
|----------|------|-------------|
| `Error` | `ChdError` | Error code (ChderrNone on success). |
| `Version` | `uint?` | CHD version (1-5). |
| `Sha1` | `byte[]?` | SHA1 hash from header. |
| `Md5` | `byte[]?` | MD5 hash from header. |
| `IsSuccess` | `bool` | True if Error == ChderrNone. |
| `Sha1Hex` | `string` | SHA1 as lowercase hex, or "(none)". |
| `Md5Hex` | `string` | MD5 as lowercase hex, or "(none)". |

Supports deconstruction: `var (err, ver, sha1, md5) = result;`

### `ChdHeaderInfo` — Full header DTO

Returned by `Chd.ReadHeader(...)`. A snapshot of the CHD header without keeping the file open.

| Property | Type | Description |
|----------|------|-------------|
| `Length` | `uint` | On-disk header length (76/80/120/108/124 for V1-V5). |
| `Version` | `uint` | CHD format version (1-5). |
| `Flags` | `uint` | Raw flags (V1-V4): bit 0 = has parent, bit 1 = writable. 0 for V5. |
| `Compression` | `ChdCodec[]` | Codec slots (up to 4 for V5). |
| `HunkBytes` / `TotalHunks` | `uint` | Hunk size / hunk count. |
| `TotalBytes` | `ulong` | Decompressed image size. |
| `MetaOffset` / `MapOffset` | `ulong` | Metadata / V5 map file offsets. |
| `Md5` / `ParentMd5` | `byte[]?` | MD5 hashes (V1-V3). |
| `Sha1` / `RawSha1` / `ParentSha1` | `byte[]?` | SHA1 hashes (V3-V5). |
| `UnitBytes` / `UnitCount` | `uint` / `ulong` | Unit size / count (matches `ChdFile.UnitBytes`). |
| `HasParent` | `bool` | True if a differential child requiring a parent. |
| `ObsoleteCylinders/Heads/Sectors/Hunksize` | `uint` | Obsolete V1/V2 hard-disk geometry. |

### `ChdProgress` — Long-operation progress

Pass an `IProgress<ChdProgress>` to `Chd.CheckFile`, `Chd.CheckFileWithParent`, `ChdFile.ReadAllBytes`, `ChdFile.EnumerateHunks`, or `ChdFile.ExtractToDirectory` to receive a report after every decompressed hunk.

| Property | Type | Description |
|----------|------|-------------|
| `CurrentHunk` | `long` | Hunks processed so far (1-based; equals `TotalHunks` when done). |
| `TotalHunks` | `long` | Total hunks in the image. |
| `BytesProcessed` | `long` | Decompressed bytes processed so far. |
| `TotalBytes` | `long` | Total decompressed image size. |
| `Elapsed` | `TimeSpan` | Wall-clock time since the operation started. |
| `Percent` | `double` | Percentage completed (0–100). |

```csharp
var progress = new Progress<ChdProgress>(p =>
    Console.WriteLine($"{p.Percent:F0}% — {p.BytesProcessed:N0}/{p.TotalBytes:N0} bytes ({p.Elapsed.TotalSeconds:F1}s)"));

var result = Chd.CheckFile(File.OpenRead("game.chd"), "game.chd", deepCheck: true, progress);
```

All parameters default to `null`, so existing callers are unaffected. For `Chd.CheckFile(deepCheck: true)`, reports arrive in hunk order from the internal hashing thread; `new Progress<ChdProgress>(...)` marshals them back to the capturing context automatically.

### Cancellation

All long-running methods take an optional trailing `CancellationToken` (default `default`) and throw `OperationCanceledException` on cancellation: `Chd.CheckFile`, `Chd.CheckFileWithParent`, `ChdFile.Open`/`OpenAsync` (all overloads), `ReadHunk`/`ReadHunkAsync`, `Read`/`ReadAsync`, `ReadAllBytes`, and `ExtractToDirectory`/`ExtractToDirectoryWithReporting`. For deep verification the token is linked into the pipeline's internal `CancellationTokenSource`, so cancel stops the workers immediately and the method throws OCE instead of reporting a bogus partial-hash mismatch. Async twins also pass the token to `Task.Run` (a pre-cancelled token yields a cancelled task). Cancellation is never swallowed into an error result.

```csharp
using var cts = new CancellationTokenSource();
var result = Chd.CheckFile(File.OpenRead("game.chd"), "game.chd", deepCheck: true, cancellationToken: cts.Token);
```

### `ChdFile` — Random-access reader

All `Open` overloads seek from the start. The reader is **not thread-safe** — serialize all calls.

#### Static factory methods

| Overload | Description |
|----------|-------------|
| `Open(string path, out ChdFile? chd, CancellationToken = default)` | Standalone CHD from disk. |
| `Open(string path, string parentPath, out ChdFile? chd, CancellationToken = default)` | Child CHD; parent opened and owned internally. |
| `Open(string path, ChdFile? parent, out ChdFile? chd, CancellationToken = default)` | Child with external parent. Pass null for standalone. |
| `Open(Stream s, bool leaveOpen, out ChdFile? chd, CancellationToken = default)` | From seekable stream. |
| `Open(Stream s, bool leaveOpen, ChdFile? parent, out ChdFile? chd, CancellationToken = default)` | From stream with external parent. |
| `OpenAsync(...)` | Async overloads for all `Open` variants, each with an optional trailing `CancellationToken`. |

#### Instance methods

| Method | Signature | Description |
|--------|-----------|-------------|
| **ReadHunk** | `ChdError ReadHunk(uint, byte[], CancellationToken = default)` | Decompress a single hunk. Serves cached hunks when `CacheSize > 1`. |
| **Read** | `ChdError Read(ulong, byte[], int, int, CancellationToken = default)` | Read byte range. Caches last hunk. |
| **ReadAllBytes** | `ChdError ReadAllBytes(out byte[], IProgress<ChdProgress>? = null, CancellationToken = default)` | Decompress entire image to a `byte[]`. Reports progress per hunk. |
| **ConfigureCache** | `void ConfigureCache(int)` | Set the multi-hunk LRU cache size. `<= 1` disables it (single-slot behaviour). |
| **Precache** | `ChdError Precache()` | Load the entire compressed file into memory for fast random access (libchdr `chd_precache` parity). Idempotent. |
| **GetMetadata** | `ChdError GetMetadata(string?, uint, out ChdMetadataEntry?)` | Search metadata by tag + occurrence index (`null`/empty tag = wildcard). Returns `Chderrmetadatanotfound` when absent. |
| **EnumerateHunks** | `IEnumerable<byte[]> EnumerateHunks(IProgress<ChdProgress>? = null)` | Yield each decompressed hunk. Buffer reused — copy if needed. Reports progress per hunk. |
| **ReadHunkAsync** | `Task<ChdError> ReadHunkAsync(uint, byte[], CancellationToken = default)` | Async hunk read; cancellable. |
| **ReadAsync** | `Task<ChdError> ReadAsync(ulong, byte[], int, int, CancellationToken = default)` | Async byte range read; cancellable. |
| **GenerateCueSheet** | `string GenerateCueSheet(string)` | Generate CUE sheet for CD CHDs. |
| **GenerateGdiDescriptor** | `string GenerateGdiDescriptor(string[])` | Generate GDI descriptor for GD-ROM CHDs. |
| **ExportToc** | `string ExportToc()` | Export TOC as human-readable text. |
| **ExtractToDirectory** | `List<string> ExtractToDirectory(string, string, IProgress<ChdProgress>? = null, CancellationToken = default)` | Extract CHD tracks to directory. Returns file paths. Reports progress per hunk; cancellable. |
| **Dispose** / **DisposeAsync** | `void Dispose()` / `ValueTask DisposeAsync()` | Release stream and parent. |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Version` | `uint` | CHD format version (1–5). |
| `TotalBytes` | `ulong` | Decompressed image size. |
| `HunkBytes` | `uint` | Size of one hunk. |
| `CacheSize` | `int` | Number of decompressed hunks retained by the multi-hunk LRU cache (default 1). Set via `ConfigureCache(int)`. Memory capped at `CacheSize * HunkBytes`. |
| `MaxCompressedBlockBytes` | `uint` | Max allowed on-disk length of one compressed hunk. Defaults to `HunkBytes * 2`; a hunk claiming more is rejected with `Chderrinvaliddata` before allocation (OOM guard). Settable; floors at `HunkBytes`, set to `0` to reset. |
| `HunkCount` | `uint` | Total number of hunks. |
| `UnitBytes` | `uint` | Unit size for parent block address translation. V5 reads from header; V1-V4 derives from metadata (HDD BPS, CD 2448, or HunkBytes). |
| `Sha1` | `byte[]?` | Combined SHA1 (image + metadata). |
| `RawSha1` | `byte[]?` | Raw image data SHA1. |
| `Md5` | `byte[]?` | Raw image MD5. |
| `RequiresParent` | `bool` | True if differential child. |
| `IsChild` | `bool` | Alias for `RequiresParent`. |
| `Tracks` | `IReadOnlyList<ChdTrackInfo>?` | CD/GD-ROM track layout. `null` if not a CD/GD-ROM image. |
| `IsCd` | `bool` | True if CD-ROM track metadata present. |
| `IsGdRom` | `bool` | True if GD-ROM (Sega Dreamcast) image. |
| `IsLittleEndianAudio` | `bool` | True for legacy GD-ROMs (`CHGT` tag / `CD_FLAG_GDROMLE`) whose CDDA audio tracks are stored little-endian. AUDIO tracks are byte-swapped during extraction. |
| `IsDvd` | `bool` | True if DVD metadata present. |
| `IsHdd` | `bool` | True if hard disk geometry metadata present. |
| `Metadata` | `IReadOnlyList<ChdMetadataEntry>` | CHD metadata entries (game name, disc type, etc.). Lazy-loaded. V1/V2 files include a synthesized `GDDD` entry. |

### `ChdMetadataEntry` — Metadata record

| Property | Type | Description |
|----------|------|-------------|
| `Tag` | `string` | 4-char tag (e.g. "GAME", "DISC", "HARD"). |
| `Data` | `byte[]` | Raw metadata bytes. |
| `Flags` | `byte` | Entry flags from the header (bit 0 = checksummed). |
| `IsText` | `bool` | True if data is printable ASCII. |
| `GetText()` | `string` | ASCII text representation. |
| `ToString()` | `string` | Human-readable: `GAME: gauntlet`. |

### `ChdTrackInfo` — Track record

| Property | Type | Description |
|----------|------|-------------|
| `TrackNumber` | `int` | 1-based track number. |
| `TrackType` | `ChdTrackType` | CD track data type (Mode1, Audio, etc.). |
| `SubType` | `ChdSubType` | Subcode type for this track. |
| `DataSize` | `int` | Bytes per sector (2048, 2352, etc.). |
| `SubSize` | `int` | Subcode bytes per sector (0 or 96). |
| `Frames` | `int` | Number of frames in this track. |
| `ExtraFrames` | `int` | Padding frames for 4-frame alignment. |
| `PreGap` | `int` | Pregap frames (index 00 to index 01). |
| `PostGap` | `int` | Postgap frames. |
| `PreGapType` | `ChdTrackType` | Track type of pregap sectors. |
| `PreGapSubType` | `ChdSubType` | Subcode type of pregap sectors. |
| `PreGapDataSize` | `int` | Bytes per sector for pregap data. |
| `PreGapSubSize` | `int` | Subcode bytes per sector for pregap. |
| `PadFrames` | `int` | GD-ROM pad frames (GD-ROM only). |
| `StartFrame` | `ulong` | CHD frame offset where this track starts. |
| `GetTypeString()` | `string` | e.g. "MODE1/2048", "AUDIO". |
| `GetSubTypeString()` | `string` | e.g. "RW", "RW_RAW", "NONE". |

### `ChdError.GetMessage()` — Extension method

```csharp
ChdError err = ChdFile.Open("bad.chd", out _);
Console.WriteLine(err.GetMessage());
// "File not found"
```

---

## Supported Formats

### CHD Versions

| Version | Header | Map Type | Status |
|---------|--------|----------|--------|
| V1 | 76 bytes | Self-hunk dedup via offset | ✅ |
| V2 | 80 bytes | Self-hunk dedup via offset | ✅ |
| V3 | 120 bytes | CRC32 map, self-hunk | ✅ |
| V4 | 108 bytes | CRC32 map, parent chain | ✅ |
| V5 | 124 bytes | CRC16 map, compressed/uncompressed map, RLE, parent/unit chain | ✅ |

### Compression Codecs

| Codec | FourCC | CD Variant | Implementation |
|-------|--------|------------|----------------|
| **Zlib** (Deflate) | `zlib` | `cdzl` | `System.IO.Compression` (managed) |
| **LZMA** | `lzma` | `cdlz` | Custom pure C# LZMA decoder |
| **Huffman** | `huff` | — | Custom pure C# Huffman decoder |
| **FLAC** | `flac` | `cdfl` | Custom pure C# FLAC decoder (16-bit stereo/mono) |
| **Zstd** | `zstd` | `cdzs` | [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) (pure C#) |
| **AVHuff** | `avhu` | — | Custom pure C# AV Huffman decoder |

---

## Common Usage Patterns

### Pattern 1: Fast batch verification

```csharp
var files = Directory.GetFiles(@"D:\CHD", "*.chd");
foreach (var path in files)
{
    using var s = File.OpenRead(path);
    var result = Chd.CheckFile(s, Path.GetFileName(path), deepCheck: true);
    Console.WriteLine($"{Path.GetFileName(path)}: {result.Error.GetMessage()}");
}
```

### Pattern 2: Universal verification (standalone or child)

```csharp
static ChdError UniversalVerify(string path, string? parentPath = null)
{
    if (parentPath != null)
    {
        var r = Chd.CheckFileWithParent(path, parentPath);
        return r.Error;
    }

    using var s = File.OpenRead(path);
    var result = Chd.CheckFile(s, Path.GetFileName(path), deepCheck: true);
    if (result.Error == ChdError.Chderrrequiresparent)
        Console.WriteLine("  -> requires parent CHD");
    return result.Error;
}
```

### Pattern 3: Working with child (differential) CHDs

```csharp
// Option A: Let the library manage parent lifetime
ChdFile.Open("child.chd", "parent.chd", out var child);
child?.Dispose();

// Option B: Share parent across multiple children
ChdFile.Open("parent.chd", out var parent);
using (parent)
{
    foreach (var childPath in new[] { "child1.chd", "child2.chd" })
    {
        ChdFile.Open(childPath, parent, out var c);
        using (c) { /* read hunks */ }
    }
}
```

### Pattern 4: Computing SHA1 while streaming

```csharp
using var sha1 = System.Security.Cryptography.SHA1.Create();
ChdFile.Open("game.chd", out var chd);
using (chd)
{
    var buf = new byte[chd.HunkBytes];
    var remaining = chd.TotalBytes;
    ulong offset = 0;
    while (remaining > 0)
    {
        var chunk = (int)Math.Min((ulong)buf.Length, remaining);
        chd.Read(offset, buf, 0, chunk);
        sha1.TransformBlock(buf, 0, chunk, null, 0);
        offset += (ulong)chunk;
        remaining -= (ulong)chunk;
    }
    sha1.TransformFinalBlock([], 0, 0);
    Console.WriteLine($"SHA1: {Convert.ToHexString(sha1.Hash!).ToLower()}");
}
```

---

## Performance

| Scenario | Throughput | Notes |
|----------|------------|-------|
| `CheckFile(deepCheck: true)` | ~200–400 MB/s | 8 parallel threads, bounded memory |
| `CheckFile(deepCheck: false)` | > 1 GB/s | Header-only |
| `ChdFile.Read()` sequential | ~150–300 MB/s | Single-threaded, hunk-cached |
| `ChdFile.ReadHunk()` random | ~50–150 MB/s | Per-hunk re-decompression |

### Tuning parallelism

```csharp
Chd.TaskCount = 16; // set before calling CheckFile
var result = Chd.CheckFile(s, name, deepCheck: true);
```

---

## Architecture

```
┌────────────────────────────────────────────────────┐
│                    Public API                       │
│  Chd.CheckFile()  ChdFile.Open()  ChdFile.Read()   │
├────────────────────────────────────────────────────┤
│  CHDHeaders    →  Parse V1–V5 headers + maps       │
│  CHDBlockRead  →  Dispatch hunk → codec delegate   │
│  CHDReaders    →  Decompression delegates (10)     │
│  CHDCodec      →  Per-codec reusable state         │
│  CHDMetaData   →  Metadata traversal + SHA1 check   │
├────────────────────────────────────────────────────┤
│  Utils/                                             │
│  CRC · CRC16 · BitStream · HuffmanDecoder ·        │
│  HuffmanDecoderRLE · BigEndian · ArrayPool · cdRom  │
├────────────────────────────────────────────────────┤
│  LZMA/                                              │
│  LzmaStream · LzmaDecoder · RangeCoder ·           │
│  LzBinTree · LzInWindow · LzOutWindow               │
├────────────────────────────────────────────────────┤
│  Flac/                                              │
│  AudioDecoder · FlacFrame · FlacSubframe ·         │
│  BitReader · LPC · RiceContext · WindowFunction     │
├────────────────────────────────────────────────────┤
│  ZstdSharp.Port  (NuGet)                            │
└────────────────────────────────────────────────────┘
```

---

## Building

```bash
dotnet build CHDSharpLib/CHDSharpLib.csproj -c Release
dotnet pack CHDSharpLib/CHDSharpLib.csproj -c Release
```

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port/) | 0.8.8 | Pure C# Zstd decompression |
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/) | 10.0.11 (all TFMs: net8.0 / net9.0 / net10.0) | Pluggable logging (optional) |

---

## Limits

- **Read-only** — This library cannot create, modify, or repack CHD files. For *creation* (raw and CD images, `chdman`-matched output), use the companion [`CHDSharpEncoder`](../CHDSharpEncoder/README.md).
- **Not thread-safe** — `ChdFile` instances must be used from a single thread
- **No lossy video** — Lossy AVHuff video variants are not supported
- **Stream must be seekable** — for `ChdFile.Open` stream overloads
- **V6+ not supported** — MAME has not released a V6 format

---

## License

MIT License — see [LICENSE](LICENSE).

---

## Acknowledgments

- **[Gordon Jefferyes](https://github.com/gjefferyes)** — original C# CHDSharp implementation
- **[MAME](https://www.mamedev.org/)** — CHD format specification and `chdman` reference
- **[libchdr](https://github.com/rtissera/libchdr)** — C reference library by Romain Tisseraud
- **[ZstdSharp](https://github.com/oleg-st/ZstdSharp)** — pure C# Zstd decompressor
