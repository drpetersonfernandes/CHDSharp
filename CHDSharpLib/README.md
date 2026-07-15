[![.NET](https://img.shields.io/badge/.NET-8.0_|_9.0_|_10.0-blueviolet)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/CHDSharp?color=blue)](https://www.nuget.org/packages/CHDSharp/)
[![License](https://img.shields.io/badge/license-GPL--3.0-green)](../LICENSE)

# CHDSharpLib

**Pure C# read-only CHD (Compressed Hunks of Data) library — V1–V5, all 10 codecs, parent/child chaining, parallel verification, 100% match with MAME chdman.**

> Fork of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp) by [Gordon Jefferyes](https://github.com/gjefferyes), extended with Zstd, AVHuff, V5 compressed map, random-access API, parent/child chaining, and parallel verification.

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

using Stream s = File.OpenRead("game.chd");
ChdError err = Chd.CheckFile(s, "game.chd", deepCheck: true,
    out uint? version, out byte[]? sha1, out byte[]? md5);

if (err == ChdError.Chderrnone)
{
    Console.WriteLine($"V{version} — SHA1: {Convert.ToHexString(sha1!).ToLower()}");
}
```

### Verify a child (differential) CHD against its parent

```csharp
ChdError err = Chd.CheckFileWithParent("child.chd", "parent.chd",
    out uint? version, out byte[]? sha1, out byte[]? md5);
```

### Random-access reading

```csharp
ChdError err = ChdFile.Open("game.chd", out ChdFile? chd);
if (err != ChdError.Chderrnone) return;

using (chd)
{
    Console.WriteLine($"V{chd.Version}: {chd.TotalBytes} bytes, " +
                      $"{chd.HunkCount} hunks x {chd.HunkBytes} bytes");

    // Read a single decompressed hunk
    byte[] hunk = new byte[chd.HunkBytes];
    chd.ReadHunk(42, hunk);

    // Read arbitrary byte range (handles hunk boundaries)
    byte[] buf = new byte[1024];
    chd.Read(offset: 0x10000, buf, 0, buf.Length);
}
```

### Parent/child reading

```csharp
ChdFile.Open("child.chd", "parent.chd", out ChdFile? child);
using (child)
{
    byte[] hunk = new byte[child.HunkBytes];
    child.ReadHunk(0, hunk); // transparently resolves parent hunks
}
```

### Header-only validation (fast)

```csharp
using Stream s = File.OpenRead("game.chd");
bool valid = Chd.CheckHeader(s, out uint length, out uint version);
// valid=true, version=5 for a V5 CHD
```

---

## API Reference

### `Chd` — Static verification class

| Member | Signature | Description |
|--------|-----------|-------------|
| **CheckHeader** | `bool CheckHeader(Stream, out uint length, out uint version)` | Sniff the CHD magic `MComprHD`. Returns header length + version. The stream must be seeked to position 0. |
| **CheckFile** | `ChdError CheckFile(Stream s, string filename, bool deepCheck, out uint? ver, out byte[]? sha1, out byte[]? md5)` | Full verification. When `deepCheck=true`: decompresses every hunk in parallel (8 threads), validates per-hunk CRC, computes and compares raw SHA1, MD5, and metadata SHA1. Does **not** handle parent/child CHDs — use `CheckFileWithParent` for those. |
| **CheckFileWithParent** | `ChdError CheckFileWithParent(string child, string parent, out uint? ver, out byte[]? sha1, out byte[]? md5)` | Fully verifies a (possibly child) CHD via sequential `ChdFile` read. Pass `parent=null` for standalone. Single-threaded — use `CheckFile` for fast standalone verification. |
| **DecompressDataParallel** | `internal` | Internal parallel decompression engine (8 threads default). |
| **TaskCount** | `int` (property) | Number of parallel workers for `CheckFile` (default: 8). |

#### `CheckFile` vs `CheckFileWithParent` — when to use each

| Scenario | Use |
|----------|-----|
| Standalone CHD, want speed | `CheckFile` (parallel, 8 threads) |
| Don't know if it's standalone or child | `CheckFileWithParent` (handles both) |
| Child CHD with known parent | `CheckFileWithParent` (required) |
| Header-only check | `CheckHeader` |

### `ChdFile` — Random-access reader (not thread-safe)

All `Open` overloads seek the stream/seek the file from the start. The reader is **not thread-safe** — serialize all calls from the caller.

#### Static factory methods

| Overload | Description |
|----------|-------------|
| `Open(string path, out ChdFile? chd)` | Standalone CHD from disk. Fails with `Chderrrequiresparent` for child CHDs. |
| `Open(string path, string parentPath, out ChdFile? chd)` | Child CHD from disk. Parent is opened internally and disposed with the child. |
| `Open(string path, ChdFile? parent, out ChdFile? chd)` | Child CHD with an existing parent instance. Caller retains ownership of `parent`. Pass `null` for standalone. |
| `Open(Stream s, bool leaveOpen, out ChdFile? chd)` | Standalone CHD from a seekable stream. |
| `Open(Stream s, bool leaveOpen, ChdFile? parent, out ChdFile? chd)` | Child CHD from a seekable stream with parent. |

#### Instance methods

| Method | Signature | Description |
|--------|-----------|-------------|
| **ReadHunk** | `ChdError ReadHunk(uint hunknum, byte[] buffer)` | Decompress a single hunk. `buffer` must be >= `HunkBytes`. Does not cache — each call re-decompresses. |
| **Read** | `ChdError Read(ulong offset, byte[] dest, int destOff, int count)` | Read arbitrary range. Caches the last hunk for sequential reads within bounds. Crosses hunk boundaries automatically. |
| **Dispose** | `void Dispose()` | Releases the underlying stream and any internally-owned parent. |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Version` | `uint` | CHD format version (1–5). |
| `TotalBytes` | `ulong` | Decompressed image size in bytes. |
| `HunkBytes` | `uint` | Size of one hunk in bytes (typically 512 or 4096 for CD, 65536 for DVD/HD). |
| `HunkCount` | `uint` | Total number of hunks. |
| `Sha1` | `byte[]?` | Combined SHA1 (image + metadata). `null` for V1/V2. |
| `RawSha1` | `byte[]?` | Raw image data SHA1 only. `null` for V1/V2. |
| `Md5` | `byte[]?` | Raw image MD5. `null` for V4/V5. |
| `RequiresParent` | `bool` | `true` if this is a differential child CHD. |

### `ChdError` — Error codes

Returned by every API method. Always check for `ChderrNone` after each call.

| Code | Value | Meaning |
|------|-------|---------|
| `ChderrNone` | 0 | Success. |
| `ChderrFileNotFound` | 6 | File does not exist — check the path. |
| `ChderrCannotOpenFile` | 28 | File exists but cannot be opened (permissions, locked). |
| `ChderrInvalidFile` | 3 | Not a valid CHD — missing or corrupted `MComprHD` magic. |
| `ChderrRequiresParent` | 7 | The CHD is a child/differential — use an `Open` overload that accepts a parent, or `CheckFileWithParent`. |
| `ChderrInvalidParent` | 12 | Parent SHA1 mismatch — wrong parent file provided. |
| `ChderrHunkOutOfRange` | 13 | `hunknum >= HunkCount`. |
| `ChderrDecompressionError` | 14 | CRC mismatch or codec failure — the file is likely corrupt. |
| `ChderrCompressionError` | 15 | Internal compression failure (shouldn't occur in read-only usage). |
| `ChderrUnsupportedVersion` | 21 | CHD version > 5 — not supported. |
| `ChderrUnsupportedFormat` | 27 | Unknown or unsupported codec in the CHD header. |
| `ChderrVerifyIncomplete` | 22 | Verification started but did not complete (partial read). |
| `ChderrInvalidData` | 5 | Corrupt or malformed data in the CHD stream. |
| `ChderrCodecError` | 11 | Internal codec failure during decode. |
| `ChderrInvalidState` | 24 | Reader is in an invalid state for the requested operation. |
| `ChderrOutOfMemory` | 2 | Out of memory during decompression. |
| `ChderrInvalidParameter` | 4 | A parameter was `null` or otherwise invalid. |
| `ChderrInvalidMetadata` | 23 | Metadata block is corrupt or unparseable. |
| `ChderrMetadataNotFound` | 19 | Requested metadata entry does not exist. |
| `ChderrInvalidMetadataSize` | 20 | Metadata size exceeds allowed limits. |
| `ChderrNoInterface` | 1 | Operation not available (internal). |
| `ChderrReadError` | 9 | I/O read error. |
| `ChderrWriteError` | 10 | I/O write error. |
| `ChderrFileNotWriteable` | 8 | File is read-only. |
| `ChderrCantCreateFile` | 16 | Cannot create output file. |
| `ChderrCantVerify` | 17 | Verification impossible (missing hash data). |
| `ChderrNotSupported` | 18 | Feature not implemented. |
| `ChderrOperationPending` | 25 | Internal — async already in progress. |
| `ChderrNoAsyncOperation` | 26 | Internal — no async operation to wait on. |

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

### Pattern 1: Fast batch verification (standalone CHDs only)

```csharp
string[] files = Directory.GetFiles(@"D:\CHD", "*.chd");
foreach (var path in files)
{
    using Stream s = File.OpenRead(path);
    var err = Chd.CheckFile(s, Path.GetFileName(path), deepCheck: true,
        out _, out _, out _);
    Console.WriteLine($"{Path.GetFileName(path)}: {err}");
}
```

### Pattern 2: Universal verification (standalone or child)

```csharp
// Verifies any CHD — standalone or child — by first trying
// the fast parallel path, falling back to the sequential parent path.
static ChdError UniversalVerify(string path)
{
    using Stream s = File.OpenRead(path);
    var err = Chd.CheckFile(s, Path.GetFileName(path), deepCheck: true,
        out _, out _, out _);
    if (err == ChdError.ChderrRequiresParent)
    {
        // The CHD is a child — you need to provide the parent
        Console.WriteLine("  -> requires parent CHD; skipping");
    }
    return err;
}
```

### Pattern 3: Streaming random access

```csharp
ChdFile.Open("game.chd", out ChdFile? chd);
using (chd)
{
    // Sequential sweep of the full image
    byte[] buf = new byte[chd.HunkBytes];
    ulong offset = 0;
    while (offset < chd.TotalBytes)
    {
        int chunk = (int)Math.Min((ulong)buf.Length, chd.TotalBytes - offset);
        chd.Read(offset, buf, 0, chunk);
        offset += (ulong)chunk;
        // Process buf[0..chunk-1] here...
    }
}
```

### Pattern 4: Computing SHA1 while reading

```csharp
using var sha1 = System.Security.Cryptography.SHA1.Create();
ChdFile.Open("game.chd", out ChdFile? chd);
using (chd)
{
    byte[] buf = new byte[chd.HunkBytes];
    ulong remaining = chd.TotalBytes;
    ulong offset = 0;
    while (remaining > 0)
    {
        int chunk = (int)Math.Min((ulong)buf.Length, remaining);
        chd.Read(offset, buf, 0, chunk);
        sha1.TransformBlock(buf, 0, chunk, null, 0);
        offset += (ulong)chunk;
        remaining -= (ulong)chunk;
    }
    sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    string computed = Convert.ToHexString(sha1.Hash!).ToLower();
    string header = Convert.ToHexString(chd.RawSha1!).ToLower();
    Console.WriteLine($"Match: {computed == header}");
}
```

### Pattern 5: Working with child (differential) CHDs

```csharp
// Option A: Let the library manage the parent lifetime
ChdFile.Open("child.chd", "parent.chd", out ChdFile? child1);
child1?.Dispose(); // also disposes the internally-opened parent

// Option B: Manage parent lifetime yourself (share parent across children)
ChdFile.Open("parent.chd", out ChdFile? parent);
using (parent)
{
    foreach (var childPath in new[] { "child1.chd", "child2.chd" })
    {
        ChdFile.Open(childPath, parent, out ChdFile? child);
        using (child)
        {
            byte[] hunk = new byte[child.HunkBytes];
            child.ReadHunk(0, hunk);
        }
    }
}
```

### Pattern 6: Child detection and fallback

```csharp
ChdFile.Open("file.chd", out ChdFile? chdOut, out ChdError err);

if (err == ChdError.ChderrRequiresParent)
{
    // The file is a child — user must provide the parent path
    string parentPath = PromptUserForParent();
    err = ChdFile.Open("file.chd", parentPath, out chdOut);
}

if (err != ChdError.ChderrNone)
{
    Console.WriteLine($"Error: {err}");
    return;
}

using (chdOut)
{
    // Safe to use regardless of standalone/child
}
```

> **Note:** `ChdFile.Open(string, out ChdFile?)` has only one `out` parameter. The pattern above uses the return value. The actual API is:
> ```csharp
> ChdError err = ChdFile.Open(path, out ChdFile? chd);
> ```

---

## Performance

| Scenario | Throughput | Notes |
|----------|------------|-------|
| `CheckFile(deepCheck: true)` | ~200–400 MB/s | 8 parallel threads, bounded memory via `SemaphoreSlim` |
| `CheckFile(deepCheck: false)` | > 1 GB/s | Header-only validation |
| `ChdFile.Read()` sequential | ~150–300 MB/s | Single-threaded, hunk-cached |
| `ChdFile.ReadHunk()` random | ~50–150 MB/s | Per-hunk re-decompression |

Performance varies by codec (Zlib/LZMA are slower; Zstd/Huffman/FLAC are faster) and hunk size (CD-sized hunks have more overhead than DVD-sized hunks).

### Tuning parallelism

```csharp
Chd.TaskCount = 16; // adjust before calling CheckFile
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

### Parallel Decompression Pipeline

`CheckFile(deepCheck: true)` uses a 3-stage producer/consumer pipeline:

```
Producer (1 thread)       Decompressors (N threads)       Hasher (1 thread)
┌──────────────┐         ┌─────────────────────────┐    ┌──────────────────┐
│ Read blocks  │──→ BQ ──→│ Decompress + CRC check  │──→ BQ─→│ Reorder + MD5/  │
│ from file    │         │ (per-codec delegates)   │    │ SHA1 hash        │
└──────────────┘         └─────────────────────────┘    └──────────────────┘
                                  │                               │
                                  └── SemaphoreSlim → throttle ──┘
```

- **N** = `Chd.TaskCount` (default: 8)
- BQ = `BlockingCollection<int>` (bounded, backpressure)
- Memory throttled to `~512MB` outstanding decompressed buffers
- Output ordering guaranteed via reorder queue

---

## Building

```bash
dotnet build CHDSharpLib/CHDSharpLib.csproj -c Release
dotnet pack CHDSharpLib/CHDSharpLib.csproj -c Release
```

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port/) | 0.8.8 | Pure C# Zstd decompression (`zstd`/`cdzs` codecs) |
| [Serilog](https://www.nuget.org/packages/Serilog/) | 4.4.0 | Structured logging |

---

## Limits

- **Read-only** — Cannot create, modify, or repack CHD files
- **Not thread-safe** — `ChdFile` instances must be used from a single thread
- **No lossy video** — Lossy AVHuff video variants are not supported
- **Stream must be seekable** — for `ChdFile.Open` stream overloads
- **V6+ not supported** — MAME has not released a V6 format

---

## License

GNU General Public License v3.0 — see [LICENSE](../LICENSE).

---

## Acknowledgments

- **[Gordon Jefferyes](https://github.com/gjefferyes)** — original C# CHDSharp implementation
- **[MAME](https://www.mamedev.org/)** — CHD format specification and `chdman` reference
- **[libchdr](https://github.com/rtissera/libchdr)** — C reference library by Romain Tisseraud
- **[ZstdSharp](https://github.com/oleg-st/ZstdSharp)** — pure C# Zstd decompressor
