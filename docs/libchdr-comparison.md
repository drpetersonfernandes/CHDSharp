# Comparison with libchdr

This page compares CHDSharp against the C reference library [libchdr 0.3.0](https://github.com/rtissera/libchdr) (kept in [`References/libchdr-0.3.0`](../References/libchdr-0.3.0)), which CHDSharp uses as a parity baseline.

---

## Headline

**CHDSharp is a feature superset of libchdr 0.3.0**: it implements everything libchdr does (plus AVHuff, which libchdr does *not* implement), adds verification, extraction, async APIs, and metadata support, and has **zero native dependencies** — where libchdr bundles zlib (miniz), LZMA SDK, zstd, and dr_flac, CHDSharp ships managed implementations.

---

## Five-way comparison: CHDSharp vs chd-rs vs CHDlite vs chdman vs libchdr

CHDSharp vs the two other independent CHD implementations ([chd-rs 0.3.4](../References/chd-rs-master) and [CHDlite 0.2.1](../References/CHDlite-main), compared against their checked-in sources), MAME's reference `chdman` (0.288), and libchdr 0.3.0. The CHDSharp column covers the whole repo (reader + `CHDSharpEncoder` + CLI). ✅ = supported, 🟡 = partial, ❌ = not supported, — = not applicable (CLI).

| Capability | CHDSharp (this repo) | [chd-rs](../References/chd-rs-master) 0.3.4 (Rust) | [CHDlite](../References/CHDlite-main) 0.2.1 (C++) | `chdman` (MAME 0.288) | [libchdr](../References/libchdr-0.3.0) 0.3.0 (C) |
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
| All 10 codecs (encode) | ✅ | ❌ | ✅ | ✅ | ❌ |
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

The same table is also in the [repository README](../README.md#library-comparison-chdsharp-vs-chd-rs-vs-chdlite-vs-chdman-vs-libchdr).

---

## Feature matrix

| Feature | libchdr 0.3.0 (C) | CHDSharp (C#) |
|---------|:---:|:---:|
| CHD V1–V5 headers | ✅ | ✅ |
| V1/V2 maps (packed entries, self-dedup) | ✅ | ✅ |
| V3/V4 maps (CRC32, mini/self/parent hunks) | ✅ | ✅ |
| V5 compressed map (Huffman+RLE) | ✅ | ✅ |
| V5 uncompressed map | ✅ | ✅ |
| V5 unit-based parent references (incl. unaligned/straddling) | ✅ | ✅ |
| `zlib` / `cdzl` | ✅ (miniz) | ✅ (managed) |
| `lzma` / `cdlz` | ✅ (LZMA SDK) | ✅ (custom C# port) |
| `huff` | ✅ | ✅ |
| `flac` / `cdfl` | ✅ (dr_flac) | ✅ (custom C# decoder) |
| `zstd` / `cdzs` | ✅ (zstd 1.5.7) | ✅ (ZstdSharp.Port) |
| `avhu` (AVHuff) | ❌ *(known limitation)* | ✅ |
| Secondary codec (`ZLIB_PLUS` type-6 hunks) | ❌ *declared but unimplemented* | ✅ |
| Per-hunk CRC32 verification (V3/V4) | ❌ *stored, never checked* | ✅ (honors NO_CRC) |
| Per-hunk CRC16 verification (V5) | ✅ (build option, default on) | ✅ |
| Full-image verification (SHA1/MD5/rawsha1) | ❌ *no verify function* | ✅ parallel |
| Combined metadata-SHA1 verification | ❌ | ✅ |
| Metadata query by tag/index/flags | ✅ `chd_get_metadata` | ✅ `GetMetadata` + `Metadata` list |
| V1/V2 synthesized GDDD metadata | ✅ | ✅ |
| `chd_precache` (whole file in RAM) | ✅ | ✅ `Precache()` |
| Random access (`chd_read` / `ReadHunk`, `Read`) | ✅ | ✅ |
| Byte-range reads | ❌ (hunk-only) | ✅ `Read(offset, ...)` |
| Sector-addressed reads (LBA/MSF) | ❌ *requested (#155)* | ✅ `ReadSector`/`ReadSectorMsf`/`ReadFrame` + `CdRomAddress` |
| Async API | ❌ | ✅ |
| Extraction (CUE/GDI/ISO/IMG/RAW) | ❌ | ✅ |
| TOC / track parsing | ❌ | ✅ `Tracks`/`ChdTrackInfo` |
| Classification (cd/dvd/hdd/gd-rom) | ❌ | ✅ |
| Custom IO (callbacks vs `Stream`) | ✅ core_file callbacks | ✅ `Stream` overloads |
| Thread-safe logging | ❌ | ✅ `ILoggerFactory` |
| CHD creation | ❌ (commented out) | ✅ [`CHDSharpEncoder`](../CHDSharpEncoder/README.md) |
| Native dependencies | zlib, lzma, flac, zstd | **none** |

---

## Parity work in this repository

To close the small gaps found during the comparison, the library gained:

1. **`GetMetadata(string? tag, uint index, out ChdMetadataEntry?)`** — mirrors `chd_get_metadata` (tag search, occurrence index, wildcard via `null`/empty tag, `Chderrmetadatanotfound`).
2. **`ChdMetadataEntry.Flags`** — exposes the metadata flags byte (libchdr's `resultflags`).
3. **`ChdFile.Precache()`** — mirrors `chd_precache` (whole compressed file in memory, idempotent, stream position restored).
4. **V1/V2 synthesized GDDD metadata** — matches libchdr's behavior of fabricating `CYLS:…,HEADS:…,SECS:…,BPS:…` from the obsolete header fields.

All four are covered by `ParityFeaturesTests`.

---

## Deliberate differences (CHDSharp is stricter)

| Area | libchdr | CHDSharp | Why |
|------|---------|----------|-----|
| V3/V4 CRC32 | never verified | verified (unless NO_CRC) | matches MAME semantics; catches corrupt files libchdr silently accepts |
| V3/V4 `ZLIB_PLUS` type-6 hunks | falls through, returns success with empty output | fully decoded (secondary codec) | correctness |
| AVHuff | unsupported (open fails or errors) | fully decoded | feature |
| Metadata errors | `CHDERR_METADATA_NOT_FOUND` only | also `Chderrreaderror`/`Chderrinvaliddata` surfaced | diagnostics |
| `Open(Stream)` IO failures | returns errors | returns errors (never throws) | robustness |

---

## Notes on decoder stacks

- **FLAC:** libchdr uses dr_flac 0.13.3 (battle-tested, full spec). CHDSharp's custom decoder covers everything CHD content uses — 16/24-bit, all channel modes incl. mid/side, fixed/LPC subframes (orders 1–32), all block sizes, Rice coding, CRC-8/16 — and rejects unsupported cases (e.g. 8/12/20-bit, custom sample-rate codes) that `chdman` never produces. The corpus includes FLAC, cdfl, and AVHuff-FLAC fixtures.
- **LZMA:** both synthesize the fixed properties (lc=3, lp=0, pb=2, dict = hunk size) since CHD hunks are headerless; CHDSharp's port also supports LZMA2 and preset dictionaries internally.
- **Zstd:** libchdr uses zstd 1.5.7 native; CHDSharp uses ZstdSharp.Port 0.8.8 (pure C#). Both handle single-frame blocks correctly.

---

## When to use which

- **Use CHDSharp** when you want a managed, dependency-free reader with verification, metadata, extraction, and modern .NET ergonomics (async, nullable, `IAsyncDisposable`).
- **Use libchdr** when you need a C library for embedding in C/C++ projects, or want the (extremely well-tested) native zstd/LZMA/FLAC stacks and do not need AVHuff, verification, or extraction.
