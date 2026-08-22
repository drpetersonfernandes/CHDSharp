---
layout: default
---

# CHD creation (CHDSharpEncoder)

`CHDSharpEncoder` is the encoder companion to the CHDSharp reader. It writes **CHD v5**
files from raw binaries and CD images (CUE/GDI/ISO/TOC/NRG), re-compresses existing CHDs
(`Copy`), creates differential (delta) children against a parent, and writes
uncompressed CHDs (`-c none`) — producing files that are **byte-for-byte identical to
`chdman`** when the same codec is used, pass `chdman verify`, and extract back
identically via `chdman extractraw`. The library is **100% pure C#** (no native DLLs)
and runs identically on Windows and Linux. One parity caveat: `cdzs` encode output is
valid and chdman-verifiable but not always bit-identical to chdman's own file (the managed
zstd port finalizes frames differently on some buffer sizes); every other codec is bit-exact.

Full API docs and project layout: [`CHDSharpEncoder/README.md`](../CHDSharpEncoder/README.md).
Implementation plan and validation history: [`References/EncoderPlan.md`](../References/EncoderPlan.md).

---

## Capabilities

| | |
|---|---|
| Raw encode | `ChdEncoder.EncodeRaw(source, chdPath, hunkBytes, unitBytes, codecTags, options)` |
| CD encode | `ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes, unitBytes, codecTags, options)` |
| Copy / re-compress | `ChdEncoder.Copy(sourceChd, chdPath, codecTags, options)` — any V1–V5 source, metadata cloned |
| Laserdisc encode | `ChdEncoder.EncodeLaserDisc(aviPath, chdPath)` — AVI → V5 laserdisc CHD (AVHuff: delta-RLE Huffman video + FLAC audio), interlace detection, VBI metadata capture, frame range selection |
| Laserdisc extract | `ChdEncoder.ExtractLaserDisc(chdPath, aviPath)` — V5 laserdisc CHD → AVI (YUY2 video + PCM audio), interlaced field assembly, frame range selection |
| Input formats | raw binary; CUE/BIN, GDI, ISO, TOC (cdrdao-style), NRG (Nero); AVI (YUY2/VYUY/UYVY + PCM); existing CHD files |
| Codecs | `zlib` (default), `zstd`, `lzma`, `huff`, `flac`, `cdzl`, `cdlz`, `cdzs`, `cdfl`, `none` — up to 4 per file, smallest output per hunk |
| Deduplication | SELF references (CRC/SHA-1 keyed), with SELF_0/SELF_1 map promotion |
| Delta (parent) CHDs | `ChdEncodeOptions.ParentPath` — COMPRESSION_PARENT refs, unit-split windows, chdman `-op` parity |
| Uncompressed CHD | `-c none` — V5 raw map, hunk-aligned raw data, zero hunks skipped, chdman byte-identical |
| Metadata | CHT2 (CD), CHGD (GD-ROM), GDDD (HDD), DVD entries, AVAV/AVLD (laserdisc), checksummed, combined SHA-1 |
| CD audio | byte-swapped to big-endian (as stored on disc), tracks padded to 4-frame boundaries |
| Ratio logging | per-hunk callback (`ChdEncodeOptions.HunkCompleted`) — never changes output |

```csharp
using CHDSharpEncoder;

ChdEncoder.EncodeRaw("game.bin", "game.chd");                       // raw, zlib
ChdEncoder.EncodeCd("game.cue", "game.chd");                        // CD, zlib
ChdEncoder.EncodeRaw("game.bin", "game.chd", 65536, 4096,
    ChdCodecs.ParseCodecTags("zlib,zstd,lzma"),
    new ChdEncodeOptions { HunkCompleted = p => Console.WriteLine(
        $"hunk {p.HunkIndex}/{p.HunkCount} {p.CodecName} {p.Ratio:P1}") });
ChdEncoder.Copy("old.chd", "new.chd", codecTags: [CodecTags.Zstd]); // re-compress
ChdEncoder.EncodeRaw("game.bin", "game.chd", 4096, 512,
    options: new ChdEncodeOptions { ParentPath = "base.chd" });     // delta child
ChdEncoder.EncodeRaw("game.bin", "game.chd", codecTags: [CodecTags.None]); // uncompressed
```

Callbacks fire in hunk order and are purely observational — encoding with a callback
produces byte-identical output to encoding without one.

---

## Validation

The encoder is validated against `chdman.exe` v0.288 and the CHDSharpLib reader
(371 tests per target framework in `CHDSharpEncoderTest`):

- `chdman info` reports the file without errors; `chdman verify` passes (raw + overall SHA-1).
- `chdman extractraw` of encoder output is byte-identical to the source (raw) and to
  `chdman createcd` output on the same CUE/BIN (CD).
- For repeated/alternating corpora the encoder's CHD files are **byte-for-byte identical
  to `chdman createraw -c zlib`** — deduplication and map encoding match MAME exactly.
- `-c none` output is **byte-for-byte identical to `chdman createraw -c none`** (including
  zero-hunk skipping), and `chdman verify` (exit 0) + `extractraw` round-trip it.
- `Copy` outputs pass `chdman verify` and extract byte-identically (standalone, child-source,
  and delta-child variants).
- Delta children made from chdman-made parents pass `chdman verify -ip` and byte-identical
  `extractraw -ip`.
- **cdzs caveat**: output passes `chdman verify`, deep `CheckFile`, and `extractcd` parity,
  but whole-file bytes may differ from chdman's (managed zstd trailing byte).
- **100 MB+ integration tests** (`LargeFileValidationTests`) encode 100 MB raw and
  ~100 MB CD images, then check `chdman verify`, `extractraw` SHA-1 vs. the source, and a
  deep CHDSharpLib `CheckFile`:

```bash
dotnet test CHDSharpEncoderTest/ --filter "FullyQualifiedName~LargeFileValidationTests"
```

---

## Performance

Encoding runs a **producer→worker→consumer pipeline** (`HunkProcessor.CompressAll`, the
same shape as the library's parallel `CheckFile`): a single producer reads the raw hunks
and maintains the running raw SHA-1, `N` workers (default `Chd.TaskCount`, 1–64, override
via `ChdEncodeOptions.TaskCount` or CLI `-t`) hash and compress each hunk with private,
persistent codec instances, and a single consumer writes blocks and map entries strictly
in hunk order. Every codec is deterministic and dedup/offset assignment stays sequential,
so the worker count can never change the output bytes (`ParallelEncodeTests` asserts
byte-identical output across task counts).

Measured on a 24-core machine (512 MB mixed corpus, zlib): **5.1× faster with 8 workers**
vs. 1 (5.0 s → 0.98 s, identical 179 MB output).

For tuning and measurement today:

- `ChdEncodeOptions.TaskCount` (or CLI `-t N`) controls the worker count per encode; the
  default follows `Chd.TaskCount`, the same knob that tunes parallel verification.
- Per-hunk compression-ratio logging (`ChdEncodeOptions.HunkCompleted`, CLI `-v`).
- Memory is bounded: raw hunks and compressed results circulate through fixed-size pools
  sized by the worker count, so multi-GB sources encode without proportional RAM growth.

## CLI

```bash
CHDSharpCli --create in.bin out.chd [-c zlib,zstd,lzma,none] [-hs 65536] [-us 4096] [-t 8] [-ip parent.chd] [-tp id] [-d] [-v]
CHDSharpCli --createcd in.cue out.chd [-c zlib,zstd,lzma,none] [-hs N] [-us N] [-t 8] [-ip parent.chd] [-v]
CHDSharpCli --createld in.avi out.chd [-c avhu] [-isf N] [-if N] [-hs N] [-v]
CHDSharpCli --extractld in.chd out.avi [-isf N] [-if N]
CHDSharpCli --listtemplates
CHDSharpCli --copy in.chd out.chd [-c zlib,zstd,lzma,none] [-t 8] [-ip parent.chd] [-op parent.chd] [-v]
```

All commands deep-verify the result with CHDSharpLib before exiting.

## Status

All chdman-reachable features are implemented: all 10 writable codecs (including `avhu`
via `createld`/`extractld`), NRG/GDI/ISO/TOC/CUE input, AVI input/output for laserdisc,
predefined HDD geometry templates (`--listtemplates`, `-tp <id>`), metadata editing
(`SetMetadata`/`DeleteMetadata` + CLI `addmeta`/`delmeta`), CUE style conversion /
Redump matching (`CueConverter`), platform detection with smart codec presets (`-c auto`),
and byte-exact map clipping parity. `createld` produces valid, `chdman verify`-passing
laserdisc CHDs from AVI files (YUY2/VYUY/UYVY, interlaced or progressive, with VBI
metadata); `extractld` decodes laserdisc CHDs back to playable AVI files. The only gaps
are encoding-level byte-parity with chdman's `createld` output (ours is smaller due to
more compact encoding) and `cdzs` bit-exactness (managed zstd trailing byte). Future ideas
live in [`References/ProposedFixes.md`](../References/ProposedFixes.md).