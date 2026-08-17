# CHDSharpEncoder

**A CHD v5 encoder in pure C#** — a companion to the CHDSharp reader library. It
produces files that pass `chdman verify` and extract byte-identically via
`chdman extractraw`, with a **100% byte-for-byte match** with `chdman` when it uses the
same codec, and parallel compression across up to 64 workers.

> Implementation plan and validation history: [`References/EncoderPlan.md`](../References/EncoderPlan.md).
> Format references: MAME 0.288 (`References/mame-mame0288`), chd-rs (`References/chd-rs-master`), CHDlite (`References/CHDlite-main`).

---

## Features

| Capability | Status |
|------------|--------|
| Raw binary → CHD (`EncodeRaw`) | ✅ |
| CD images → CHD (`EncodeCd`) via CUE, GDI, ISO, TOC | ✅ |
| Codecs | all 10 MAME codecs (`zlib`, `zstd`, `lzma`, `huff`, `flac`, `cdzl`, `cdlz`, `cdzs`, `cdfl`, `none`); up to 4 per file, best-per-hunk |
| SELF-hunk deduplication (COMPRESSION_SELF, with SELF_0/SELF_1 map promotion) | ✅ |
| CHT2 / CHGD metadata (linked list, checksummed, combined SHA-1) | ✅ |
| Audio byte-swap (little-endian BIN → big-endian CHD, like chdman) | ✅ |
| Per-hunk compression-ratio logging (`ChdEncodeOptions.HunkCompleted`) | ✅ |
| Parallel hunk compression (producer→worker→consumer pipeline, `TaskCount` 1–64) | ✅ |
| Parent CHD (`COMPRESSION_PARENT`) | not implemented |
| NRG (Nero) input | not implemented |

**Validation**: 316 xUnit tests (`CHDSharpEncoderTest`), cross-checked against
`chdman.exe` v0.288 (`chdman info` / `verify` / `extractraw` / `createcd` /
`createraw`) and the CHDSharpLib reader — including 100 MB+ integration tests and
byte-identical-output tests across worker counts.

---

## Quick start

```csharp
using CHDSharpEncoder;

// Raw binary → CHD (hunk 4096 B, unit 512 B, zlib)
ChdEncoder.EncodeRaw("game.bin", "game.chd");

// CD image → CHD from a CUE sheet (8 frames per hunk, 2448 B frames)
ChdEncoder.EncodeCd("game.cue", "game.chd");

// More codecs (tried per hunk; smallest output wins)
ChdEncoder.EncodeRaw("game.bin", "game.chd", 4096, 512,
    codecTags: ChdCodecs.ParseCodecTags("zlib,zstd,lzma"));
```

Both APIs also accept a `ChdEncodeOptions` for per-hunk compression-ratio logging and
parallelism control:

```csharp
var options = new ChdEncodeOptions
{
    // parallel compression workers (default: CHDSharp.Chd.TaskCount, 1-64)
    TaskCount = 8,
    HunkCompleted = p => Console.WriteLine(
        $"hunk {p.HunkIndex,6}/{p.HunkCount}  {p.CodecName,-5} {p.RawBytes,8} -> {p.StoredBytes,8} B  ({p.Ratio:P1})")
};

ChdEncoder.EncodeRaw("game.bin", "game.chd", options: options);
```

Callbacks fire once per hunk, **in hunk order**, and never affect the output bytes
(reporting is purely observational — see `WithCallback_OutputIsByteIdentical_ToWithout`).

### Progress reporting semantics

`HunkProgress` reports, per hunk:

- `RawBytes` — the uncompressed hunk size;
- `StoredBytes` — 0 for a SELF reference, the hunk size for `COMPRESSION_NONE`,
  otherwise the compressed length;
- `CompressionType` — map type 0–3 (codec index), 4 (none), 5 (SELF);
- `CodecName` — `"zlib"`, `"zstd"`, `"lzma"`, `"cdfl"`, `"none"`, `"self"`;
- `Ratio` — `StoredBytes / RawBytes` (0 for SELF references).

---

## CLI

`CHDSharpCli` exposes the encoder:

```bash
# Raw binary → CHD
CHDSharpCli --create in.bin out.chd [-c zlib,zstd,lzma] [-hs 65536] [-us 4096] [-t 8] [-v]

# CD image → CHD (CUE/GDI/ISO/TOC)
CHDSharpCli --createcd in.cue out.chd [-c zlib,zstd,lzma] [-hs N] [-us N] [-t 8] [-v]
```

`-v` / `--verbose` prints one line per hunk (codec, sizes, ratio) plus an overall
stored-bytes summary. `-t N` sets the parallel compression worker count (default:
`Chd.TaskCount`). Both commands run a deep CHDSharpLib `CheckFile` on the result
before exiting.

---

## Codecs

| Tag | Codec | Notes |
|-----|-------|-------|
| `zlib` | Deflate (`System.IO.Compression`, `SmallestSize`) | Default; matches `chdman -c zlib` byte-for-byte |
| `zstd` | Zstandard at max level (ZstdSharp.Port) | Matches MAME's `ZSTD_maxCLevel()` |
| `lzma` | Raw headerless LZMA (SharpCompress 0.39.0) | lc=3/lp=0/pb=2, dictionary = hunk size; see plan §3 |
| `huff` | MAME generic Huffman | Weight-scaled canonical tree, Huffman-encoded tree export (see plan §1) |
| `flac` | Raw FLAC (2-pass LE/BE, marker byte) | From-scratch FLAC frame encoder; MAME blocksize formula |
| `cdzl`/`cdlz`/`cdzs` | CD compound (ECC + zlib/LZMA/zstd) | `[ecc bitmap][base length][base][subcode]` layout, Mode-1 sync/ECC clearing |
| `cdfl` | CD FLAC + deflated subcode | 2352-sample blocks (MAME's cdfl blocksize), validated against libFLAC |
| `none` | Uncompressed CHD | recognized, throws `NotSupportedException` (roadmap: Phase 4.2) |

All codecs are deterministic: the same input always produces the same output, so
parallelism can never change the bytes (see [Performance](#performance)).

---

## Project layout

```
CHDSharpEncoder/
├── ChdEncoder.cs        Public API (EncodeRaw / EncodeCd orchestrators)
├── ChdEncodeOptions.cs  HunkProgress record + options (per-hunk ratio logging)
├── ChdCodec.cs          IChdCodec, zlib/zstd/lzma codecs, tag parsing
├── CdflCodec.cs         CD FLAC codec (+ Flac/ frame encoder)
├── HunkProcessor.cs     Producer→worker→consumer compression pipeline + map entries
├── MapCompressor.cs     V5 compressed map (RLE + Huffman, SELF promotion)
├── MetadataWriter.cs    CHT2/CHGD metadata, combined SHA-1
├── CdImageParser.cs     CUE / GDI / ISO / TOC dispatch
├── CueParser.cs, GdiParser.cs, IsoParser.cs, TocParser.cs, CdToc.cs
├── BigEndianWriter.cs, Crc16.cs, Sha1.cs, RawDeflate.cs, BitStream.cs,
├── Huffman16_8.cs, ChdHeaderV5.cs, MapEntry.cs
└── (tests in CHDSharpEncoderTest/)
```

---

## Performance

Encoding runs a **producer→worker→consumer pipeline** (`HunkProcessor.CompressAll`, the
same shape as the library's parallel `CheckFile`): a single producer reads the raw hunks
and maintains the running raw SHA-1, `N` workers (default `Chd.TaskCount`; 1–64) hash and
compress each hunk with their own persistent codec instances, and a single consumer writes
blocks and map entries strictly in hunk order. Because every codec is deterministic and
dedup/offset assignment is sequential, the worker count never changes a single output byte
(`ParallelEncodeTests` asserts byte-identical files across task counts).

Measured on a 24-core machine (512 MB mixed corpus, zlib): **5.1× faster with 8 workers**
than 1 (5.0 s → 0.98 s, byte-identical output).

What exists today:

- **Per-hunk compression-ratio logging** via `ChdEncodeOptions.HunkCompleted` (library)
  and `-v` (CLI) — aggregate or chart ratios per codec without touching output bytes.
- **Parallelism control** via `ChdEncodeOptions.TaskCount` (library) or `-t N` (CLI); the
  default follows `Chd.TaskCount`, the same knob that tunes parallel verification.
- **100 MB+ integration tests** (`LargeFileValidationTests`): 100 MB raw and ~100 MB CD
  round-trips validated with `chdman verify`, `chdman extractraw` (SHA-1 vs. source) and
  a deep CHDSharpLib `CheckFile`. Run them with:

```bash
dotnet test CHDSharpEncoderTest/ --filter "FullyQualifiedName~LargeFileValidationTests"
```

Memory use is bounded: raw hunks and compressed results circulate through fixed-size
buffer pools sized by the worker count, so multi-GB sources encode in constant memory.

---

## Known limitations

- No `COMPRESSION_PARENT` (differential) CHD creation.
- No NRG (Nero) input parsing.
- `-c none` (uncompressed CHD) throws `NotSupportedException` (roadmap).

## License

MIT — see [LICENSE](../LICENSE.txt).