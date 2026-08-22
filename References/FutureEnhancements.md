# Future Enhancements

Ordered by importance, driven by the [libchdr issue tracker](https://github.com/rtissera/libchdr/issues)
(open and closed), by what consumers of a CHD decoding library actually ask for,
and by feature parity comparison against [chd-rs](https://github.com/SnowflakePowered/chd-rs),
[CHDlite](https://github.com/rtissera/CHDlite), and MAME 0.288 chdman.
Optional enhancements are at the bottom.

Legend — issue references: `#NN` = [rtissera/libchdr issue](https://github.com/rtissera/libchdr/issues/NN),
`crs#NN` = [SnowflakePowered/chd-rs issue](https://github.com/SnowflakePowered/chd-rs/issues/NN).

---

## Tier 1 — Correctness & Hardening

---

### 1. Bounds Checks & Validation

| Field | Value |
|-------|-------|
| **Missing Feature** | Several locations lack bounds checks that could cause crashes or data corruption on crafted CHD files. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Fix the following: (1) `CHDFile.cs:560` `LinkSelfBlocks` — add `if (me.Offset >= chd.Map.Length) return ChdError.Chderrinvaliddata`. (2) `CHDBlockRead.cs:191` `GetReaderFromCodec` — throw `NotSupportedException` or return a lambda returning `ChdError.Chderrcodecerror` instead of `null!`. (3) `CHDHeaders.cs:226` — validate V5 codec values against known enum members before casting. (4) `CHDReaders.cs:146-174` FLAC — check `srcPos < buffInLength` before each `DecodeFrame` call. (5) `CHDHeaders.cs:24-34` V1/V2 — validate `Blocksize > 0` and `Totalblocks * Blocksize` doesn't overflow. |
| **Estimated Time** | 3–4 hours |

---

### 2. Compressed Hunk Larger Than Output Bounds (#118)

| Field | Value |
|-------|-------|
| **Missing Feature** | libchdr assumed a compressed hunk always fits inside an uncompressed hunk. That is wrong for valid CHDs created with low compression levels (codec headers/footers can push the compressed size over hunkbytes) and trivially wrong for malicious CHDs — the compressed length is attacker-controlled data from the hunk map. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Audit the C# read path (`ChdFile.ReadHunk` → `ChdBlockRead.ReadBlock`): the compressed buffer (`BuffIn`) is already allocated from the map's `Length` rather than `Blocksize`, which sidesteps the C buffer overflow, but add explicit validation that (a) `Length` does not exceed a sane cap (e.g. `Blocksize * 2` or a configurable maximum) to prevent OOM on malicious files, and (b) codec readers never assume `buffInLength <= buffOutLength`. Add corpus/malformed-file tests with compressed hunks larger than `Blocksize`. |
| **Implemented As** | `ChdHeader.MaxCompressedBlockCap` (default = `Blocksize * 2`, see `ChdHeaders.DefaultMaxCompressedMultiple`), normalized in `ValidateSizeLimits` and on `ChdFile.MaxCompressedBlockBytes` (public, floored at `HunkBytes`). `ReadHunk` and `DecompressDataParallel` reject a hunk whose on-disk `Length` exceeds the cap with `Chderrinvaliddata` before any allocation; the parallel path's input `ArrayPool` is sized by the cap (was `Blocksize`) so a compressed hunk larger than the hunk can no longer overflow the input buffer. Codec readers already take `buffInLength`/`buffOutLength` independently and their internal loops guard `buffInLength`, so they do not assume `buffInLength <= buffOutLength`. Tests in `BoundsValidationTests` cover the cap defaults/normalization, `ReadHunk` rejecting an oversized claimed length, a valid zlib hunk whose compressed size exceeds `HunkBytes` but is under the cap decoding correctly, and the parallel verification path rejecting an oversized compressed hunk. |
| **Estimated Time** | 2–3 hours |

---

### 3. Typed Exception Handling

| Field | Value |
|-------|-------|
| **Missing Feature** | Multiple bare `catch` blocks swallow all exceptions including `OutOfMemoryException`, `ThreadAbortException`, etc. Locations: `CHDFile.cs:222` (metadata), `CHDFile.cs:452` (file open), `CHD.cs:336,496,546,596` (verification), `CHDReaders.cs:58` (Zstd). |
| **Implementation Status** | Finished |
| **Proposed Logic** | Replace bare `catch` with `catch (Exception ex)` and log the exception. In `CHDFile.cs:222` (metadata), catch `IOException` and `InvalidDataException` specifically; log and return empty list. In `CHDFile.cs:452` (file open), catch `IOException`, `UnauthorizedAccessException`, `FileNotFoundException` specifically; return appropriate `ChdError`. In `CHD.cs` verification, catch `OperationCanceledException` separately (for cancellation support). In `CHDReaders.cs:58`, catch `ZstdException` specifically. |
| **Estimated Time** | 2–3 hours |

---

### 4. IDisposable on ChdCodecState

| Field | Value |
|-------|-------|
| **Missing Feature** | `ChdCodecState` holds `ZstdSharp.Decompressor` (implements `IDisposable`) and `FlacAudioDecoder` (may hold resources) but does not implement `IDisposable` itself. These resources are leaked when `ChdFile` is disposed. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Make `ChdCodecState` implement `IDisposable`. In `Dispose()`, dispose `BZstd` (ZstdSharp.Decompressor), `FlacAudioDecoder` (if disposable), and any other held disposables. In `ChdFile.Dispose()`, call `_codec.Dispose()`. |
| **Estimated Time** | 1 hour |

---

### 5. Deflate Decoder Infinite-Loop Guard (#168)

| Field | Value |
|-------|-------|
| **Missing Feature** | libchdr's bundled miniz 3.1.1 has an infinite-loop vulnerability in `tinfl_decompress` when `code_len==0` is reached during Huffman decoding (fixed in miniz 3.1.2). A crafted zlib/deflate hunk can hang `chd_read` indefinitely — denial of service via untrusted CHD input. CHDSharp's vendored zlib 1.3.1 port (`CHDSharpEncoder/ZLib/`) and the managed ZstdSharp decoder should be audited for the same class of bug. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | (1) Audit the vendored zlib 1.3.1 port's inflate path (`CHDSharpEncoder/ZLib/`) for the `code_len==0` guard in the Huffman decode loop — add `if (code_len == 0) return Z_DATA_ERROR` if missing. (2) Audit ZstdSharp's managed inflate for equivalent bounds. (3) Add a fuzz test that feeds crafted deflate streams to `ChdFile.Open` + `ReadHunk` and asserts no hang (timeout-based). (4) Verify the encoder's deflate output does not trigger the bug in other decoders. |
| **Estimated Time** | 2–3 hours |

---

### 6. Bounded Metadata String Parsing (#165)

| Field | Value |
|-------|-------|
| **Missing Feature** | libchdr's C metadata format strings (`CDROM_TRACK_METADATA_FORMAT`, `GDROM_TRACK_METADATA_FORMAT`) use unbounded `%s` in `sscanf`, allowing buffer overflows from crafted metadata payloads. Downstream emulators (Flycast, Mednafen) copy these format strings verbatim. CHDSharp's C# parsing (`int.Parse`, `string.Split`) is inherently safer but should still validate field lengths and reject malformed metadata gracefully rather than allocating huge strings. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | (1) In `ChdTocParser`, cap the length of parsed string fields (TYPE, SUBTYPE, PGTYPE, PGSUB) to 15 characters (matching the proposed `%15s` fix in #165). Reject metadata entries whose text payload contains null bytes or exceeds a sane maximum (e.g. 4 KiB). (2) In `ChdFile.GetMetadata`, reject entries whose `length` field is implausibly large (> 64 KiB). (3) Add corpus tests with crafted oversized metadata fields. |
| **Estimated Time** | 1–2 hours |

---

### 7. Header Struct Alignment (#66)

| Field | Value |
|-------|-------|
| **Missing Feature** | libchdr's C `chd_header` struct has platform-dependent alignment (200 bytes with MSVC, 196 without `#pragma pack`), causing interop issues for Delphi/D consumers. |
| **Implementation Status** | **Not applicable.** CHDSharp reads/writes every header field individually via `BigEndianReader`/`BigEndianWriter` — no struct marshaling, no padding, no alignment issues. The file format is byte-level big-endian, not a C struct dump. |
| **Estimated Time** | 0 |

---

## Tier 2 — Decode Coverage

---

### 8. 2ND_COMPRESSED Map Entry Type (V3/V4 Type 6)

| Field | Value |
|-------|-------|
| **Missing Feature** | Map entry type 6 — a secondary compression algorithm for a hunk (e.g., FLAC for CDDA tracks in V3/V4 CHDs). |
| **Implementation Status** | Finished |
| **Proposed Logic** | In `ChdCommon.ConvMapEntryFlagtoCompressionType()`, handle the type-6 flag. Map it to a new `CompressionType.Compressiontype2nd` enum value. In `ChdBlockRead.ReadBlock()`, resolve type-2nd hunks by attempting the primary codec first; if the hunk is audio (from track metadata), fall back to the secondary codec (FLAC). This requires storing a secondary codec reference on the `MapEntry` or header. Validate against libchdr's `2ND_COMPRESSED` handling in `libchdr_chd.c`. |
| **Estimated Time** | 4–6 hours |

---

### 9. ZLIB_PLUS Codec (V1-V4 Compression Type 2)

| Field | Value |
|-------|-------|
| **Missing Feature** | `CHDCOMPRESSION_ZLIB_PLUS` — a legacy V1-V4 compression variant that uses zlib with an extended header/metadata block. |
| **Implementation Status** | Finished |
| **Proposed Logic** | In `ChdCommon.CompTypeConv()`, the value 2 is already mapped (verify it routes to `ChdCodec.Zlib` or a new `ChdCodec.ZlibPlus`). Investigate libchdr's `codec_zlib` init path — zlib-plus may prepend a custom header before the deflate stream. If the only difference is a header skip, adjust the `Zlib()` reader in `ChdReaders` to detect the plus variant and skip the extra bytes. If it uses a different decompression window, create a `ZlibPlus()` reader. Test against any V1-V4 zlib-plus CHD corpus files. |
| **Estimated Time** | 3–4 hours |

---

### 10. GDROMLE Flag — Little-Endian GD-ROM Audio

| Field | Value |
|-------|-------|
| **Missing Feature** | `CD_FLAG_GDROMLE` (0x02) — indicates a GD-ROM whose CDDA audio is stored in little-endian byte order (Sega CD, PCEngine CD). |
| **Implementation Status** | Finished |
| **Proposed Logic** | In `ChdTocParser`, detect the `GDROMLE` flag in GD-ROM metadata. Store a `bool IsLittleEndianAudio` on `ChdTrackInfo` (or a CHD-level flag). In the FLAC / CD+FLAC codec path, pass `swap_endian: true` to the FLAC decoder when this flag is set. In `ChdReaders.Cdflac()`, the existing `flac_decoder_decode_interleaved` already accepts a `swap_endian` parameter — wire it through. |
| **Implemented As** | **Note: applied at the track-extraction layer (MAME parity), not in the codec.** `ChdFile.IsLittleEndianAudio` detects the legacy `CHGT` ("CHT2"-style old GD-ROM) metadata tag (`CD_FLAG_GDROMLE`). During GD-ROM extraction, `TryWriteTrackToFile` byte-swaps the 2352-byte sector-data portion of each 2448-byte frame only for `AUDIO` tracks, leaving subcode intact (matches MAME `cdrom.cpp:402`). Raw `Read()`/hashing/verification output is unchanged. Helper: `ChdReaders.SwapCdda16`. |
| **Estimated Time** | 2–3 hours |

---

## Tier 3 — User-Requested Headline Features (from open issues)

---

### 11. Multi-Hunk LRU Cache (#36)

| Field | Value |
|-------|-------|
| **Missing Feature** | `ChdFile` caches only a single hunk (`_cachedHunk`). Random reads touching multiple hunks in quick succession re-decompress each one. No configurable multi-hunk cache exists. Requested in libchdr #36 (kcgen): returning already-decompressed data immediately instead of burning another decompression round. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add `ChdFile.CacheSize` property (default 1, backward compatible). When >1, maintain a `Dictionary<uint, byte[]>` of cached hunks with LRU eviction (use `LinkedList<uint>` for access order). In `ReadHunk`, check cache first; on miss, decompress and insert; on hit, promote to front. Cap memory at `CacheSize * HunkBytes`. Expose `ChdFile.ConfigureCache(int maxHunks)` method. The existing `KeepMostRepeatedBlocks` in verification is a separate concern (verification-time caching vs runtime read caching). |
| **Implemented As** | Added `ChdFile.CacheSize` (default 1, backward compatible) and `ChdFile.ConfigureCache(int maxHunks)` (values `<= 1` disable the multi-hunk cache; reducing capacity evicts least-recently-used entries). A `Dictionary<uint, LinkedListNode<CachedHunk>>` + `LinkedList<CachedHunk>` implements LRU eviction with promote-on-hit; memory is capped at `CacheSize * HunkBytes`. `ReadHunk` (including parent-referenced hunks) checks the cache first, serves hits by copying into the caller's buffer, and inserts freshly decompressed hunks — evicting the oldest when over capacity. Default of 1 preserves the previous single-slot behaviour (`_cachedHunk`). Tests in `LruCacheTests` cover the default, lower-bounding, per-hunk correctness/independence, eviction + promotion order, and reconfiguration. `KeepMostRepeatedBlocks` remains separate (verification-time caching). |
| **Estimated Time** | 4–5 hours |

---

### 12. Sidecar Meta File Support (#164)

| Field | Value |
|-------|-------|
| **Missing Feature** | External "sidecar" metadata files alongside `.chd` images to carry extended CD metadata (multi-index tracks, CATALOG, ISRC, raw CD subchannel data such as Libcrypt/SBI/subcode) that CHD format cannot natively store. Requested in libchdr #164 with concrete breakage reports: Saturn/Sega CD games rely on multi-index timing and subcode data that CHD V5 discards; CATALOG/ISRC are stripped during conversion. A tiny sidecar file fixes it without re-compressing terabyte-scale collections. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | Define a `ChdSidecar` class that loads an XML or JSON sidecar file (e.g. `.chd.xml`) describing extended track metadata, subchannel patches, and supplemental info. In `ChdFile.Open()`, probe for a sibling file with a known extension. In `ChdTocParser`, merge sidecar data into the parsed track list — overriding pregap, appending subchannel patches, injecting CATALOG/ISRC fields. In `ExtractToDirectory()`, write the sidecar alongside the extracted BIN/CUE. Define a schema (XSD or JSON Schema) for the sidecar format. Coordinate with MAME/RetroArch community on a standard sidecar format if one does not yet exist. |
| **Estimated Time** | 8–12 hours |

---

### 13. MSF/LBA Conversion + Sector-Address Read API (#155)

| Field | Value |
|-------|-------|
| **Missing Feature** | libchdr #155 (maintainer): expose a higher-level API allowing sector reads by LBA or MSF addressing, instead of raw byte offsets. |
| **Implementation Status** | Finished |
| **Proposed Logic** | (1) Add a static `CdRomAddress` class (or extend `CdRom`) with: `MsfToLba(byte m, byte s, byte f)` → `int` (unpack BCD, compute `(m*60 + s)*75 + f - 150`). `LbaToMsf(int lba)` → `(byte m, byte s, byte f)` (add 150, decompose, pack BCD). `LbaToMsfAlt(int lba)` → same but without the 150 pregap offset (for Sega CD / PCEngine). All three are pure math, no dependencies. (2) Add `ChdFile.ReadSector(uint lba, byte[] buffer)` / `ReadSectorMsf(...)` that maps the address to a `(track, frame offset)` via `Tracks`/`StartFrame` and reads the 2352-byte sector (or full 2448-byte frame) from the decompressed image. |
| **Implemented As** | `CdRomAddress` (public static, `CHDSharpLib/Utils/CdRomAddress.cs`): `MsfToLba`/`LbaToMsf` (BCD, ±150 lead-in), `MsfToLbaAlt`/`LbaToMsfAlt` (BCD, no lead-in — Sega CD / PC Engine), constants `FramesPerSecond`/`SecondsPerMinute`/`PregapFrames`; invalid BCD nibbles and minutes > 99 (BCD limit) throw `ArgumentOutOfRangeException`. `ChdFile.ReadSector(uint lba, byte[] buffer, ct)` reads the 2352-byte sector data (zero-padded tail for sub-2352 data sizes, as stored), `ReadSectorMsf(m, s, f, buffer, ct)` the BCD-MSF variant (addresses before 00:02:00 rejected), and `ReadFrame(uint lba, byte[] buffer, ct)` the full 2448-byte frame incl. subcode. LBA→image mapping derived from the track table: image frame = `lba + (PreGapDataSize > 0 ? tracks[0].PreGap : 0)` — the pregap is physically in the image only when the metadata carries the `PGTYPE:V...` data prefix (CUE INDEX 00/01, chdman parity), otherwise the image begins at track 1's INDEX 01 (Redump-style CUEs, PREGAP-keyword CUEs, NRG, TOC, GDI). Non-CD/GD-ROM images (no track metadata) return `Chderrinvaliddata`; bad buffers/out-of-range addresses return `Chderrinvalidparameter`. Tests in `CdRomAddressTests` (BCD vectors, lead-in boundaries, 99-minute BCD limit, invalid-BCD throws, round trips) and `ReadSectorTests` (sector reads match the decompressed image, all-1000-frames concatenation equals the whole image, MSF↔LBA equivalence, error paths, and V3/V4/V5 CD corpus coverage incl. cdlz/cdfl codecs). |
| **Estimated Time** | 2–3 hours |

---

### 14. Threaded Read-Ahead Decompression (#34)

| Field | Value |
|-------|-------|
| **Missing Feature** | Background worker threads that predict and pre-decompress upcoming hunks, eliminating I/O stalls during sequential reads and audio/video streaming. Requested in libchdr #34 (kcgen): threaded so latency isn't added to the current read, read-ahead distance configurable in KiB, `-1` = decode everything ahead. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | Add an optional `ReadAhead` mode to `ChdFile`. When enabled, after each `ReadHunk()`, a background `Task` pre-decompresses the next N hunks (configurable, default 4) into a ring buffer keyed by hunk index. Use `SemaphoreSlim` to cap memory usage. `ReadHunk()` checks the ring buffer first — on hit, swap the buffer in; on miss, decompress synchronously as today. Expose via `ChdFile.EnableReadAhead(int lookAhead = 4)`. The existing `_cachedHunk` single-slot cache remains as the L1; the ring buffer acts as L2. For `EnumerateHunks()`, use a `Channel<byte[]>` to bridge the background producer and the caller's consumer. Wire into the parallel verification pipeline in `Chd.CheckFile()` for consistent behavior. Consider implementing #8 (LRU cache) first — it covers most of the same reuse win with less complexity. |
| **Estimated Time** | 6–8 hours |

---

### 15. Xdelta/PPF Patch → Child CHD (#77)

| Field | Value |
|-------|-------|
| **Missing Feature** | libchdr #77 (i30817): apply an xdelta (or PPF) patch directly to a parent CHD, producing a child CHD without decompressing/recompressing the parent. Xdelta is the standard ROM-hack distribution format for CD-based games (Saturn, PS1, Dreamcast); users currently must extract the parent CHD → BIN/CUE, apply the patch, then re-create the CHD — a multi-step, multi-GB workflow that could be a single command. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | (1) Add `ChdEncoder.ApplyPatch(parentChdPath, patchPath, outputChdPath)` that reads the parent CHD, applies a binary patch (xdelta or PPF) at the byte level, and writes the result as a delta child CHD (using existing `COMPRESSION_PARENT` references for unchanged hunks). (2) Xdelta format: parse the VCDIFF header (RFC 3284) to get the target window instructions, read only the affected parent hunks, apply the copy/insert/run instructions, and compress the modified hunks as `COMPRESSION_TYPE_0` while unchanged hunks become `COMPRESSION_PARENT`. (3) PPF format: simpler — parse the PPF patch header (offset + data pairs), apply to the relevant parent hunks, re-compress only those hunks. (4) Auto-detect format from patch file magic (`VCDM` for xdelta, `PPF` for PPF). (5) CLI: `--applypatch <parent.chd> <patch.xdelta> <output.chd>`. (6) Checksum verification: both formats include source/target checksums — validate the parent's SHA-1 matches the patch's expected source before applying. |
| **Estimated Time** | 8–12 hours |

---

## Tier 4 — Large-File Support

---

### 16. Sources Larger Than 10 GB (#147)

| Field | Value |
|-------|-------|
| **Missing Feature** | libchdr #147 (closed by PR #153): CHDs whose source image exceeds ~10 GB (e.g. PS3 ISOs) failed to open in libchdr due to 32-bit offset assumptions. The C# port uses `long`/`ulong` offsets throughout, so this is expected to work — but it is unverified, and two APIs are hard-capped at 2 GiB: `ReadAllBytes` (guards `TotalBytes > int.MaxValue`) and `Precache` (guards `length > int.MaxValue`). |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add a >10 GB regression test (generate a sparse/large child or synthetic V5 CHD with `TotalBytes` > 4 GiB; open, `Read` at offsets past 4 GiB, verify round-trip). Audit all `int` casts on offsets (`ReadHunk`, `Read`, `ReadParentHunk`, extraction writers). For `ReadAllBytes`, document the 2 GiB limitation or add an `ExtractToFile` path that streams; for `Precache`, keep the 2 GiB cap but document it. |
| **Implemented As** | Audited all offset casts across `ReadHunk`, `Read`, `ReadParentHunk`, `WriteAllBytesSlow`/`TryWriteTrackToFile`, `Precache`, and `ReadAllBytes`: the C# port is already 64-bit safe — stream `Seek` uses `long`, hunk indices are `uint` bounded by `Totalblocks`, and the remaining `(int)` casts are hunk-scoped (bounded by `HunkBytes`). No truncation bugs required fixing. The 2 GiB caps on `ReadAllBytes`/`Precache` remain by design and are documented. Added `LargeFileTests` (net8/9/10): a synthetic uncompressed V5 CHD declaring a 20 GiB image (~2 MiB on disk, one stored hunk at a 5 GiB logical offset, remainder zero hunks) covering size reporting, random access past 4 GiB (stored-pattern round-trip and zero-hunk reads), cross-read consistency, and the `ReadAllBytes` 2 GiB guard. Full suite 488/488. |
| **Estimated Time** | 2–3 hours |

---

## Tier 5 — API Usability & Code Quality

---

### 17. CancellationToken Support on All Public APIs

| Field | Value |
|-------|-------|
| **Missing Feature** | No public API accepts a `CancellationToken`. Long-running operations (CheckFile, ReadAllBytes, ExtractToDirectory, OpenAsync, ReadAsync) cannot be cancelled by the caller. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add `CancellationToken ct = default` as the last parameter to: `ChdFile.Open/OpenAsync` (all overloads), `ChdFile.Read/ReadAsync/ReadHunk/ReadHunkAsync/ReadAllBytes/ExtractToDirectory`, `Chd.CheckFile/CheckFileWithParent`. In `DecompressDataParallel`, pass `ct` to the internal `CancellationTokenSource.CreateLinkedTokenSource(ct)`. In `ReadHunk`, check `ct.ThrowIfCancellationRequested()` before decompression. In `ExtractToDirectory`, check between hunk writes. This is a non-breaking change since the parameter has a default value. |
| **Implemented As** | `CancellationToken` added as the last parameter (default `default`) to every `ChdFile.Open`/`OpenAsync` overload, `ReadHunk`/`ReadHunkAsync`, `Read`/`ReadAsync`, `ReadAllBytes`, `ExtractToDirectory`/`ExtractToDirectoryWithReporting`, and both `Chd.CheckFile`/`CheckFileWithParent` overloads. Cancellation throws `OperationCanceledException` (`.NET`-idiomatic; async variants surface it as a cancelled task via `Task.Run(..., token)`). `DecompressDataParallel` now uses `CancellationTokenSource.CreateLinkedTokenSource(ct)` so caller cancellation stops the producer/workers/hasher; after the pipeline drains, the caller token is re-checked so a cancelled deep check throws OCE instead of reporting a bogus partial-hash mismatch. `ExtractToDirectoryWithReporting`/`TryWriteTrackToFile` rethrow OCE so cancellation is never swallowed into an error result. Checks are placed per hunk/chunk (zero overhead when not cancelled — just an `IsCancellationRequested` probe). Tests in `CancellationTokenTests` cover pre-cancelled throws for every method, cancelled-task async twins, mid-run cancellation of the parallel pipeline via the progress hook, and backward-compatible calls without the token. |
| **Estimated Time** | 4–6 hours |

---

### 18. Decompressed Image Stream Wrapper

| Field | Value |
|-------|-------|
| **Missing Feature** | No `Stream`-like object wrapping the decompressed CHD image. Consumers expecting a `Stream` (e.g., piping to other tools, computing hashes, feeding decoders) must manually loop over hunks. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | Add `ChdFile.OpenAsStream()` returning a `ChdImageStream : Stream` class. It wraps `ChdFile.Read()` with `CanSeek=true`, `CanRead=true`, `CanWrite=false`. `Seek` updates an internal `_position`. `Read` delegates to `ChdFile.Read(_position, buffer, 0, count)` and advances position. `Length` returns `TotalBytes`. Dispose disposes the parent `ChdFile`. Optionally support `ReadAsync` via `ChdFile.ReadAsync`. |
| **Estimated Time** | 3–4 hours |

---

### 19. Span&lt;byte&gt; / Memory&lt;byte&gt; Read Overloads

| Field | Value |
|-------|-------|
| **Missing Feature** | `ReadHunk` and `Read` accept only `byte[]`. Callers using `stackalloc`, `ArrayPool`, or `Memory<byte>` must allocate a new array to use these APIs. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | Add overloads: `ReadHunk(uint hunknum, Span<byte> buffer)` and `Read(ulong offset, Span<byte> destination, int count)`. Internally, the existing `byte[]`-based logic can work with spans via `MemoryMarshal.TryGetArray`. For `ReadHunk`, copy from the internal `_hunkBuffer` to the caller's span. For `Read`, use the span directly in the cross-hunk loop. `Memory<byte>` overloads can be added later for truly async paths. |
| **Estimated Time** | 3–4 hours |

---

### 20. ReadHeader Standalone

| Field | Value |
|-------|-------|
| **Missing Feature** | `chd_read_header(filename, header)` — reads and parses the full CHD header from a file without opening it for hunk reads. (The current `Chd.CheckHeader`/`IsChdFile` only sniff magic + version; there is no full header DTO read.) |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add a static `Chd.ReadHeader(string filename, out ChdHeader header)` method. Open the file, read the header (reuse `ChdHeaders.ReadHeaderV*`), populate a public `ChdHeader` DTO, close the file. The DTO exposes all header fields: version, flags, compression slots, hunk bytes, total hunks, logical bytes, MD5, SHA1, parent MD5/SHA1, unit bytes, unit count, etc. This is lighter than `Open()` which keeps the file handle alive. |
| **Implemented As** | `Chd.ReadHeader(string filename, out ChdHeaderInfo? header)` — libchdr `chd_read_header` parity — plus a stream overload (`Chd.ReadHeader(Stream, out ChdHeaderInfo?)`, mirroring `chd_read_header_file`; the stream is seeked from byte 0 and left open) and `Chd.ReadHeaderAsync(string)`. The public `ChdHeaderInfo` record exposes `Length`, `Version`, `Flags` (V1–V4), `Compression` codec slots, `HunkBytes`, `TotalHunks`, `TotalBytes`, `MetaOffset`, `MapOffset` (V5), all hashes (`Md5`, `ParentMd5`, `Sha1`, `RawSha1`, `ParentSha1`), `UnitBytes`/`UnitCount` (V5 from header; V1–V4 derived from metadata via the shared `ChdFile.GuessUnitBytesFromMetadata`, matching `ChdFile.UnitBytes` and libchdr `header_guess_unitbytes`), `HasParent`, and the obsolete V1/V2 geometry fields. The flags field was previously discarded by `ReadHeaderV1-V4` and is now captured; V5's map offset is retained for the DTO. No hunk-map linking, codec setup, or parent resolution is performed, and no file handle is retained. Tests in `ReadHeaderTests` cover all versions, field parity with an opened `ChdFile`, codec slots (incl. multi-codec and uncompressed), child/parent hash linkage, V1 geometry, error paths (missing/invalid/truncated/non-seekable), stream leave-open semantics, and the async overload. |
| **Estimated Time** | 2–3 hours |

---

### 21. GetMetadata(tag, index) — Indexed Metadata Search API

| Field | Value |
|-------|-------|
| **Missing Feature** | `chd_get_metadata(chd, searchtag, searchindex, ...)` — search metadata by tag and ordinal index, returning data, result tag, and flags. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add `ChdFile.GetMetadata(string tag, uint index)` returning `(ChdError error, ChdMetadataEntry? entry)`. Walk `_metadata` list, filter by tag (or wildcard), count matches, return the `index`-th one. Also add a `GetMetadata(string tag)` convenience overload returning the first match. Expose `resultFlags` (checksum flag) on `ChdMetadataEntry` as a new `Flags` property. |
| **Implemented As** | `ChdFile.GetMetadata(string? tag, uint index, out ChdMetadataEntry? entry)` — libchdr `chd_get_metadata` parity, including wildcard matching when `tag` is null/empty. |
| **Estimated Time** | 2–3 hours |

---

### 22. Wildcard Metadata Tag Search

| Field | Value |
|-------|-------|
| **Missing Feature** | `CHDMETATAG_WILDCARD` (tag value 0) — matches any metadata tag when searching. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add a constant `ChdMetadataTagWildcard = 0` (or `"\0\0\0\0"`). In `ChdFile`, add a `GetMetadata(uint searchTag, uint searchIndex)` method that traverses the metadata chain and returns the Nth entry matching `searchTag` (or any tag if wildcard). This mirrors libchdr's `chd_get_metadata()`. The existing `Metadata` property returns all entries and remains unchanged. |
| **Implemented As** | Wildcard search via `GetMetadata(null, index, out entry)`; the `Metadata` property remains the full-list accessor. |
| **Estimated Time** | 2–3 hours |

---

### 23. Non-Owning File Close

| Field | Value |
|-------|-------|
| **Missing Feature** | `core_stdio_fclose_nonowner` — close a CHD without closing the underlying file handle, allowing the caller to retain ownership of the stream. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add a `bool leaveOpen` parameter (default `false`) to `ChdFile.Open(Stream, ...)` overloads. Store it as `_leaveOpen`. In `Dispose()` / `DisposeAsync()`, skip `_stream.Dispose()` when `_leaveOpen` is true. This mirrors the existing `leaveOpen` pattern used by `BinaryReader` and other .NET stream wrappers. |
| **Implemented As** | All `Open(Stream, bool leaveOpen, ...)` / `OpenAsync(Stream, bool leaveOpen, ...)` overloads; disposal skips the stream when `leaveOpen` is true. |
| **Estimated Time** | 1 hour |

---

### 24. Enable Nullable Reference Types Project-Wide

| Field | Value |
|-------|-------|
| **Missing Feature** | `<Nullable>enable</Nullable>` is not set in the csproj. Nullable annotations are used ad-hoc but not enforced. `ChdHeader` fields use `= null!` suppressions extensively. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add `<Nullable>enable</Nullable>` to `Directory.Build.props`. Fix all resulting warnings: convert `= null!` fields to proper constructor initialization or required properties. Add `?` to genuinely nullable fields. Use `[MemberNotNull]` / `[MemberNotNullWhen]` attributes where methods guarantee initialization. This is a large but mechanical refactor that catches real null-dereference bugs at compile time. |
| **Implemented As** | `<Nullable>enable</Nullable>` was already present in `Directory.Build.props`; the remaining work was eliminating the `= null!` suppressions. `ChdHeader.Compression`/`ChdReader`/`Map` now default to `[]` (populated by the header parsers/`FindBlockReaders`); the five hash fields became genuinely nullable `byte[]?` (V1/V2 have no SHA1, V4/V5 have no MD5), with the public `ChdFile.Sha1`/`RawSha1`/`Md5` properties keeping their documented `byte[]` contract via `!`. `MapEntry.SelfMapEntry`/`BuffIn`/`BuffOut`/`BuffOutCache` are now `?`-nullable (they were already null-checked at their read sites, e.g. `CHDBlockRead.cs:149,265` and `CHDFile.cs:975`); `ReadBlock` gained explicit null guards returning `Chderrcodecerror`/`Chderrinvaliddata` instead of latent `NullReferenceException`s, and self-reference loops (`ReadHunk`, `LinkSelfBlocks`, `FindRepeatedBlocks`, `ReadBlock` self case) now use null-safe locals. `Util.IsAllZeroArray` carries `[NotNullWhen(false)]` so `!IsAllZeroArray(hash)` narrows the hash to non-null at hash-comparison sites. LZMA: `LzmaStream`'s preset-dictionary parameter is `Stream?` (was `null!`-fed), the lazy LZMA2 `_decoder` is `Decoder?`, `RangeCoder.Decoder.Stream` is `Stream?` with a guarded `ReadByteChecked`, `OutWindow` uses `[]`/`Stream?` defaults, and `LzmaDecoder._mCoders = []`. FLAC: `AudioDecoder.Path` is properly assigned in both constructors (empty string for stream input) and the unused `(AudioPcmConfig)` ctor's `null!` writes became benign defaults; `AudioBuffer`'s lazily-allocated `_samples`/`_fsamples`/`_bytes` are nullable with null-safe guards in the conversion getters. Test/tool suppressions (`ChdReader = null!` initializers in `HeaderAndApiTests`, `ChdmanWrapper` string fields) converted to real defaults. Solution builds with zero nullable warnings; full suite green (CHDSharpTest 558/558 across net8/9/10; the pre-existing intermittent CHDSharpEncoderTest flake under concurrent multi-TFM runs reproduces on the unmodified baseline). |
| **Estimated Time** | 6–8 hours |

---

## Tier 6 — Diagnostics & Observability

---

### 25. IProgress&lt;T&gt; Reporting on Long Operations

| Field | Value |
|-------|-------|
| **Missing Feature** | No long-running operation reports progress. `ExtractToDirectory`, `ReadAllBytes`, `CheckFile`, and `EnumerateHunks` give the caller no feedback during multi-gigabyte operations. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Define `ChdProgress` record with `long CurrentHunk`, `long TotalHunks`, `long BytesProcessed`, `long TotalBytes`, `TimeSpan Elapsed`. Add `IProgress<ChdProgress>? progress = null` parameter to `ExtractToDirectory`, `ReadAllBytes`, `CheckFile`, `CheckFileWithParent`. Report after each hunk decompression. For `CheckFile`, also report hash computation progress. Callers can use `Progress<ChdProgress>` for UI binding or logging. |
| **Implemented As** | Added the public `ChdProgress` record (`CurrentHunk` 1-based hunks completed, `TotalHunks`, `BytesProcessed`, `TotalBytes`, `Elapsed`, plus a computed `Percent` and a readable `ToString()`). Optional trailing `IProgress<ChdProgress>? progress = null` parameters on `Chd.CheckFile` (both overloads, reported from the in-order hashing thread of the parallel pipeline after each hunk is hashed), `Chd.CheckFileWithParent` (both overloads, sequential loop), `ChdFile.ReadAllBytes`, `ChdFile.EnumerateHunks`, and `ChdFile.ExtractToDirectory` / `ExtractToDirectoryWithReporting` (both the whole-image path and GD-ROM per-track path). Existing callers compile unchanged (default `null` = no reporting, zero overhead beyond a null check per hunk). Tests in `ProgressReportingTests` cover per-hunk report counts, monotonic progress, final 100%/totals, ordered parallel reports, header-only silent behavior, byte-equality between reported and unreported `ReadAllBytes`, and backward-compatible calls without the parameter. |
| **Estimated Time** | 3–4 hours |

---

### 26. Hard Disk Ident Metadata (IDNT)

| Field | Value |
|-------|-------|
| **Missing Feature** | chdman `createhd -id <ident.bin>` reads an ATA IDENTIFY DEVICE response (512 bytes) from a file and stores it as `IDNT` metadata. This preserves the original drive's model, serial, CHS geometry, and firmware revision — needed by some emulators (e.g. OG Xbox HDD emulation). CHDSharp's `createhd` command and `HardDiskMetadata` class do not read or write `IDNT` entries. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | (1) Add `IdentMetadataTag = 0x494E5452` ("IDNT") constant and `BuildIdentMetadata(byte[] identData)` in `MetadataWriter`. (2) In `createhd`, accept `--ident <path>` CLI flag; read the 512-byte file and write it as an `IDNT` metadata entry. (3) In `ChdFile`, expose `IdentData` property that returns the raw `IDNT` bytes (or null). (4) In `ReadHeader`, include `IDNT` in the DTO if present. (5) During `copy`, clone `IDNT` entries. |
| **Estimated Time** | 2–3 hours |

---

### 27. Hard Disk Key Metadata (KEY)

| Field | Value |
|-------|-------|
| **Missing Feature** | chdman stores hard disk encryption keys as `KEY ` metadata (binary blob). Used by OG Xbox and other platforms that encrypt HDD contents. CHDSharp has no read/write support for this tag. |
| **Implementation Status** | Not planned |
| **Proposed Logic** | (1) Add `KeyMetadataTag = 0x4B455920` ("KEY ") constant. (2) Expose `KeyData` property on `ChdFile` returning the raw bytes. (3) In `ChdHeaderInfo` DTO, include the key if present. (4) During `copy`, clone `KEY ` entries. (5) In CLI, support `--key <path>` for `createhd`. |
| **Estimated Time** | 1–2 hours |

---

### 28. PCMCIA CIS Metadata (CIS)

| Field | Value |
|-------|-------|
| **Missing Feature** | chdman stores PCMCIA Card Information Structure as `CIS ` metadata (binary blob). Used by PC Engine CD and other platforms with PCMCIA interfaces. CHDSharp has no read/write support for this tag. |
| **Implementation Status** | Finished |
| **Proposed Logic** | (1) Add `PcmciaCisMetadataTag = 0x43495320` ("CIS ") constant. (2) Expose `PcmciaCisData` property on `ChdFile`. (3) During `copy`, clone `CIS ` entries. |
| **Implemented As** | Added `PcmciaCisMetadataTag = 0x43495320` constant to `MetadataWriter`. Added `PcmciaCisData` property to `ChdFile` that returns the raw CIS metadata bytes (or null if absent). CIS metadata is automatically cloned during `ChdEncoder.Copy()` (all non-legacy metadata is cloned verbatim). Added6 unit tests: read/write, absent returns null, preserved during copy, set/delete, empty data, and coexistence with other metadata. All18 PcmciaCisMetadataTests pass across net8.0/net9.0/net10.0. |
| **Estimated Time** | 1 hour |

---

### 29. Open Parent Callback (Lazy Parent Resolution)

| Field | Value |
|-------|-------|
| **Missing Feature** | MAME's `chd_file::open()` accepts an `open_parent_func` callback that resolves a parent CHD by SHA-1 hash at read time, rather than requiring an explicit file path upfront. This enables libraries and frontends (RetroArch, MAME) to implement their own parent search logic (database lookup, ROM set scanning) without the CHD library needing to know the file location. CHDSharp currently requires an explicit `parentPath` or pre-opened `ChdFile` instance. |
| **Implementation Status** | Finished |
| **Proposed Logic** | (1) Define `Func<HashInfo, ChdFile?>?` delegate type for parent resolution (takes a record with `Sha1` and optional `Md5`, returns a parent `ChdFile` or null). (2) Add `ChdFile.Open(Stream, Func<HashInfo, ChdFile?> parentResolver, ...)` overload. During `ReadHunk`, when a parent-referencing hunk is encountered, call the resolver if no parent is set. Cache the resolved parent. (3) Add `Chd.CheckFileWithParent(path, Func<...> resolver)` variant. (4) Keep existing explicit-parent overloads unchanged. |
| **Implemented As** | `ParentResolver` delegate (`Func<byte[]?, byte[]?, ChdFile?>`) that takes parent SHA1 and MD5 hashes and returns an opened parent `ChdFile` or null. New `ChdFile.Open` overloads: `Open(string, ParentResolver?, out ChdFile?, ...)`, `Open(Stream, bool, ParentResolver?, out ChdFile?, ...)`, and `Open(Stream, bool, ChdFile?, ParentResolver?, out ChdFile?, ...)` — the core 5-param overload allows opening child CHDs without a parent upfront when a resolver is provided. `TryResolveParent()` method lazily invokes the resolver on first parent-hunk read, validates the resolved parent's hashes, and caches the result. All three ReadParentHunk variants (sync, async, concurrent) support lazy resolution. New `Chd.CheckFileWithParent(string, ParentResolver?, ...)` overloads for verification with lazy resolution. Added7 unit tests: resolver invocation, caching, null resolver returns `Chderrrequiresparent`, hash mismatch returns `Chderrinvalidparent`, null resolver fails at open, verification with resolver, and hash passthrough. All 21 ParentResolverTests pass across net8.0/net9.0/net10.0. |
| **Estimated Time** | 3–4 hours |

---

### 30. Blank HD CHD Creation Without Input

| Field | Value |
|-------|-------|
| **Missing Feature** | chdman `createhd` can create a zero-filled hard disk CHD without reading from an input file (using `--size` flag). Useful for creating virtual hard drives for emulators. CHDSharp's `EncodeRaw`/`EncodeHardDisk` require a source stream. |
| **Implementation Status** | Finished |
| **Proposed Logic** | (1) Add `ChdEncoder.CreateBlank(string outputPath, long totalBytes, int hunkSize, int unitSize, string compression, ChsGeometry? chs, int sectorSize)` static method. (2) Implementation: create a zero-filled hunk buffer, write `totalHunks` hunks of all-zeros using `COMPRESSION_NONE` (or self-references for dedup). (3) Write hard disk metadata from CHS if provided. (4) CLI: `createhd --size 500M --chs 1024,16,63 -o blank.chd`. (5) `CreateBlankAsync` variant with `IProgress<ChdProgress>`. |
| **Implemented As** | `ChdEncoder.CreateBlank` and `ChdEncoder.CreateBlankWithChs` static methods that create zero-filled CHD v5 files without requiring an input stream. The implementation: (1) `CreateBlank(chdPath, totalBytes, hunkBytes, unitBytes, codecTags, options)` creates a blank CHD with auto-generated GDDD hard disk geometry metadata derived from the total size. (2) `CreateBlankWithChs(chdPath, cylinders, heads, sectors, sectorSize, hunkBytes, codecTags, options)` creates a blank CHD with explicit CHS geometry metadata. (3) Both methods use a zero-filled hunk reader that clears the buffer for each hunk, matching chdman's behavior for blank disk creation. (4) Added `--createhd` CLI command with `--size N` (supports K/M/G suffixes), `-chs C,H,S` for explicit geometry, `-ss N` for sector size, and standard options (`-c`, `-hs`, `-us`, `-t`, `-v`). (5) Added 10 unit tests: `CreateBlank_ProducesValidHeader`, `CreateBlank_HasCorrectLogicalBytes`, `CreateBlank_HasHardDiskMetadata`, `CreateBlank_VerifiesSuccessfully`, `CreateBlank_WithChs_ProducesCorrectGeometry`, `CreateBlank_AllZeros_ReadsCorrectly`, `CreateBlank_WithNoneCodec_ProducesValidFile`, `CreateBlank_LargeFile_Works`, `CreateBlank_ZeroBytes_ThrowsArgumentException`, `CreateBlank_InvalidHunkSize_ThrowsArgumentException`. All 30 CreateBlankTests pass. |
| **Estimated Time** | 2–3 hours |

---

### 31. Metadata Upgrade During Copy/Recompress

| Field | Value |
|-------|-------|
| **Missing Feature** | chdman's `copy` command upgrades legacy metadata tags to their current equivalents: `CHCD` → `CHT2`, `CHGT` → `CHGD` (with CDDA byte-swap flag fix). CHDSharp's `Copy` method clones metadata verbatim without upgrading. Old CHDs created with pre-V5 chdman may carry legacy tags that newer tools don't handle optimally. |
| **Implementation Status** | Finished |
| **Proposed Logic** | In `ChdEncoder.Copy`, after cloning metadata entries, scan for legacy tags: (1) If `CHCD` is found, parse its binary track data, convert to `CHT2` text format (adding pregap/postgap fields), and replace the entry. (2) If `CHGT` is found, parse its binary track data, convert to `CHGD` text format, and replace. (3) Remove the old entries. (4) Add a `--no-upgrade` CLI flag to preserve legacy tags if the user explicitly wants them. |
| **Implemented As** | `ChdEncoder.Copy` now detects legacy CD/GD-ROM metadata tags (`CHCD`, `CHTR`, `CHGT`) and upgrades them to modern equivalents (`CHT2`, `CHGD`) during copy, matching MAME chdman's `copy` command behavior. The implementation: (1) Iterates all metadata entries, skipping legacy tags and cloning everything else. (2) When legacy tags are found, uses the source's parsed `Tracks` property to build a `CdToc` and generates modern `CHT2`/`CHGD` entries via `MetadataWriter.BuildCdMetadataEntries`. (3) For legacy GD-ROMs (`CHGT`), CDDA audio tracks are byte-swapped from little-endian to big-endian during the copy (matching MAME's `cdrom.cpp:402` behavior). (4) Added `ChdEncodeOptions.NoMetadataUpgrade` property to preserve legacy tags when set to `true`. (5) Added `--no-upgrade` CLI flag to the `--copy` command. (6) Added legacy tag constants to `MetadataWriter`: `CdRomOldMetadataTag` (CHCD), `CdRomTrackMetadataTag` (CHTR), `GdRomOldMetadataTag` (CHGT). (7) Added `IsLegacyCdMetadata` and `IsLegacyGdRomMetadata` helper methods. (8) Added `BuildTocFromTracks` helper to convert `ChdTrackInfo` to `CdTrack`. (9) Added 4 new unit tests: `Copy_UpgradesLegacyChtrToCht2`, `Copy_PreservesNonCdMetadata`, `Copy_NoUpgradeFlag_PreservesLegacyMetadata`. All 42 ChdCopyTests pass. |
| **Estimated Time** | 2–3 hours |

---

### 32. K/M/G Size Suffix Parsing in CLI

| Field | Value |
|-------|-------|
| **Missing Feature** | chdman accepts human-readable size suffixes: `10M` = 10485760, `2G` = 2147483648, `512K` = 524288. CHDSharp's CLI parses only plain numeric values for `--hunksize`, `--unitsize`, `--size`, `--inputbytes` etc. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add `TryParseSizeWithSuffix(string s)` utility that: (1) strips trailing K/M/G suffix, (2) parses the numeric part, (3) multiplies by 1024/1048576/1073741824. Apply to all CLI options that accept byte sizes. |
| **Implemented As** | Added `TryParseSizeWithSuffix` (two overloads: `uint` and `long`) to `Program.cs`, matching MAME chdman's `parse_number()` behaviour — `K`/`k` = ×1024, `M`/`m` = ×1048576, `G`/`g` = ×1073741824, with `checked` overflow protection. Applied to `-hs` (hunk size) and `-us` (unit size) parsing in both `TryParseOptions` (shared by `--create`, `--createcd`) and `CreateLdTest` (`--createld`). |
| **Estimated Time** | 30 minutes |

---

## Optional Enhancements

---

### 33. Precache — Full File In-Memory Cache

| Field | Value |
|-------|-------|
| **Missing Feature** | `chd_precache()` — reads the entire CHD file into a memory buffer so subsequent hunk reads are served from RAM instead of disk. |
| **Implementation Status** | Finished |
| **Proposed Logic** | Add a `Precache()` / `PrecacheAsync()` method to `ChdFile`. On call, read `_stream` entirely into a `byte[]` (or `Memory<byte>`) backed by `ArrayPool`. Swap the internal `_stream` reference to a `MemoryStream` over that buffer. Return `ChdError.Chderrounofmemory` if allocation fails. Guard against double-calling. The existing `ReadHunk` / `Read` paths remain unchanged since they already read from `_stream`. |
| **Implemented As** | `ChdFile.Precache()` — reads the whole stream into a `byte[]` (2 GiB cap), restores stream position, idempotent. |
| **Estimated Time** | 2–3 hours |

