---
layout: default
---

# Troubleshooting & FAQ

Common problems, their causes, and known limitations.

---

## Errors

### `Chderrrequiresparent` — "Child CHD requires a parent"

**Cause:** the file is a differential (child) CHD — its map references hunks stored in a parent image.

**Fix:** supply the parent:

```csharp
ChdFile.Open("child.chd", "parent.chd", out var chd);
// or
ChdFile.Open("child.chd", parentInstance, out var chd);
// verification:
Chd.CheckFileWithParent("child.chd", "parent.chd");
```

### `Chderrinvalidparent` — "Invalid or incompatible parent CHD"

**Cause:** the supplied parent's MD5/SHA1 does not match the `parentmd5`/`parentsha1` stored in the child header — wrong file, wrong version, or a corrupted parent.

**Fix:** locate the exact parent (the child's SHA1 hashes should match the parent's `Sha1`); verify the parent alone first.

### `Chderrdecompressionerror` — "Decompression failed" on a specific hunk

Possible causes:

1. **Corrupt/truncated CHD** — a hunk's CRC16/CRC32 did not match after decompression, or a codec failed. Enable [logging](logging.md) to see the inner exception (the library logs the underlying cause with the hunk number).
2. **A known historical bug** — stereo laserdisc (AVHuff) CHDs (e.g. `dlair.chd`, `cubeqst.chd`) failed with `Failed to read hunk 0: Chderrdecompressionerror` in library versions before the AVHuff mono-FLAC fix. Update to a version containing the fix (the repo includes the regression fixture `v5_av_stereo.chd`).
3. **Very old/odd images** — V1/V2 files carry no per-hunk CRCs, so a *verification* failure there points at the whole-image MD5; a *read* failure indicates structural corruption.

**Fix:** re-dump/re-download the image, or verify with `chdman verify` to confirm the file itself is good.

### `Chderrinvalidfile` — "Not a valid CHD file"

**Cause:** missing `MComprHD` magic, wrong header length for the version, or the stream is not positioned at the start.

**Fix:** make sure you pass the file from byte 0 (`Open` seeks to the start itself; `CheckHeader` requires the stream at position 0).

### `Chderrreaderror` during open or precache

**Cause:** the underlying stream threw while reading (network drop, media error, or a custom stream that does not support what was asked). Note `Open(Stream, ...)` requires `CanSeek`; non-seekable streams return `Chderrinvalidparameter`.

### `Chderroutofmemory` from `ReadAllBytes` / `Precache`

**Cause:** the decompressed image (or compressed file, for `Precache`) exceeds 2 GiB — the API works on `byte[]`, which is limited to `int.MaxValue`.

**Fix:** use `EnumerateHunks()` or `Read()` with your own buffer, or `ExtractToDirectory` for file output.

### `Chderrinvalidmetadata` during deep verification

**Cause:** the combined SHA1 (`rawsha1` + checksummed metadata hashes) does not match the header's `sha1` — metadata was modified or the file is corrupt.

---

## Laserdisc (A/V) CHDs

### Can CHDSharp convert `dlair.chd` to a CD/DVD image?

**No — and neither can `chdman extractcd`.** Laserdisc CHDs are **A/V images** (interleaved video frames + audio), not CD/DVD disc images. CHDSharp extracts them as raw `.raw` data (the `chav` frame stream); `chdman extractld` is the tool that turns them back into an AVI.

### Which A/V content is supported?

- Audio: FLAC (mono per channel) and delta-Huffman — both supported.
- Video: **lossless** delta-RLE Huffman (the only encoding MAME's AVHuff encoder produces) — supported.
- **Lossy AVHuff video variants are not supported** (none exist in the wild; MAME's encoder only writes lossless).

---

## Known limitations

| Limitation | Detail |
|------------|--------|
| **Read-only** | The library cannot create, modify, or repack CHDs. For writing, see `CHDSharpEncoder` (companion project). |
| **Not thread-safe per instance** | One `ChdFile` = one thread. Use separate instances for parallel work. |
| **Seekable streams only** | `Open(Stream, ...)` requires `CanSeek`. |
| **V6+ not supported** | MAME has not released a V6 format; if it ever does, a new header parser will be needed. |
| **No lossy AVHuff video** | See above. |
| **No native codecs** | zstd goes through `ZstdSharp.Port`; everything else is managed. No `libz`/`liblzma` acceleration. |

---

## FAQ

**Q: Is CHDSharp a full replacement for `chdman`?**
A: For *reading, verifying, and extracting* — yes (100% byte-for-byte match with MAME 0.288). For *creating* CHDs — no; that's what `chdman` and the companion `CHDSharpEncoder` are for.

**Q: Why does `CheckFile` report success when the header hashes are all zero?**
A: A CHD with zeroed hashes is valid but unverifiable at the whole-image level; per-hunk CRCs are still validated during deep verification.

**Q: `Metadata` is empty but the file has metadata.**
A: Metadata is lazy-loaded; check `Chd.LoggerFactory` to see warnings. Corrupt chains return the readable prefix and log the failure (the `GetMetadata` API reports the error code).

**Q: Can I read a CHD over HTTP?**
A: Yes — wrap the response stream in a seekable buffer (e.g. copy to `MemoryStream`) and use `Open(Stream, ...)`. `Precache()` is a good fit for remote streams.

**Q: Why do V1/V2 files report a `GDDD` entry in `Metadata`?**
A: V1/V2 have no metadata section; CHDSharp synthesizes `GDDD` from the obsolete header geometry (libchdr does the same) so geometry-based APIs (`UnitBytes`, `IsHdd`) work uniformly.

**Q: Does the library verify the `sha1` on V3 files?**
A: V3 stores `sha1` of raw data; deep verification recomputes and compares MD5/SHA1 when present. The combined (raw+metadata) SHA1 semantics only exist in V4+.

**Q: How do I report a bug?**
A: Include the `ChdError`, the CHD version, the codec list (from header info), and — ideally — the hunk number from the log output. Enabling `Chd.LoggerFactory` with a file sink before reproducing usually captures the root cause.
