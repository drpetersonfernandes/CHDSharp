# Comparison with libchdr

This page compares CHDSharp against the C reference library [libchdr 0.3.0](https://github.com/rtissera/libchdr) (kept in [`References/libchdr-0.3.0`](../References/libchdr-0.3.0)), which CHDSharp uses as a parity baseline.

---

## Headline

**CHDSharp is a feature superset of libchdr 0.3.0**: it implements everything libchdr does (plus AVHuff, which libchdr does *not* implement), adds verification, extraction, async APIs, and metadata support, and has **zero native dependencies** — where libchdr bundles zlib (miniz), LZMA SDK, zstd, and dr_flac, CHDSharp ships managed implementations.

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
| Async API | ❌ | ✅ |
| Extraction (CUE/GDI/ISO/IMG/RAW) | ❌ | ✅ |
| TOC / track parsing | ❌ | ✅ `Tracks`/`ChdTrackInfo` |
| Classification (cd/dvd/hdd/gd-rom) | ❌ | ✅ |
| Custom IO (callbacks vs `Stream`) | ✅ core_file callbacks | ✅ `Stream` overloads |
| Thread-safe logging | ❌ | ✅ `ILoggerFactory` |
| CHD creation | ❌ (commented out) | ❌ (read-only; see `CHDSharpEncoder`) |
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
