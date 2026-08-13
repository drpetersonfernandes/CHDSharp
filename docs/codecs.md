# Codecs

CHDSharp implements **all ten decompression codecs** used by the CHD format. Every codec is a delegate with the signature

```csharp
delegate ChdError ChdReader(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodecState codec);
```

dispatched per hunk by `ChdBlockRead.ReadBlock` according to the map entry's compression type. `ChdCodecState` holds reusable per-codec scratch (LZMA dictionary window, zstd decompressor, FLAC decoder, Huffman lookup tables) so sequential hunks do not reallocate.

| Codec | FourCC | CD variant | Implementation in CHDSharp |
|-------|--------|------------|----------------------------|
| Zlib | `zlib` | `cdzl` | `System.IO.Compression.DeflateStream` (managed) |
| LZMA | `lzma` | `cdlz` | Custom pure-C# port of the LZMA SDK decoder |
| Huffman | `huff` | — | Custom pure-C# Huffman decoder |
| FLAC | `flac` | `cdfl` | Custom pure-C# FLAC decoder |
| Zstd | `zstd` | `cdzs` | [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) (pure C#) |
| AVHuff | `avhu` | — | Custom pure-C# A/V Huffman decoder |

---

## zlib / cdzl

Raw DEFLATE (`-MAX_WBITS`, i.e. no zlib wrapper — exactly what `chdman` writes). CHDSharp uses the managed `DeflateStream`.

The **`cdzl`** CD variant splits each CD frame (2352 bytes of sector data + 96 bytes of subcode = 2448 total) into two streams:

1. The hunk header carries an ECC-bitmap (1 bit per frame: ECC/sync present or stripped) plus the compressed lengths.
2. Sector data is zlib-compressed, subcode is zlib-compressed.
3. On decode, CHDSharp reassembles frames and **regenerates the sync header and ECC bytes** (`CdRom.EccGenerate`) for frames flagged as ECC-stripped.

## lzma / cdlz

CHD LZMA hunks are **raw, headerless LZMA payloads** — there is no 5-byte properties header in the stream. Both `chdman` (encoder) and libchdr (decoder) use fixed settings:

- `lc = 3, lp = 0, pb = 2` → properties byte `0x5D` (`93`).
- Dictionary size = the hunk size (always ≥ the maximum back-reference distance, since each hunk is compressed independently).

CHDSharp synthesizes these properties and decodes with its own C# port of the LZMA SDK decoder (`LZMA/`): full literal/match state machine, four repeat distances, range-coder validation, end-marker handling, and 32/64-bit output paths. The reusable dictionary buffer is amortized across hunks via `ChdCodecState`.

The **`cdlz`** variant uses LZMA for sector data and zlib for subcode, with the same ECC/sync regeneration as `cdzl`.

## huff

A custom static 8-bit Huffman codec. The tree itself is Huffman-encoded in the hunk (a 24-code/6-bit meta-Huffman with RLE runs); decoding uses a 16-bit-wide lookup table (`HuffmanDecoder`). Each hunk decodes exactly `hunkbytes` symbols. Overflow or under-consumption of the bitstream after flushing is treated as invalid data.

The same machinery (`ImportTreeRle`) powers AVHuff audio trees and the V5 compressed-map decoding.

## flac / cdfl

CHD FLAC hunks are **headerless** (no STREAMINFO): a single marker byte (`'L'` = little-endian PCM, `'B'` = big-endian PCM) followed by raw FLAC frames. CHDSharp's custom decoder (`Flac/AudioDecoder.cs`) is a from-scratch C# FLAC decoder (derived from the CUETools.Flake lineage) that supports:

- 16-bit and 24-bit PCM;
- mono and all stereo channel modes — left/right, left-side, right-side, mid/side;
- subframe types: constant, verbatim, fixed (orders 0–4), LPC (orders 1–32, coefficient precision 1–15 bits, 32/64-bit accumulation);
- all standard block sizes plus 8/16-bit custom sizes;
- Rice residual coding (methods 0 and 1, partition order ≤ 8, escape codes);
- CRC-8 (frame header) and CRC-16 (frame) verification, enabled by default.

The marker byte selects endianness; CHDSharp byte-swaps when the stream is big-endian.

The **`cdfl`** variant uses FLAC (always byte-swapped) for sector data and zlib for subcode.

## zstd / cdzs

Zstandard via the managed `ZstdSharp.Port` package — no native code. Each hunk is a single-frame zstd block; the decompressed length must exactly equal `hunkbytes`. The **`cdzs`** variant uses zstd for both sector data and subcode with ECC/sync regeneration.

## avhu (A/V Huffman)

The laserdisc A/V codec — the one codec **libchdr 0.3.0 does not implement**. Each hunk is one video frame:

```
[0]    metasize (1 byte)      metadata payload size
[1]    channels (1 byte)      audio channel count (≤ 16)
[2-3]  samples (2 bytes)      audio samples per channel
[4-5]  width (2 bytes)        video width
[6-7]  height (2 bytes)       video height
[8-9]  audio huffman size     0xFFFF => FLAC audio, 0 => raw deltas, else tree size
[10..] per-channel compressed sizes (2 bytes each)
       metadata | audio trees | per-channel audio | video data
```

**Audio** is encoded one **mono** stream per channel:

- `0xFFFF` → each channel is a headerless **mono FLAC** stream (16-bit @ 48 kHz in MAME's encoder). CHDSharp configures its FLAC decoder as 16-bit mono and swaps to big-endian output, matching MAME's `flac_decoder::reset(48000, 1, ...)`.
- non-`0xFFFF` → two delta Huffman trees (hi/lo bytes) per hunk; samples are delta-decoded from a running previous sample.
- `0` → uncompressed 16-bit deltas.

**Video** is delta-RLE Huffman: the first byte must have bit `0x80` set (the only encoding AVHuff produces — lossless). Three delta-RLE Huffman contexts (Y, Cb, Cr) decode `width × height` bytes of 16-bit YUY2 (`Cb,Y,Cr,Y` order), with per-row RLE flushing.

> **History:** a stereo laserdisc bug (decoder configured with the header's channel count instead of mono) broke extraction of files like `dlair.chd` / `cubeqst.chd` with `Chderrdecompressionerror`; the fix and a stereo regression fixture (`v5_av_stereo.chd`) are in the repository.

---

## CD codec shared behavior

The CD variants (`cdzl`, `cdlz`, `cdzs`, `cdfl`) share the CD frame model:

```
CD frame = 2352 bytes sector data + 96 bytes subcode = 2448 bytes
```

| Codec | Sector stream | Subcode stream | ECC/sync regeneration |
|-------|---------------|----------------|------------------------|
| `cdzl` | zlib | zlib | ✅ |
| `cdlz` | lzma | zlib | ✅ |
| `cdzs` | zstd | zstd | ✅ |
| `cdfl` | flac | zlib | — (stored losslessly) |

ECC regeneration uses the standard CD-ROM Reed–Solomon P/Q parity computation (`CdRom.EccGenerate`).

---

## Error behavior

All codecs report failures as `ChdError` values:

- `Chderrinvaliddata` — structurally invalid compressed stream (bad sync, truncated, tree import failure, bitstream overflow).
- `Chderrdecompressionerror` — decompression produced the wrong length, CRC mismatch, or an unexpected exception.

`ChdFile.ReadHunk` catches exceptions from codec internals, logs the inner exception, and returns `Chderrdecompressionerror`, so corrupt hunks never surface as raw `InvalidDataException`s.
