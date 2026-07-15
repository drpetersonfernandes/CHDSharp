[![.NET](https://img.shields.io/badge/.NET-8.0_|_9.0_|_10.0-blueviolet)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/CHDSharp?color=blue)](https://www.nuget.org/packages/CHDSharp/)
[![License](https://img.shields.io/badge/license-GPL--3.0-green)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-38%20passed-brightgreen)](#testing)

# CHDSharp

**Pure C# CHD (Compressed Hunks of Data) reader — V1–V5, all codecs, parent/child chaining, 100% match with MAME chdman.**

> A fork of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp) by [Gordon Jefferyes (gjefferyes)](https://github.com/gjefferyes), extended with Zstd, AVHuff, V5 compressed map, random-access API, parent/child chaining, parallel verification, and comprehensive chdman integration tests.


---

## Overview

CHDSharp is a **read-only** CHD library and CLI tool written entirely in C#. It decompresses and verifies CHD files produced by MAME's `chdman` tool, byte-for-byte, matching chdman's output exactly.

It supports every CHD format version (V1 through V5), every compression codec (zlib, lzma, huffman, flac, zstd, AVHuff, and all CD variants), and parent/child (differential) CHD chains.

---

## Features

- **Read any CHD** — V1, V2, V3, V4, V5 headers and all internal map formats
- **All 10 codecs** — zlib, lzma, huffman, flac, zstd, AVHuff + CD variants (`cdzl`, `cdlz`, `cdfl`, `cdzs`)
- **Random-access API** — `ReadHunk(hunknum)` and `Read(offset, length)` with zero-copy caching
- **Async API** — `OpenAsync`, `ReadHunkAsync`, `ReadAsync`, `IAsyncDisposable`
- **Metadata reading** — expose game name, disc labels, and other CHD header metadata
- **Full verification** — deep decompress + per-hunk CRC + raw SHA1/MD5 + metadata SHA1
- **Parent/child chaining** — differential CHDs referencing a parent, with wrong-parent detection
- **Parallel decompression** — multi-threaded `CheckFile` with bounded memory via `SemaphoreSlim`, configurable thread count
- **100% chdman match** — integration-tested against `chdman info`, `chdman verify`, and `chdman extractraw`
- **Pluggable logging** — `Microsoft.Extensions.Logging` integration; silent by default, hook any provider
- **CLI tool** — verify directories, file lists, random-access self-test, parent/child validation

---

## Supported CHD Formats

| Version | Header Size | Map Type | Status |
|---------|-------------|----------|--------|
| V1 | 76 bytes | Self-hunk dedup | ✅ |
| V2 | 80 bytes | Self-hunk dedup | ✅ |
| V3 | 120 bytes | CRC32 map, self-hunk | ✅ |
| V4 | 108 bytes | CRC32 map, parent chain | ✅ |
| V5 | 124 bytes | CRC16 map, compressed/uncompressed map, RLE, parent/unit chain | ✅ |

### Codec Support Matrix

| Codec | FourCC | CD Equivalent | Status |
|-------|--------|---------------|--------|
| Zlib | `zlib` | `cdzl` | ✅ |
| LZMA | `lzma` | `cdlz` | ✅ |
| Huffman | `huff` | — | ✅ |
| FLAC | `flac` | `cdfl` | ✅ |
| Zstd | `zstd` | `cdzs` | ✅ |
| AVHuff | `avhu` | — | ✅ |

---

## Quick Start

### NuGet Package

```bash
dotnet add package CHDSharp
```

### CLI

```bash
# Verify all .chd files in a directory
CHDSharpCli D:\CHD

# Verify a list of CHD paths from a text file
CHDSharpCli --list chd_paths.txt

# Random-access self-test on a single CHD
CHDSharpCli --random game.chd

# Verify a child (differential) CHD against its parent
CHDSharpCli --parent child.chd parent.chd
```

### Library

```csharp
using CHDSharp;
using CHDSharp.Models;

// Quick check: is this a valid CHD file?
if (Chd.IsChdFile("game.chd", out uint version))
    Console.WriteLine($"Detected V{version}");

// Full verification — returns a result object
using Stream s = File.OpenRead("game.chd");
var result = Chd.CheckFile(s, "game.chd", deepCheck: true);
if (result.IsSuccess)
    Console.WriteLine($"V{result.Version} — SHA1: {result.Sha1Hex}");
else
    Console.WriteLine($"Error: {result.Error.GetMessage()}");

// Verify child (differential) CHD
var childResult = Chd.CheckFileWithParent("child.chd", "parent.chd");

// Random access (open once, read hunks or byte ranges on demand)
var err = ChdFile.Open("game.chd", out var chd);
using (chd)
{
    // Inspect metadata (game name, disc label, etc.)
    foreach (var meta in chd.Metadata)
        Console.WriteLine(meta.ToString());

    // Read a single decompressed hunk
    byte[] hunk = new byte[chd.HunkBytes];
    chd.ReadHunk(42, hunk);

    // Read arbitrary byte range (handles hunk boundaries automatically)
    byte[] buf = new byte[1024];
    chd.Read(offset: 0x10000, buf, 0, buf.Length);
}

// Parent/child chain (differential CHDs)
ChdFile.Open("child.chd", "parent.chd", out var child);
using (child)
{
    child.ReadHunk(0, hunk);  // transparently resolves parent hunks
}

// Async random access
var (asyncErr, asyncChd) = await ChdFile.OpenAsync("game.chd");
await using (asyncChd)
{
    await asyncChd.ReadHunkAsync(0, hunk);
}

// Decompress the full image at once
chd.ReadAllBytes(out byte[] image);

// Iterate hunks one at a time
foreach (var h in chd.EnumerateHunks())
{
    // process each hunk; buffer is reused — copy if needed
}
```

---

## API Reference

### `Chd` — Static verification API

| Method | Description |
|--------|-------------|
| `IsChdFile(string path)` | Quick check: does this file path point to a valid CHD? |
| `IsChdFile(string path, out uint version)` | Same, also returns the version. |
| `CheckHeader(Stream, out uint length, out uint version)` | Sniff the CHD magic `MComprHD` and return header length + version. |
| `CheckFile(Stream, string name, bool deep)` | Full deep verification. Returns `ChdResult`. `deep=true` decompresses every hunk. |
| `CheckFileWithParent(string child, string parent)` | Full verification of a child CHD with its parent. Returns `ChdResult`. Pass `parent=null` for standalone. |

| Field / Property | Type | Description |
|------------------|------|-------------|
| `LoggerFactory` | `ILoggerFactory?` (static) | Set to enable internal logging via any MEL-compatible provider. |
| `TaskCount` | `int` (static) | Number of parallel workers for `CheckFile`. Default 8. Change before calling. |

### `ChdResult` — Verification result

| Property | Type | Description |
|----------|------|-------------|
| `Error` | `ChdError` | Error code (Chderrnone on success). |
| `Version` | `uint?` | CHD version (1–5). |
| `Sha1` | `byte[]?` | SHA1 hash from header. |
| `Md5` | `byte[]?` | MD5 hash from header. |
| `IsSuccess` | `bool` | `true` if Error == Chderrnone. |
| `Sha1Hex` | `string` | SHA1 as lowercase hex, or "(none)". |
| `Md5Hex` | `string` | MD5 as lowercase hex, or "(none)". |

Supports deconstruction: `var (err, ver, sha1, md5) = result;`

### `ChdFile` — Random-access reader

| Method | Description |
|--------|-------------|
| `Open(string path, out ChdFile chd)` | Open standalone CHD. Fails with `ChdError.Chderrrequiresparent` if child. |
| `Open(string path, string parentPath, out ChdFile chd)` | Open child CHD with parent (parent owned by the child). |
| `Open(string path, ChdFile parent, out ChdFile chd)` | Open child CHD with an external parent. |
| `Open(Stream, bool leaveOpen, out ChdFile chd)` | Open from a seekable stream. |
| `OpenAsync(...)` | Async overloads for all `Open` variants. |
| `ReadHunk(uint hunknum, byte[] buffer)` | Decompress a single hunk into a pre-allocated buffer. |
| `Read(ulong offset, byte[] dest, int destOff, int count)` | Read arbitrary byte range. Crosses hunk boundaries. |
| `ReadHunkAsync(uint, byte[])` | Async hunk read. |
| `ReadAsync(ulong, byte[], int, int)` | Async byte range read. |
| `ReadAllBytes(out byte[])` | Decompress the entire image into a single `byte[]`. |
| `EnumerateHunks()` | Yield each decompressed hunk. Buffer reused — copy if needed. |
| `Dispose()` / `DisposeAsync()` | Release the underlying stream and any internally-owned parent. |

| Property | Type | Description |
|----------|------|-------------|
| `Version` | `uint` | CHD format version (1–5). |
| `TotalBytes` | `ulong` | Decompressed image size. |
| `HunkBytes` | `uint` | Size of one hunk in bytes. |
| `HunkCount` | `uint` | Total number of hunks. |
| `Sha1` | `byte[]` | Combined SHA1 (image + metadata). |
| `RawSha1` | `byte[]` | Raw image data SHA1 only. |
| `Md5` | `byte[]` | Raw image MD5 (V1–V3 only). |
| `RequiresParent` | `bool` | True if this is a differential CHD needing a parent. |
| `IsChild` | `bool` | Alias for `RequiresParent`. |
| `Metadata` | `IReadOnlyList<ChdMetadataEntry>` | CHD metadata entries (game name, disc type, etc.). Lazy-loaded. |

### `ChdMetadataEntry` — Metadata record

| Property | Type | Description |
|----------|------|-------------|
| `Tag` | `string` | 4-char tag (e.g. "GAME", "DISC", "HARD"). |
| `Data` | `byte[]` | Raw metadata bytes. |
| `IsText` | `bool` | True if data is printable ASCII. |
| `GetText()` | `string` | ASCII text representation. |
| `ToString()` | `string` | Human-readable: `GAME: gauntlet`. |

### `ChdError` — Error codes

| Value | Meaning |
|-------|---------|
| `Chderrnone` | Success |
| `Chderrfilenotfound` | File does not exist |
| `Chderrinvalidfile` | Not a valid CHD |
| `Chderrrequiresparent` | Child CHD opened without a parent |
| `Chderrinvalidparent` | Wrong parent SHA1 |
| `Chderrhunkoutofrange` | Hunk index >= `HunkCount` |
| `Chderrdecompressionerror` | CRC mismatch or codec failure |
| `Chderrunsupportedversion` | Unsupported CHD version |
| ... | (21 additional error codes) |

Use `ChdErrorExtensions.GetMessage()` (e.g. `err.GetMessage()`) for human-readable descriptions.

---

## Logging

By default, the library is silent. To enable logging, set `Chd.LoggerFactory` before any other call:

```csharp
using Serilog;
using Serilog.Extensions.Logging;

Chd.LoggerFactory = new SerilogLoggerFactory(
    new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger());
```

Any `ILoggerFactory`-compatible provider works (NLog, `Microsoft.Extensions.Logging.Console`, etc.).

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
│  LzmaStream · LzmaDecoder · LzmaEncoder ·          │
│  RangeCoder · LzBinTree · LzInWindow · LzOutWindow  │
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
Producer (1 thread)          Decompressors (taskCount threads)       Hasher (1 thread)
┌──────────────┐            ┌─────────────────────────────┐        ┌──────────────────┐
│ Read blocks  │──→ BQ ──→ │ Decompress + CRC validate    │──→ BQ ──→│ Reorder + MD5/   │
│ from file    │            │ (per-codec delegates)        │        │ SHA1 hash        │
└──────────────┘            └─────────────────────────────┘        └──────────────────┘
                                     │                                        │
                                     └── SemaphoreSlim ──→ memory throttle ───┘
```

- **BQ** = `BlockingCollection<int>` (bounded, backpressure)
- **`taskCount`** configurable via `Chd.TaskCount` (default 8)
- Memory bounded by `~512MB / hunkSize` outstanding decompressed buffers

---

## Building from Source

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows, Linux, or macOS (any platform supporting .NET)

### Project Structure

| Project | Type | Target | Description |
|---------|------|--------|-------------|
| `CHDSharpLib` | Class Library | net8.0 / net9.0 / net10.0 | Core library (NuGet package) |
| `CHDSharpCli` | Console App | net10.0 | CLI tool |
| `CHDSharpTest` | xUnit Tests | net8.0 / net9.0 / net10.0 | Test suite |

### Build

```bash
git clone https://github.com/drpetersonfernandes/CHDSharp.git
cd CHDSharp
dotnet build -c Release
```

The CLI binary is at `CHDSharpCli/bin/Release/net10.0/CHDSharpCli.exe` (Windows) or `CHDSharpCli.dll` (cross-platform via `dotnet CHDSharpCli.dll`).

Build the NuGet package:

```bash
dotnet pack -c Release CHDSharpLib/
```

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port/) | 0.8.8 | Zstd decompression (V5 zstd/cdzs codecs) |
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/) | 8.0.0 | Pluggable logging (optional) |

All other codecs (zlib, lzma, huffman, flac, AVHuff) are implemented from scratch in C# with zero native dependencies.

---

## Testing

The test suite contains **38 tests** across 5 test classes, verified against MAME `chdman` 0.288.

### Test Categories

| Class | Type | Description |
|-------|------|-------------|
| `HeaderAndApiTests` | Unit | Header sniffing, magic validation, error paths |
| `ChecksumTests` | Unit | CRC-32 / CRC-16 test vectors |
| `RandomAccessTests` | Integration | ReadHunk/Read bounds, determinism, cross-hunk reads |
| `ZstdCodecTests` | Integration | Zstd/CD-Zstd round-trip via chdman recompression |
| `ParentChainTests` | Integration | Parent/child CHD chain validation |
| `ChdListIntegrationTests` | Integration | **Cross-checked against chdman**: header info, deep verify, full-read SHA1, random-access byte-range extraction |

### Cross-Check Methodology

Every integration test that touches decoded data is verified against `chdman`:

```
Header info     →  chdman info -i file        (version, sizes, SHA1, Data SHA1)
Deep verify     →  chdman verify -i file       (exit code match)
Random access   →  chdman extractraw -isb -ib   (byte-for-byte comparison)
Zstd round-trip →  chdman copy -c zstd/cdzs    (recompress + verify)
Parent chain    →  chdman verify -i child -ip parent
```

Place `chdman.exe` at the repo root, put your CHD files in `D:\CHD` (or configure `TestPaths.ChdFolder`), and run:

```bash
dotnet test
```

---

## Comparison with libchdr

| Feature | libchdr 0.3.0 (C) | CHDSharp (C#) |
|---------|-------------------|---------------|
| V1–V5 headers | ✅ | ✅ |
| zlib, lzma, huffman, flac | ✅ | ✅ |
| Zstd (zstd, cdzs) | ❌ | ✅ |
| AVHuff | ❌ | ✅ |
| Parent/child chains | ✅ | ✅ |
| Random access | ✅ | ✅ |
| Async API | ❌ | ✅ |
| Metadata reading | ❌ | ✅ |
| Parallel verification | ❌ | ✅ |
| Pluggable logging | ❌ | ✅ |
| Native dependencies | zlib, lzma, flac | **none** (pure C# + ZstdSharp) |
| CHD creation | ❌ | ❌ |

CHDSharp exceeds libchdr 0.3.0's read capabilities by adding **Zstd** and **AVHuff** codec support, plus async APIs, metadata access, and parallel verification.

---

## License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.

GPL-3.0 is a strong copyleft license. If you distribute modified versions of this software, you must make your source code available under the same license.

---

## Acknowledgments

This project is a fork of **[RomVault/CHDSharp](https://github.com/RomVault/CHDSharp)**.

Special thanks to **[Gordon Jefferyes (gjefferyes)](https://github.com/gjefferyes)** who built the original C# CHD reader foundation that this project extends.

- **[MAME](https://www.mamedev.org/)** — original CHD format specification and `chdman` reference implementation
- **[libchdr](https://github.com/rtissera/libchdr)** — C reference library by Romain Tisseraud (v0.3.0)
- **[ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp)** — pure C# Zstd decompressor by Oleg Stepanischev
- **[CHDSharpReference](https://github.com/RomVault/CHDSharp)** — educational per-version reference implementation bundled in `References/`

---

## Support

* **Donate:** If you find this project useful, consider [supporting the developer](https://www.purelogiccode.com/donate).

**⭐ If you like this project, please give us a star on GitHub! ⭐**

---
