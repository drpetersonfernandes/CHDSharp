# MissingFeatures — chdman.exe capabilities CHDSharp does not have

**Audited against:** MAME 0.288 `chdman` (`References/mame-mame0288/src/tools/chdman.cpp`),
command table at lines 82–99, on 2026-08-21.

**Bottom line:** parity is nearly total. `createld` is in progress (§1) with one video
encoding blocker remaining; `extractld` and `listtemplates` are the only commands not yet
started (§2–3); the only output-quality caveat is `cdzs` (see §4). Everything else — `info`,
`verify`(+`--fix`), `createraw`/`createhd`/`createcd`/`createdvd`, `copy`, delta children,
metadata (`addmeta`/`delmeta`/`dumpmeta`), `convertcue`, uncompressed `-c none`,
CUE/GDI/ISO/TOC/NRG input — is at or beyond chdman parity (parallel verify/encode, LRU cache,
memory-mapped reads, async APIs, platform detection, multi-hash output, fuzz hardening are
CHDSharp-only extras).

---

## 1. `createld` — laserdisc CHD creation from AVI (avhu encode)

| | |
|---|---|
| **What chdman does** | Reads an AVI file and encodes a V5 laserdisc CHD through MAME's `avhuff_encoder`: delta-RLE Huffman video + FLAC/delta audio per frame (`do_create_ld`, `chd_avi_compressor`, chdman.cpp ~3081+). |
| **CHDSharp status** | 🚧 **In progress** (2026-08-21). Most infrastructure is in place; the remaining blocker is the video encoding round-trip (see §1.1). Audio, metadata, CLI, and AVI reading are all working. |
| **What's been implemented** | See §1.2 below. |
| **Remaining work** | Fix the video bitstream encoding so CHDSharpLib's decoder accepts it (§1.1), then verify chdman byte-parity for real AVI files. |

### §1.1 — Blocker: video encoding does not round-trip through CHDSharpLib decoder

The AvHuffEncoder's video bitstream structure (header, trees, data) is correct — the tree
export/import round-trips pass, the 0x80 video marker is emitted, and the data section encodes
the right symbols. However, `DecodeVideo` in `CHDSharpReadersAVHuff.cs` fails at the final
`bitbuf.Flush() != buffInLength` check (line ~393).

**Root cause:** The encoder writes the video bitstream to a scratch buffer (`byte[width*height*2]`)
and then copies the actual encoded bytes to the dest buffer. MAME's `bitstream_out(dest, width*height*2)`
writes directly into the dest buffer with the full cap region as the stream size. The CHDSharpLib
decoder creates `BitStream(buffIn, buffInOffset, buffInLength)` where `buffInLength` = the video
portion length, and expects `Flush()` to return exactly that length. Because the encoder's actual
bitstream is smaller than `width*height*2`, the decoder's `Flush()` returns a smaller value.

**Fix needed (one of):**
1. Change `EncodeData` signature from `Span<byte> dest` to `byte[] dest`, then construct
   `new BitStreamOut(dest, dstOffs, videoRegionSize)` in `EncodeVideoLossless` to write directly
   into the dest buffer (matching MAME). Return `videoRegionSize` (the full cap) from
   `EncodeVideoLossless`, not the actual bitstream length.
2. Alternative: pad the video region to exactly `width*height*2` bytes after encoding, so the
   decoder's `buffInLength` matches the full region.

**Key files for this fix:**
- `CHDSharpEncoder/AvHuffEncoder.cs` — `EncodeVideoLossless` (line ~191), `EncodeData`
- `CHDSharpEncoder/AvHuffCodec.cs` — `Compress` method (calls `EncodeData`)
- `CHDSharpLib/CHDReadersAVHuff.cs` — `DecodeVideo` (line ~318), the `Flush()` check at ~393
- `CHDSharpLib/Utils/BitStream.cs` — decoder's `BitStream`, `Flush()` returns `_doffset - _initialOffset`
- `CHDSharpEncoder/BitStreamOut.cs` — encoder's `BitStreamOut`, `Flush()` returns `ByteLength`

**Verification:** After fixing, run:
- `AvHuffDebugTests.EncodeVideoOnlyFrame_RoundTripsThroughChdLib` — should pass
- `LaserDiscEncodeTests.SmallAvi_RoundTripsThroughChdReader` — should pass
- `LaserDiscEncodeTests.SmallAvi_MatchesChdmanByteForByte` — byte-parity vs chdman.exe

### §1.2 — What's been implemented (complete list)

| Component | File | Status |
|---|---|---|
| AvHuffEncoder (full MAME port) | `CHDSharpEncoder/AvHuffEncoder.cs` | ✅ AssembleData, EncodeData, DeltaRleEncoder, RleAndHistoBitmap, CodeToRleCount/RleCountToCode |
| AvHuffCodec (IChdCodec registration) | `CHDSharpEncoder/AvHuffCodec.cs` | ✅ CodecTags.Avhu, Compress/Decompress wired |
| HuffmanEncoder.ExportTreeRle | `CHDSharpEncoder/HuffmanEncoder.cs` | ✅ Port of MAME's `write_rle_tree_bits` + `export_tree_rle` (round-trip tests pass) |
| LibFlacEncoder mono+48kHz | `CHDSharpEncoder/Flac/LibFlacEncoder.cs` | ✅ Generalized with channels/sampleRate params; stereo/44100 path unchanged |
| AVI container reader | `CHDSharpEncoder/AviReader.cs` | ✅ RIFF/AVIX, hdrl, strl, movi, idx1 + fallback scan; YUY2/VYUY/UYVY + PCM 8/16-bit |
| VBI metadata parser | `CHDSharpEncoder/VbiParse.cs` | ✅ Port of vbiparse.cpp (Manchester code, white flag, ParseAll, MetadataPack) |
| AVAV/AVLD metadata builders | `CHDSharpEncoder/MetadataWriter.cs` | ✅ AvMetadataTag, AvLdMetadataTag, BuildAvMetadata, BuildAvLdMetadata |
| ChdCodec registration | `CHDSharpEncoder/ChdCodec.cs` | ✅ CodecTags.Avhu in CreateAll, ParseCodecTags, FromName, SupportedCodecNames |
| EncodeLaserDisc pipeline | `CHDSharpEncoder/ChdEncoder.cs` | ✅ Full createld layout (header→AVAV→data→map→AVLD), interlace detection, VBI capture, parent CHD support |
| CLI --createld | `CHDSharpCli/Program.cs` | ✅ Frame range (-isf/-if), options, VerifyResultChd |
| AvHuffDebugTests | `CHDSharpEncoderTest/AvHuffDebugTests.cs` | ✅ Diagnostic tests (tree round-trip, bitstream, video encode) |
| LaserDiscEncodeTests | `CHDSharpEncoderTest/LaserDiscEncodeTests.cs` | ✅ Integration tests (round-trip, metadata, interlace/VBI, frame range, chdman parity) |
| AviTestWriter | `CHDSharpEncoderTest/LaserDiscEncodeTests.cs` (embedded) | ✅ Synthetic AVI writer for tests |

**Known correct:** Huffman tree round-trips, FLAC mono encode, AVI parsing, metadata AVAV/AVLD
layout, EncodeLaserDisc frame loop, CLI wiring. The only open issue is the video bitstream
dest-buffer semantics described in §1.1.

## 2. `extractld` — laserdisc CHD → AVI file

| | |
|---|---|
| **What chdman does** | Decodes an avhu laserdisc CHD back into a playable **AVI file** (DIB video frames + PCM sound samples via MAME's `avi_file` writer; chdman.cpp:94, 554–602). |
| **CHDSharp status** | ❌ No AVI writer exists. The decode half is done: AVHuff decoding works and is regression-tested (mono + stereo fixtures, see `docs/testing.md`), but extraction currently stops at raw frame/sample data rather than a muxed `.avi`. |
| **Effort if ever wanted** | Moderate (~1 day): write a minimal AVI muxer (index-less DIB frames + PCM WAVEFORMATEX track) and wire it into `ExtractToDirectory` for `DiscPlatform`-laserdisc / avhu images. |

## 3. `listtemplates` + `createhd -tp <id>` — predefined HDD geometry templates

| | |
|---|---|
| **What chdman does** | `listtemplates` prints a built-in table of ~40 classic hard disks (manufacturer, model, cylinders/heads/sectors, sector size); `createhd -tp <id>` uses one as geometry and writes its GDDD metadata accordingly (`s_hd_templates`, chdman.cpp:918; template resolution at :1979–1985; info shows "Template: …"). |
| **CHDSharp status** | ❌ Not ported. GDDD metadata synthesis itself exists (`MetadataWriter.BuildHardDiskMetadata`), so only the table + CLI flag are missing. |
| **Effort if ever wanted** | Trivial (~1–2 h): copy the data-only table from chdman.cpp:918, add `--template <id>` to CLI `--create` and a `Templates()` listing; optionally expose `ChdEncoder.HardDiskTemplates`. |

---

## 4. Output-quality caveat (not a missing feature): `cdzs` bit-exactness

Since the 2026-08-21 pure-C# decision (ProposedFixes §7.2), `cdzs` encode uses the managed
ZstdSharp port: output is valid, passes `chdman verify`, deep `CheckFile`, and decodes
byte-identically — but whole-file bytes can differ from chdman's own output (managed zstd
trailing-byte finalization on CD-sized buffers). Raw `zstd` hunks at common hunk sizes remain
bit-identical. Reintroducing the libzstd P/Invoke (recipe in BattleTestResult sixth-run notes)
would restore bit-parity if ever required.

---

## Parity summary

| chdman command | CHDSharp | Where |
|---|---|---|
| `info` | ✅ | CLI `--info` (+ `Chd.ReadHeader`) |
| `verify` / `verify --fix` | ✅ | `Chd.CheckFile[AndRepair]` + CLI |
| `createraw` / `createhd` | ✅ | `EncodeRaw` (+ templates = gap #3) |
| `createcd` (all codecs) | ✅ | `EncodeCd` |
| `createdvd` | ✅ | CLI `--create -d` / `-c auto` |
| `createld` | 🚧 | gap #1 — in progress, video encoding blocker (§1.1) |
| `extractraw` / `extracthd` / `extractcd` / `extractdvd` | ✅ | `ExtractToDirectory` |
| `extractld` | ❌ | gap #2 |
| `copy` | ✅ | `ChdEncoder.Copy` |
| delta CHD (`-ip`) | ✅ | `ParentPath` / `-op` |
| `addmeta` / `delmeta` / `dumpmeta` | ✅ | `SetMetadata`/`DeleteMetadata`/`GetMetadata` + CLI |
| `convertcue` | ✅ | `CueConverter` |
| `listtemplates` | ❌ | gap #3 |
| uncompressed (`-c none`) | ✅ | byte-exact vs chdman |
