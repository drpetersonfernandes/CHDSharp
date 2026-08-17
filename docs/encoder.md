# CHD creation (CHDSharpEncoder)

`CHDSharpEncoder` is the encoder companion to the CHDSharp reader. It writes **CHD v5**
files from raw binaries and CD images (CUE/GDI/ISO/TOC), producing files that are
**byte-for-byte identical to `chdman`** when the same codec is used, pass
`chdman verify`, and extract back identically via `chdman extractraw`.

Full API docs and project layout: [`CHDSharpEncoder/README.md`](../CHDSharpEncoder/README.md).
Implementation plan and validation history: [`References/EncoderPlan.md`](../References/EncoderPlan.md).

---

## Capabilities

| | |
|---|---|
| Raw encode | `ChdEncoder.EncodeRaw(source, chdPath, hunkBytes, unitBytes, codecTags, options)` |
| CD encode | `ChdEncoder.EncodeCd(cuePath, chdPath, hunkBytes, unitBytes, codecTags, options)` |
| Input formats | raw binary; CUE/BIN, GDI, ISO, TOC (cdrdao-style) |
| Codecs | `zlib` (default), `zstd`, `lzma`, `cdfl` — up to 4 per file, smallest output per hunk |
| Deduplication | SELF references (CRC/SHA-1 keyed), with SELF_0/SELF_1 map promotion |
| Metadata | CHT2 (CD) and CHGD (GD-ROM) entries, checksummed, combined SHA-1 |
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
```

Callbacks fire in hunk order and are purely observational — encoding with a callback
produces byte-identical output to encoding without one.

---

## Validation

The encoder is validated against `chdman.exe` v0.288 and the CHDSharpLib reader
(258 tests in `CHDSharpEncoderTest`):

- `chdman info` reports the file without errors; `chdman verify` passes (raw + overall SHA-1).
- `chdman extractraw` of encoder output is byte-identical to the source (raw) and to
  `chdman createcd` output on the same CUE/BIN (CD).
- For repeated/alternating corpora the encoder's CHD files are **byte-for-byte identical
  to `chdman createraw -c zlib`** — deduplication and map encoding match MAME exactly.
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
CHDSharpCli --create in.bin out.chd [-c zlib,zstd,lzma] [-hs 65536] [-us 4096] [-t 8] [-v]
CHDSharpCli --createcd in.cue out.chd [-c zlib,zstd,lzma] [-hs N] [-us N] [-t 8] [-v]
```

Both commands deep-verify the result with CHDSharpLib before exiting.

## Roadmap

- Parent (differential) CHD creation.
- NRG (Nero) input parsing.
- `-c none` (uncompressed) CHD creation.