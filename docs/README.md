# CHDSharp Wiki

Welcome to the **CHDSharp** documentation wiki.

CHDSharp is a **pure C# read-only CHD (Compressed Hunks of Data) reader** — the disk-image format used by [MAME](https://www.mamedev.org/) for arcade hard disks, CD/GD-ROMs, DVDs, and laserdisc A/V content. It supports every CHD format version (V1–V5), every compression codec ever shipped in a CHD (including Zstd and AVHuff), parent/child differential chains, parallel verification, metadata, TOC parsing, and extraction — with **zero native dependencies** and a **100% byte-for-byte match with MAME `chdman`**.

> This project is a fork of [RomVault/CHDSharp](https://github.com/RomVault/CHDSharp) by Gordon Jefferyes, extended with Zstd, AVHuff, V5 compressed maps, random access, async APIs, parent/child chaining, parallel verification, and a comprehensive test suite. The C reference implementation ([libchdr 0.3.0](https://github.com/rtissera/libchdr)) and the MAME 0.288 sources are kept in [`References/`](../References) as the authoritative format references.

---

## Quick facts

| | |
|---|---|
| Package | [`CHDSharp`](https://www.nuget.org/packages/CHDSharp/) |
| Targets | `net8.0`, `net9.0`, `net10.0` |
| Format versions | CHD V1 – V5 (read-only) |
| Codecs | `zlib`, `lzma`, `huff`, `flac`, `zstd`, `avhu` + CD variants `cdzl`, `cdlz`, `cdfl`, `cdzs` |
| Native dependencies | **none** |
| License | MIT |
| Repository | https://github.com/purelogiccode/CHDSharp |

---

## Wiki contents

### Getting started

| Page | Description |
|------|-------------|
| [Getting Started](getting-started.md) | Install the package, write your first program, tour the CLI. |
| [Building](building.md) | Build the solution, create the NuGet package, publish the CLI. |

### Format & internals

| Page | Description |
|------|-------------|
| [CHD Format Reference](chd-format.md) | The on-disk format: headers, maps, metadata, hashing, delta CHDs — V1 through V5. |
| [Codecs](codecs.md) | All ten decompression codecs: how they work and how each is implemented. |
| [Architecture](architecture.md) | Solution layout, library design, data flow, threading model. |

### API & usage

| Page | Description |
|------|-------------|
| [API Reference](api-reference.md) | Complete reference for `Chd`, `ChdFile`, and all public models/enums. |
| [Metadata](metadata.md) | Reading and querying CHD metadata tags (`GDDD`, `CHT2`, `AVAV`, …). |
| [Verification](verification.md) | Full/parallel and header-only verification, checksum semantics. |
| [Extraction](extraction.md) | TOC parsing, CUE/GDI/ISO/IMG/RAW extraction, classification. |
| [Parent/Child CHDs](parent-child-chds.md) | Differential CHDs, unit-based references, parent validation. |

### Operations

| Page | Description |
|------|-------------|
| [Performance](performance.md) | Throughput, parallelism tuning, caching, `Precache()`. |
| [Logging](logging.md) | Pluggable logging via `Microsoft.Extensions.Logging`. |
| [Error Codes](error-codes.md) | Every `ChdError` value and its meaning. |
| [Testing](testing.md) | The xUnit suite, the 30-file corpus, generators, and the WPF tester. |

### Reference

| Page | Description |
|------|-------------|
| [Comparison with libchdr](libchdr-comparison.md) | Feature parity analysis against the C reference library. |
| [Troubleshooting & FAQ](troubleshooting.md) | Common errors, known limitations, and fixes. |

---

## Feature overview

- **Any CHD, any version** — V1–V5 headers, every internal map format (self-hunk dedup, CRC32 maps, CRC16/compressed/RLE maps, uncompressed V5 maps, unit-based parent references).
- **Header DTO without opening** — `Chd.ReadHeader()` returns the full parsed header (`ChdHeaderInfo`) without keeping the file open (libchdr `chd_read_header` parity); `CheckHeader`/`IsChdFile` remain for magic/version sniffing.
- **All 10 codecs** — zlib, lzma, huffman, flac, zstd, AVHuff, plus the four CD-aware variants (`cdzl`, `cdlz`, `cdfl`, `cdzs`) with ECC/sync regeneration.
- **Random access** — `ReadHunk()`, `Read()` (byte ranges across hunk boundaries), `EnumerateHunks()`, `ReadAllBytes()`.
- **Async API** — `OpenAsync`, `ReadHunkAsync`, `ReadAsync`, `IAsyncDisposable`.
- **Progress reporting** — optional `IProgress<ChdProgress>` on `CheckFile`, `CheckFileWithParent`, `ReadAllBytes`, `EnumerateHunks`, and `ExtractToDirectory`, reported after every decompressed hunk.
- **Parallel verification** — multi-threaded `CheckFile()` with bounded memory and configurable worker count.
- **Parent/child chains** — transparent differential CHD support with wrong-parent detection.
- **Metadata** — tag/index query API (`GetMetadata`) plus the full entry list; checksum-flag aware.
- **Track info & extraction** — CD/GD-ROM TOC parsing (`ChdTrackInfo`), CUE/GDI descriptor generation, legacy `CHGT` little-endian CDDA handling (`IsLittleEndianAudio`), and whole-image extraction (`.bin`/`.cue`, `.iso`, `.img`, `.raw`, `.gdi`).
- **Pluggable logging** — `Microsoft.Extensions.Logging` integration, silent by default.
- **100% chdman match** — cross-checked against `chdman info`, `verify`, and `extractraw` (MAME 0.288).

---

## Support matrix

### Format versions

| Version | Header | Map | Notes |
|---------|--------|-----|-------|
| V1 | 76 bytes | 8-byte packed offset/length entries, self-hunk dedup | 512-byte sectors, MD5 only, no metadata |
| V2 | 80 bytes | Same as V1 | Adds `seclen` (bytes/sector) |
| V3 | 120 bytes | 16-byte entries with CRC32, self/parent hunks | Adds SHA1, metadata, `ZLIB_PLUS` |
| V4 | 108 bytes | Same as V3 | Adds `rawsha1`; combined SHA1 semantics |
| V5 | 124 bytes | CRC16 compressed map (Huffman+RLE) or uncompressed map | Up to 4 codecs, unit-based parent refs |

### Codecs

| Codec | FourCC | CD variant | C# implementation |
|-------|--------|------------|-------------------|
| Zlib (Deflate) | `zlib` | `cdzl` | `System.IO.Compression` |
| LZMA | `lzma` | `cdlz` | Custom pure-C# LZMA decoder |
| Huffman | `huff` | — | Custom pure-C# Huffman decoder |
| FLAC | `flac` | `cdfl` | Custom pure-C# FLAC decoder |
| Zstd | `zstd` | `cdzs` | [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) (pure C#) |
| AVHuff | `avhu` | — | Custom pure-C# A/V Huffman decoder |

---

## Related projects in this repository

| Project | Purpose |
|---------|---------|
| `CHDSharpLib` | The library itself (this wiki documents it). |
| `CHDSharpCli` | Command-line verification/classification tool. |
| `CHDSharpEncoder` | Companion *encoder* support library (CRC16, SHA1, Deflate, V5 map compression) — useful for writing CHDs. |
| `CHDSharpTest` | xUnit unit + corpus test suite (30 deterministic CHD files). |
| `CHDSharpTestGen` | Deterministic corpus generator (drives vintage `chdman` binaries). |
| `CHDSharpTester` | WPF interactive batch verifier cross-checked against `chdman`. |

---

## License

MIT License — see [LICENSE](../LICENSE.txt).

## Acknowledgments

- **Gordon Jefferyes** — original C# CHDSharp implementation (RomVault).
- **MAME** — CHD format specification and `chdman` reference implementation.
- **libchdr** (Romain Tisseraud) — C reference library, used for parity comparison.
- **ZstdSharp.Port** (Oleg Stepanischev) — pure C# Zstd decompressor.
