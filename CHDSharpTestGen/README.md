# CHDSharpTestGen

**Deterministic CHD test corpus generator for the CHDSharp xUnit test suite.**

Generates the 29-file CHD corpus in `CHDSharpTest/TestData/`, covering every format version (V1–V5), all compression codecs (zlib, lzma, huffman, flac, zstd, avhu + CD variants), every map type, and parent/child chains. All source images are built deterministically (seeded PRNG) so regenerating the corpus produces byte-identical output.

---

## Usage

```bash
# Generate the full corpus (output → ../CHDSharpTest/TestData/)
dotnet run --project CHDSharpTestGen

# AVI passthrough test for chdman's aviio reader
dotnet run --project CHDSharpTestGen -- --avitest

# Debug a single FLAC-hunked V5 CHD
dotnet run --project CHDSharpTestGen -- --hunkdebug path\to\v5_flac.chd
```

### Prerequisites

Four vintage chdman binaries must be placed in `CHDSharpTest/chdman/`:

| Binary | MAME Version | Used For |
|--------|-------------|----------|
| `hdcomp_v1.exe` | ~0.77 | V1 format (legacy hdcomp) |
| `chdman_v3.exe` | ~0.130 | V3 format (raw, CD, A/V, diff) |
| `chdman_v4.exe` | ~0.145 | V4 format (raw, uncompressed, CD, diff) |
| `chdman_v5.exe` | ~0.288 | V5 format (all codecs, CD variants, laserdisc, parent/child) |

---

## Generated Corpus

| Version | Files | Content |
|---------|-------|---------|
| **V1** | `v1_zlib.chd` | Raw HD, zlib codec, legacy self-dedup map |
| **V2** | `v2_zlib.chd` | Synthesized from V1 by patching header + shifting offsets |
| **V3** | `v3_zlib.chd` | Raw HD, zlib+ codec |
| | `v3_cd.chd` | CD-ROM (TOC), zlib/flac codec |
| | `v3_av.chd` | A/V laserdisc, avhuff legacy codec (comp type 3) |
| | `v3_child.chd` | Differential CHD with parent refs (`v3_zlib.chd` parent) |
| **V4** | `v4_zlib.chd` | Raw HD, zlib+ codec |
| | `v4_uncomp.chd` | Uncompressed HD, uncompressed map entries |
| | `v4_cd.chd` | CD-ROM (CUE), zlib/flac era codec |
| | `v4_av.chd` | A/V laserdisc, synthesized from V3 (chdman 0.145 A/V crashes) |
| | `v4_child.chd` | Differential CHD (`v4_zlib.chd` parent) |
| **V5** | `v5_zlib.chd` | Raw, single codec: zlib |
| | `v5_lzma.chd` | Raw, single codec: lzma |
| | `v5_huff.chd` | Raw, single codec: huffman |
| | `v5_flac.chd` | Raw, single codec: flac |
| | `v5_zstd.chd` | Raw, single codec: zstd |
| | `v5_multi.chd` | Raw, 4-slot codec: lzma,zlib,huff,flac |
| | `v5_none.chd` | Raw, uncompressed map (compression none) |
| | `v5_tiny.chd` | Single-hunk file, degenerate huffman map (expected: invalid) |
| | `v5_odd.chd` | Logical size not a hunk multiple (partial last hunk) |
| | `v5_parent.chd` | Parent for V5 child chains |
| | `v5_child.chd` | Compressed-map child with parent refs |
| | `v5_child_none.chd` | Uncompressed-map child with offset-0 parent refs |
| | `v5_child_hs2560.chd` | Child with mismatched hunk size (unaligned parent refs) |
| | `v5_cd_default.chd` | CD, default codec selection (cdlz,cdzl,cdfl) |
| | `v5_cd_cdzl.chd` | CD, single codec: cdzl |
| | `v5_cd_cdlz.chd` | CD, single codec: cdlz |
| | `v5_cd_cdfl.chd` | CD, single codec: cdfl |
| | `v5_cd_cdzs.chd` | CD, single codec: cdzs |
| | `v5_ld_avhu.chd` | Laserdisc, avhuff codec |

### Special Entries

| File | Note |
|------|------|
| `v2_zlib.chd` | No stock MAME tool ever produced V2 by default. Synthesized from V1 by appending a `seclen` field and shifting all map offsets by +4 bytes. |
| `v4_av.chd` | chdman 0.145's A/V compression crashes. Synthesized from `v3_av.chd` by shrinking the V3 header to V4 layout, recomputing the combined SHA1. |
| `v5_tiny.chd` | A 1-hunk file triggers a degenerate single-symbol huffman map in chdman that MAME cannot read. The library must gracefully reject it (`expect: "invalid"`). |
| `v5_child_hs2560.chd` | Child with hunk size 2560 vs parent 4096 causes unit-unaligned parent references (two-hunk stitching on read). |

---

## Architecture

```
CHDSharpTestGen/
├── Program.cs          — Entry point, corpus generation pipeline, manifest writing
├── SourceData.cs       — Deterministic source image builder (raw, CD, AVI)
├── DetRng.cs           — xorshift64* deterministic PRNG
├── AviWriter.cs        — Minimal AVI writer (YUY2 + PCM) for chdman -createav / createld
├── ToolRunner.cs       — Process wrapper for invoking chdman/hdcomp binaries
├── V2Patcher.cs        — V1 → V2 header/map converter
└── V3ToV4Patcher.cs    — V3 → V4 header/map/metadata converter (with SHA1 recompute)
```

### Determinism

All source images are built with a **xorshift64*** PRNG seeded with hard-coded constants:
- `0xC0FFEE01` — primary raw image (512 KiB, 128 hunks of 4096 bytes)
- `0xC0FFEE02` — child variant (modifies 14 hunks)
- `0xC0FFEE03` — CD data track (600 sectors of mixed content)
- `0xC0FFEE04` — CD audio track (400 WAV frames, sine sweep + noise bursts)

Each hunk in the raw image exercises a different map encoding path:
| Hunks | Content | Map Encoding |
|-------|---------|-------------|
| 0–7 | Zeros | Mini (V3/V4), zero/self (V5) |
| 8–15 | Repeating 8-byte pattern | Mini (V3/V4), compressed (V5) |
| 16–31 | ASCII text | Compressed (codec) |
| 32–39 | Random noise | Stored uncompressed (none) |
| 40–47 | Duplicates of hunks 16–23 | Self references |
| 48–127 | Structured runs | Compressed (codec) |

---

## Output

All generated `.chd` files and `manifest.json` are written to `CHDSharpTest/TestData/`. The manifest is a JSON array:

```json
[
  {
    "file": "v1_zlib.chd",
    "version": 1,
    "parent": null,
    "expect": "ok",
    "note": "raw hd, zlib, legacy map (none/self/type0)"
  },
  {
    "file": "v3_child.chd",
    "version": 3,
    "parent": "v3_zlib.chd",
    "expect": "ok",
    "note": "diff chd, map: parent refs"
  }
]
```

Consumed at test time by `CHDSharpTest/CorpusTests.cs` via `[MemberData]` / `[Theory]`.

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `CHDSharpLib` | (project reference) | CHD header reading, model types |

No NuGet package is produced — this tool is internal to the CHDSharp test infrastructure.

---

## Building

```bash
dotnet build CHDSharpTestGen\CHDSharpTestGen.csproj -c Release
```
