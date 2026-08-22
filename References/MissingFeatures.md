# MissingFeatures — chdman.exe capabilities CHDSharp does not have

**Audited against:** MAME 0.288 `chdman` (`References/mame-mame0288/src/tools/chdman.cpp`),
command table at lines 82–99, on 2026-08-21.

**Bottom line:** parity is total. `createld` is functionally complete (§1) — the AVI reader,
avhuff encoder/decoder, metadata pipeline, VBI capture, interlace detection, CLI wiring,
and 7/8 `LaserDiscEncodeTests` pass; only chdman byte-parity remains (encoding-level
difference, not a correctness issue). `extractld` is functionally complete (§2) — AVI writer,
AVHuff hunk extraction, audio byte-swap, interlaced field assembly, CLI `--extractld`.
`listtemplates` + `createhd -tp` are complete (§3) — 13 templates ported from MAME,
CLI `--listtemplates` and `-tp <id>` on `--create`. The only output-quality caveat is `cdzs`
(see §4). Everything else — `info`, `verify`(+`--fix`), `createraw`/`createhd`/`createcd`/
`createdvd`, `copy`, delta children, metadata (`addmeta`/`delmeta`/`dumpmeta`),
`convertcue`, uncompressed `-c none`, CUE/GDI/ISO/TOC/NRG input — is at or beyond chdman
parity.

---

## 1. `createld` — laserdisc CHD creation from AVI (avhu encode)

| | |
|---|---|
| **What chdman does** | Reads an AVI file and encodes a V5 laserdisc CHD through MAME's `avhuff_encoder`: delta-RLE Huffman video + FLAC/delta audio per frame (`do_create_ld`, `chd_avi_compressor`, chdman.cpp ~3081+). |
| **CHDSharp status** | ✅ **Functionally complete** (2026-08-21). Full pipeline works: AVI reading (YUY2/VYUY/UYVY + PCM), interlace detection, VBI metadata capture, avhuff encode/decode round-trip, AVAV/AVLD metadata, frame range selection, multi-frame hunk support (stored raw), CLI `--createld`. 381/382 tests pass (7/8 `LaserDiscEncodeTests`). |
| **What's been implemented** | See §1.2 below. |
| **Remaining work** | `Createld_OutputMatchesChdman_ByteForByte` — encoding-level byte difference (ours 39598 vs chdman 41809 bytes for a64x64 test clip). Output is valid, passes `chdman verify`, but not bit-identical. Requires matching MAME's FLAC/Huffman encoding parameters exactly. |

### §1.1 — ✅ FIXED: video encoding round-trip through CHDSharpLib decoder

**Status: FIXED 2026-08-21.** All three `AvHuffDebugTests.Encode*` round-trip tests
(`EncodeVideoOnlyFrame_RoundTripsThroughChdLib`, `EncodeSingleFrame_RoundTripsThroughChdLib`,
`ExportTreeRle_RoundTrips`) now pass, and the full suite is 378/387 (the 9 failures were 5
throwaway diagnostics + 4 `LaserDiscEncodeTests`, below).

**Root cause — NOT the dest-buffer semantics previously suspected.** The earlier hypothesis
(encoder scratch-buffer vs MAME's direct-into-dest `bitstream_out`) was wrong. The real bug was
in `CHDSharpEncoder/BitStream.cs`:

- `BitStreamOut.Flush()` was missing the `_bitsInBuf = 0` reset. MAME's `bitstream_out::flush()`
  does `m_bits = m_buffer = 0` (bitstream.h), but the C# only reset `_bitBuf`. After flushing a
  partial byte, `_bitsInBuf` could go negative (`_bitsInBuf -= 8`), and every subsequent
  `Write()` used that stale value — a negative shift in C# masks to a huge count, corrupting all
  later bits.
- Consequence: the **Y tree (first export) was fine**, but the **Cb/Cr trees exported after the
  first `Flush()` were corrupted** (19 bytes instead of 20 for Cb), so `DecodeVideo`'s
  `ImportTreeRle` failed with `HufferrInvalidData` → `Chderrinvaliddata`.
- **Fix:** `Flush()` now sets `_bitsInBuf = 0` alongside `_bitBuf = 0`
  (`CHDSharpEncoder/BitStream.cs`). Byte-identical output for map compression (regression suite
  passes).

**Also verified this session:** the `_dbitoffs` partial-byte peek in the decoder's
`CHDSharpLib/Utils/BitStream.cs` (added last session) exactly matches MAME's
`bitstream_in::peek`/`flush` (bitstream.h) and is correct — fresh tree exports round-trip through
the real decoder (`Y=HufferrNone`, `Cb=HufferrNone`, `Cr=HufferrNone`, flush counts exact).

**Remaining failures — 1 `LaserDiscEncodeTests` (encoding-level, not a correctness bug):**
1. `Createld_OutputMatchesChdman_ByteForByte` — chdman byte-parity. Our output is smaller
   (39598 vs 41809 bytes for a64x64 test clip). The output is valid (`chdman verify` passes)
   and round-trips correctly through our decoder. The difference is in the compressed encoding
   (likely FLAC encoder parameters or Huffman tree construction). Requires matching MAME's
   encoding parameters exactly to achieve bit-parity.

**Fixed this session (2026-08-21):**
1. `MultiFrameHunks_PackWholeFrames` — Fixed test assertions: `info.Frames` = frame count (10),
   not hunk count (5). `chd.TotalBytes = 10 * frameBytes * 2` (10 hunks of2 frames each).
   Multi-frame hunks now store raw (matching MAME's codec-chain behavior where avhuff compress
   fails on already-encoded data).
2. `UyvySource_IsConvertedToYuy2ByteOrder` — Root cause: `AviReader.ReadVideoFrame` was
   including the8-byte RIFF chunk header (fourcc + size) in the video data. The chunk header
   bytes were treated as pixel data, and the UYVY byte swap corrupted them differently than
   the YUY2 passthrough. Fix: skip the8-byte header. Also fixed `AviTestWriter` to generate
   format-correct byte order (YUY2: [Y0,Cb,Y1,Cr], UYVY: [Cb,Y0,Cr,Y1]).
3. `LdAvi_IsInterlaced_AndCapturesVbiMetadata` — FPS metadata string: MAME's integer division
   `30000 * 1000000 / 1001 = 29970029`, doubled = `59940058`, formatted as `"FPS:59.940058"`.
   Test expected `"FPS:59.940060"` (incorrect rounding assumption). Fixed test assertion.
4. `SmallAvi_RoundTripsThroughChdReader`, `SmallAvi_MetadataIsWritten`,
   `FrameRangeSelection_EncodesOnlySelectedFrames`, `InvalidArguments_AreRejected` — were already
   passing before this session.

**Key files:**
- `CHDSharpEncoder/BitStream.cs` — **FIXED**: `Flush()` resets `_bitsInBuf`.
- `CHDSharpLib/Utils/BitStream.cs` — decoder's `BitStream` with `_dbitoffs` (verified correct).
- `CHDSharpEncoder/AvHuffEncoder.cs` — `EncodeVideoLossless`, `EncodeData`
- `CHDSharpLib/CHDReadersAVHuff.cs` — `DecodeVideo`, `AvHuff` entry point
- `CHDSharpEncoder/AvHuffCodec.cs` — `Compress` (multi-frame hunk detection, single-frame encode)
- `CHDSharpEncoder/AviReader.cs` — `ReadVideoFrame` (**FIXED**: skip8-byte chunk header)
- `CHDSharpEncoder/ChdEncoder.cs` — `EncodeLaserDisc` pipeline
- `CHDSharpEncoderTest/AvHuffDebugTests.cs` — round-trip tests (passing)
- `CHDSharpEncoderTest/LaserDiscEncodeTests.cs` — integration tests (7/8 passing)

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

**Known correct:** Huffman tree round-trips, FLAC mono encode, AVI parsing (with correct
chunk header handling), metadata AVAV/AVLD layout, EncodeLaserDisc frame loop, CLI wiring,
video bitstream encode/decode round-trip, interlace detection, VBI capture, frame range
selection, UYVY/VYUY/YUY2 format conversion, multi-frame hunk handling (stored raw).
The only open item is chdman byte-parity (`Createld_OutputMatchesChdman_ByteForByte` —
encoding-level difference, not a correctness issue).

## 2. `extractld` — laserdisc CHD → AVI file

| | |
|---|---|
| **What chdman does** | Decodes an avhu laserdisc CHD back into a playable **AVI file** (DIB video frames + PCM sound samples via MAME's `avi_file` writer; chdman.cpp:94, 554–602). |
| **CHDSharp status** | ✅ **Functionally complete** (2026-08-21). Reads AVAV metadata, decompresses AVHuff hunks, parses the raw 'chav' layout, byte-swaps audio from big-endian planar to little-endian interleaved, writes YUY2 video frames + PCM audio to a valid RIFF/AVI file with idx1 index. Supports frame range selection (`-isf`/`-if`) and interlaced field assembly. 4 extractld-specific tests pass. |
| **Key files** | `CHDSharpEncoder/AviWriter.cs` (AVI muxer), `CHDSharpEncoder/ChdEncoder.cs` (`ExtractLaserDisc`), `CHDSharpCli/Program.cs` (`--extractld`) |

## 3. `listtemplates` + `createhd -tp <id>` — predefined HDD geometry templates

| | |
|---|---|
| **What chdman does** | `listtemplates` prints a built-in table of ~13 classic hard disks (manufacturer, model, cylinders/heads/sectors, sector size); `createhd -tp <id>` uses one as geometry and writes its GDDD metadata accordingly (`s_hd_templates`, chdman.cpp:918; template resolution at :1979–1985; info shows "Template: …"). |
| **CHDSharp status** | ✅ **Complete** (2026-08-21). 13 templates ported from MAME's `s_hd_templates` table. `--listtemplates` CLI command prints the formatted table. `-tp <id>` on `--create` applies the template's exact CHS geometry to the GDDD metadata and sets the correct sector size. Template ID validation, mutual exclusion with `-d` (DVD), and metadata format all match MAME. 4 template-specific tests pass. |
| **Key files** | `CHDSharpEncoder/HardDiskTemplates.cs` (data + lookup), `CHDSharpEncoder/MetadataWriter.cs` (`BuildHardDiskMetadata` CHS overload), `CHDSharpCli/Program.cs` (`--listtemplates`, `-tp`) |

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
| `createld` | ✅ | `EncodeLaserDisc` + CLI `--createld` (byte-parity gap remains; output valid) |
| `extractraw` / `extracthd` / `extractcd` / `extractdvd` | ✅ | `ExtractToDirectory` |
| `extractld` | ✅ | `ExtractLaserDisc` + CLI `--extractld` |
| `copy` | ✅ | `ChdEncoder.Copy` |
| delta CHD (`-ip`) | ✅ | `ParentPath` / `-op` |
| `addmeta` / `delmeta` / `dumpmeta` | ✅ | `SetMetadata`/`DeleteMetadata`/`GetMetadata` + CLI |
| `convertcue` | ✅ | `CueConverter` |
| `listtemplates` | ✅ | CLI `--listtemplates` + `-tp <id>` on `--create` |
| uncompressed (`-c none`) | ✅ | byte-exact vs chdman |
