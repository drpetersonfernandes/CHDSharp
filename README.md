[![.NET](https://img.shields.io/badge/.NET-8.0_|_9.0_|_10.0-blueviolet)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/CHDSharp?color=blue)](https://www.nuget.org/packages/CHDSharp/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-xUnit-brightgreen)](#tests)

# CHDSharp

**Pure C# CHD (Compressed Hunks of Data) reader — V1–V5, all 10 codecs, parent/child chaining, 100% byte-for-byte match with MAME `chdman`.**

> Fork of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp) by [Gordon Jefferyes](https://github.com/gjefferyes) — extended with Zstd, AVHuff, parallel verification, async APIs, metadata support, and a comprehensive test suite.

---

## What's New in v1.2.0

- **CD/GD-ROM track (TOC) parsing** — Full track layout, sector types, pregap/postgap, GD-ROM support (incl. legacy `CHGT` little-endian CDDA) via `Tracks` / `IsLittleEndianAudio` / `GenerateCueSheet()` / `ExportToc()`
- **`UnitBytes` property** — Derives sector size from metadata for all CHD versions (HDD 512B, CD 2448B, V5 header)
- **New enums** — `ChdTrackType` (Mode1, Mode2, Audio, etc.) and `ChdSubType` (None, Normal, Raw)
- **Centralized versioning** — All 7 projects share version `1.2.0` via `Directory.Build.props`
- **Deterministic builds** — Reproducible byte-for-byte builds with embedded SourceLink
- **Embedded debug symbols** — Easier NuGet debugging with `<DebugType>embedded</DebugType>`
- **Companion library** — `CHDSharpEncoder` with CRC16, SHA1, and Deflate support
- **Code refactoring** — Consistent code style across the entire codebase
- **Metadata upgrade during copy** — Legacy CD/GD-ROM metadata (`CHCD`, `CHTR`, `CHGT`) is automatically upgraded to modern equivalents (`CHT2`, `CHGD`) during `Copy`, matching MAME chdman behavior. Use `--no-upgrade` to preserve legacy tags.

---

## Installation

```bash
dotnet add package CHDSharp
```

Targets `net8.0`, `net9.0`, and `net10.0`. Zero native dependencies — every codec (zlib, lzma, huffman, flac, zstd, AVHuff) is implemented in pure C#, with Zstd backed by the managed [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp).

---

## Quick Start

```csharp
using CHDSharp;
using CHDSharp.Models;

// Quick check: is this a valid CHD?
if (Chd.IsChdFile("game.chd", out uint version))
    Console.WriteLine($"Detected V{version}");

// Full verification (parallel, deep decompress every hunk)
using var stream = File.OpenRead("game.chd");
var result = Chd.CheckFile(stream, "game.chd", deepCheck: true);
Console.WriteLine(result.IsSuccess
    ? $"V{result.Version}  SHA1: {result.Sha1Hex}"
    : $"Error: {result.Error.GetMessage()}");

// Random access — open once, read hunks or byte ranges on demand
var err = ChdFile.Open("game.chd", out var chd);
using (chd)
{
    // Inspect metadata (game name, disc label, etc.)
    foreach (var meta in chd.Metadata)
        Console.WriteLine(meta);  // e.g. "GAME: gauntlet"

    // Parse CD/GD-ROM track layout (TOC)
    if (chd.Tracks is { } tracks)
        foreach (var track in tracks)
            Console.WriteLine($"Track {track.TrackNumber}: {track.GetTypeString()}");

    // Read hunk #42
    var hunk = new byte[chd.HunkBytes];
    chd.ReadHunk(42, hunk);

    // Read arbitrary byte range (crosses hunk boundaries)
    var buf = new byte[1024];
    chd.Read(offset: 0x10000, buf, 0, buf.Length);

    // Or decompress the entire image at once
    chd.ReadAllBytes(out var image);
}

// Child (differential) CHD with its parent
var childResult = Chd.CheckFileWithParent("child.chd", "parent.chd");

// Async API
var (_, asyncChd) = await ChdFile.OpenAsync("game.chd");
await using (asyncChd)
{
    await asyncChd.ReadHunkAsync(0, hunk);
}
```

### CLI

```bash
# Verify all .chd files in directories (recursive)
CHDSharpCli D:\CHD

# Verify paths from a text file
CHDSharpCli --list chd_paths.txt

# Random-access self-test on a single CHD
CHDSharpCli --random game.chd

# Verify a child CHD against its parent
CHDSharpCli --parent child.chd parent.chd

# Print CD/GD-ROM table of contents
CHDSharpCli --toc game.chd

# Generate CUE sheet for CD CHDs
CHDSharpCli --cue game.chd

# Classify CHD media type
CHDSharpCli --classify game.chd

# Create CHDs (raw, CD, or re-compress an existing CHD; -c none for uncompressed)
CHDSharpCli --create in.bin out.chd [-c zlib,zstd,lzma,none] [-hs 65536] [-us 4096] [-t 8] [-ip parent.chd] [-d] [-tp id] [-v]
CHDSharpCli --createcd in.cue out.chd [-c zlib,zstd,lzma,none] [-t 8] [-ip parent.chd] [-v]
CHDSharpCli --createhd out.chd --size N [-c zlib,zstd,lzma,none] [-hs N] [-us N] [-chs C,H,S] [-ss N] [-t 8] [-v]
CHDSharpCli --copy in.chd out.chd [-c zlib,zstd,lzma,none] [-t 8] [-ip parent.chd] [-op parent.chd] [--no-upgrade] [-v]
```

---

## Features

- **Read any CHD** — V1–V5 headers, all internal map formats (self-dedup, CRC16/32, compressed/uncompressed, RLE)
- **All 10 codecs** — zlib, lzma, huffman, flac, zstd, AVHuff + CD variants (`cdzl`, `cdlz`, `cdfl`, `cdzs`)
- **Random-access API** — `ReadHunk()` and `Read()` with hunk-caching; `EnumerateHunks()` for sequential streaming
- **LBA/MSF sector reads** — `ReadSector()`, `ReadSectorMsf()`, and `ReadFrame()` read CD/GD-ROM sectors or full 2448-byte frames by logical block address; `CdRomAddress` converts between BCD MSF and LBA (with or without the 150-frame lead-in)
- **Async API** — `OpenAsync`, `ReadHunkAsync`, `ReadAsync`, `IAsyncDisposable`
- **Parallel verification** — multi-threaded `CheckFile` with bounded memory, configurable thread count
- **Parent/child chaining** — transparent differential CHD support with wrong-parent detection
- **Track info** — parse CD/GD-ROM table of contents (track types, sector sizes, pregap/postgap, frame offsets)
- **Metadata** — expose game name, disc labels, and other CHD header metadata
- **CHD creation** — companion [`CHDSharpEncoder`](CHDSharpEncoder/README.md) writes V5 CHDs from raw binaries, CD images (CUE/GDI/ISO/TOC/NRG), and AVI laserdisc sources (`createld`) with `chdman`-matched output; re-compresses existing CHDs (`Copy`), creates delta children (`-ip parent`), and writes uncompressed CHDs (`-c none`)
- **100% chdman match** — cross-checked against `chdman info`, `verify`, `copy`, and `extractraw` (MAME 0.288)
- **Pluggable logging** — `Microsoft.Extensions.Logging` integration; silent by default

---

## Support Matrix

| Version | Header | Map | Status |
|---------|--------|-----|--------|
| V1 | 76 bytes | Self-hunk dedup | ✅ |
| V2 | 80 bytes | Self-hunk dedup | ✅ |
| V3 | 120 bytes | CRC32 map, self-hunk | ✅ |
| V4 | 108 bytes | CRC32 map, parent chain | ✅ |
| V5 | 124 bytes | CRC16 / compressed / RLE, parent/unit chain | ✅ |

| Codec | FourCC | CD Variant | |
|-------|--------|------------|--|
| Zlib (Deflate) | `zlib` | `cdzl` | ✅ |
| LZMA | `lzma` | `cdlz` | ✅ |
| Huffman | `huff` | — | ✅ |
| FLAC | `flac` | `cdfl` | ✅ |
| Zstd | `zstd` | `cdzs` | ✅ |
| AVHuff | `avhu` | — | ✅ |

---

## vs libchdr

| Feature | libchdr 0.3.0 (C) | CHDSharp (C#) |
|---------|:---:|:---:|
| V1–V5 headers | ✅ | ✅ |
| 9 of 10 codecs (zlib, lzma, huff, flac, zstd, cdzl, cdlz, cdfl, cdzs) | ✅ | ✅ |
| AVHuff (`avhu`) | ❌ | ✅ |
| Parent/child chains | ✅ | ✅ |
| Random access (`chd_read` / `ReadHunk`) | ✅ | ✅ |
| Byte-range reads | ❌ (hunk-only) | ✅ `Read(offset, ...)` |
| LBA/MSF sector reads | ❌ | ✅ `ReadSector`/`ReadSectorMsf`/`ReadFrame` + `CdRomAddress` |
| Full-image verification | ❌ | ✅ parallel |
| Metadata reading | ✅ `chd_get_metadata` | ✅ `GetMetadata` + `Metadata` |
| Metadata writing | ❌ | ✅ `SetMetadata`/`DeleteMetadata` |
| Extraction (CUE/BIN, GDI, ISO) | ❌ | ✅ |
| Async API | ❌ | ✅ |
| Parallel verification | ❌ | ✅ |
| Pluggable logging | ❌ | ✅ |
| CHD creation | ❌ (commented out) | ✅ `CHDSharpEncoder` |
| Native dependencies | zlib (miniz), LZMA SDK, zstd, dr_flac | **none** |

See [docs/libchdr-comparison.md](docs/libchdr-comparison.md) for the full parity analysis.

---

## Library comparison: CHDSharp vs chd-rs vs CHDlite vs chdman vs libchdr

CHDSharp vs the two other independent CHD implementations (compared against their checked-in sources in [`References/`](References)), MAME's reference `chdman` (0.288), and the C reference library [libchdr 0.3.0](References/libchdr-0.3.0). The CHDSharp column covers the whole repo (reader + `CHDSharpEncoder` + CLI). ✅ = supported, 🟡 = partial, ❌ = not supported, — = not applicable (CLI).

| Capability | CHDSharp (this repo) | [chd-rs](References/chd-rs-master) 0.3.4 (Rust) | [CHDlite](References/CHDlite-main) 0.2.1 (C++) | `chdman` (MAME 0.288) | [libchdr](References/libchdr-0.3.0) 0.3.0 (C) |
|---|:---:|:---:|:---:|:---:|:---:|
| **Reading** | | | | | |
| Read V1–V5 | ✅ | ✅ | 🟡 V3–V5 only (rejects V1/V2) | ✅ | ✅ |
| All 10 codecs (decode) | ✅ | ✅ | ✅ | ✅ (reference) | 🟡 9 of 10 (no AVHuff) |
| Parent/child chains (read) | ✅ | ✅ | ✅ | ✅ | ✅ |
| Per-hunk CRC16/CRC32 verification | ✅ always | 🟡 opt-in feature, off by default | ✅ always | ✅ always | 🟡 V5 CRC16 build option (on by default); V3/V4 CRC32 never checked |
| Full-image verify (rawsha1 + combined SHA1) | ✅ parallel | 🟡 raw SHA1 only | ✅ sequential | ✅ sequential | ❌ no verify function |
| `verify --fix` (repair header hashes) | ✅ | ❌ | ✅ | ✅ | ❌ |
| Track/TOC parsing (CD/GD-ROM) | ✅ `Tracks`/`ChdTrackInfo` | 🟡 tags recognized, no track model | ✅ | ✅ | ❌ |
| Metadata read | ✅ | ✅ | ✅ | ✅ | ✅ `chd_get_metadata` |
| **Writing** | | | | | |
| Write V5 | ✅ (encoder) | ❌ read-only | ✅ | ✅ (reference) | ❌ read-only |
| All 10 codecs (encode) | ✅ 10 of 10 (incl. `avhu` via `createld`/`extractld`) | ❌ | ✅ | ✅ | ❌ |
| Uncompressed CHD (`-c none`) | ✅ byte-exact with chdman | 🟡 decode only | 🟡 core supports, CLI rejects | ✅ | 🟡 decode only |
| Delta/parent CHD creation (`-ip`) | ✅ | ❌ | ✅ | ✅ | ❌ |
| CHD→CHD copy / re-compress | ✅ | ❌ | ✅ | ✅ | ❌ |
| Metadata write (addmeta/delmeta) | ✅ | ❌ | ✅ | ✅ | ❌ |
| **Input formats** | | | | | |
| CUE / GDI / ISO / TOC / NRG parsing | ✅ all five | — | ✅ all five | ✅ all five | ❌ |
| CUE style conversion / Redump match | ✅ | ❌ | ✅ | ✅ `convertcue` | ❌ |
| **API & reads** | | | | | |
| Byte-range reads | ✅ `Read(offset, ...)` | ✅ `Read + Seek` | ✅ `read_bytes` | — | ❌ hunk-only `chd_read` |
| LBA/MSF sector-addressed reads | ✅ `ReadSector`/`ReadSectorMsf`/`ReadFrame` + `CdRomAddress` | ❌ | ✅ `read_sector` + `msf_to_lba`/`lba_to_msf` | — | ❌ |
| Thread-safe random access | ✅ `ReadHunkConcurrent` | ❌ | ❌ | — | ❌ |
| Async I/O API | ✅ | ❌ | 🟡 async *compress* pump only | — | ❌ |
| Cancellation + progress reporting | ✅ on all long-running APIs | ❌ | 🟡 cancel + callbacks via C API | — | ❌ |
| Precache / multi-hunk LRU cache | ✅ both | ❌ | 🟡 single-hunk cache | — | 🟡 `chd_precache`, no hunk cache |
| Header-only DTO read | ✅ `Chd.ReadHeader` (libchdr parity) | 🟡 `Header` struct exposed | 🟡 via `ChdReader` | ✅ `info` | ✅ `chd_read_header` |
| **Performance & tooling** | | | | | |
| Parallel verification | ✅ (default 8 workers) | ❌ | ❌ | ❌ | ❌ |
| Parallel encoding | ✅ (1–64 workers) | ❌ | ✅ (≤ 16, per-codec weighted queues) | ✅ (≤ 16 work-queue threads) | ❌ |
| Benchmarks | ✅ BenchmarkDotNet + chdman comparer | ✅ `benches/` | ✅ `benchmark_chd.cpp` | — | 🟡 `tests/benchmark.c` (minimal timing harness) |
| Fuzzing / mutation testing | ✅ 3500-seed deterministic suite | ✅ cargo-fuzz target | ❌ | ❌ | 🟡 `tests/fuzz.c` (libFuzzer harness) |
| **Extras** | | | | | |
| Extraction (CUE/BIN, GDI, ISO) | ✅ | 🟡 raw dump only | ✅ | ✅ | ❌ |
| Platform/game detection | ✅ 11 systems (CHDlite parity) | ❌ | ✅ 11 systems | ❌ | ❌ |
| Multi-hash output (SHA-256/CRC32/XXH3) | ✅ SHA1/SHA256/CRC32/XXH3 | ❌ | ✅ SHA1/MD5/CRC32/SHA256/XXH3 | 🟡 SHA1/MD5 only | ❌ |
| Batch mode (folder scan) | ✅ | ❌ | ✅ | ❌ | ❌ |
| Native dependencies | **none** (pure C#) | none (pure-Rust crates) | zlib-ng / zstd / lzma / flac | zlib / lzma / flac | zlib (miniz) / LZMA SDK / zstd / dr_flac |
| Language | C# (.NET 8/9/10) | Rust | C++ | C++ (MAME) | C |

See [`docs/libchdr-comparison.md`](docs/libchdr-comparison.md) for the detailed parity analysis against libchdr, and [`References/ProposedFixes.md`](References/ProposedFixes.md) for the feature-by-feature roadmap that closed each gap vs chd-rs and CHDlite.

---

## Logging

By default the library is silent. Set `Chd.LoggerFactory` before any other call to enable logging with any `ILoggerFactory`-compatible provider:

```csharp
using Serilog;
using Serilog.Extensions.Logging;

Chd.LoggerFactory = new SerilogLoggerFactory(
    new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger());
```

---

## Documentation

The full wiki lives in [`docs/`](docs/README.md) — format reference, codecs, API reference, verification, extraction, performance, testing, and troubleshooting. CHD *creation* is documented in [the `CHDSharpEncoder` README](CHDSharpEncoder/README.md) and the wiki's [Encoder page](docs/encoder.md).

---

## Full API

See the [CHDSharpLib README](CHDSharpLib/README.md#api-reference) for the complete `Chd`, `ChdFile`, `ChdResult`, `ChdHeaderInfo`, `ChdMetadataEntry`, and `ChdError` API reference, including all `Open` overloads, the full header DTO read (`Chd.ReadHeader`), performance tuning (`Chd.TaskCount`), and usage patterns.

---

## Tests

The test infrastructure has three tiers:

| Project | Type | Description |
|---------|------|-------------|
| [`CHDSharpTest`](CHDSharpTest/README.md) | xUnit | Unit tests (header/API, CRC checksums) + corpus tests against 29 deterministic CHD files (V1–V5, all codecs & map types) |
| [`CHDSharpTester`](CHDSharpTester/README.md) | WPF | Interactive batch verification cross-checked against `chdman` — header info, deep verify, SHA1, random-access extraction, codec decode, parent/child chains |
| [`CHDSharpTestGen`](CHDSharpTestGen/README.md) | Console | Deterministic corpus generator (builds source images, runs vintage chdman binaries, produces the `CHDSharpTest/TestData/` corpus) |

```bash
# Regenerate corpus (requires chdman binaries in CHDSharpTest/chdman/)
dotnet run --project CHDSharpTestGen

# Run unit + corpus tests
dotnet test

# Interactive chdman cross-check
dotnet run --project CHDSharpTester
```

---

## Building

```bash
git clone https://github.com/purelogiccode/CHDSharp.git
cd CHDSharp
dotnet build -c Release

# NuGet package
dotnet pack -c Release CHDSharpLib/
```

Requires [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later. Works on Windows, Linux, and macOS.

---

## License

MIT License — see [LICENSE](LICENSE.txt).

### Special Thanks

**Gordon Jefferyes ([@gjefferyes](https://github.com/gjefferyes))** — the original author of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp), which this project is forked from. Gordon built the foundational C# CHD reader (V1–V5 headers, zlib/lzma/huffman/flac codecs, and a custom LZMA/FLAC stack) that this project extends with Zstd, AVHuff, parallel verification, async APIs, metadata support, and comprehensive testing.

### Acknowledgments

- **[MAME](https://www.mamedev.org/)** — CHD format specification and `chdman` reference implementation
- **[libchdr](https://github.com/rtissera/libchdr)** — C reference library by Romain Tisseraud
- **[ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp)** — pure C# Zstd decompressor by Oleg Stepanischev

---

* **Donate:** [support the developer](https://www.purelogiccode.com/donate)
* **⭐ Star this repo on [GitHub](https://github.com/purelogiccode/CHDSharp)**
