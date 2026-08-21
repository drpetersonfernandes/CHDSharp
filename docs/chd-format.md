---
layout: default
---

# CHD Format Reference

This page documents the **CHD (Compressed Hunks of Data)** on-disk format as implemented by CHDSharp. It is based on the MAME 0.288 sources and the reference notes in [`References/CHDInfo.md`](../References/CHDInfo.md).

---

## 1. Overview

A CHD file stores a logical disk image split into fixed-size blocks called **hunks**. Each hunk can be:

- stored **uncompressed**,
- **compressed** with one of several codecs,
- **deduplicated** (a copy of another hunk in the same file — *self reference*), or
- **inherited** from a parent CHD (delta/incremental images — *parent reference*).

A **map** (one entry per hunk) records how and where each hunk's data is stored. A linked list of **metadata** blobs (tagged with four-character tags) carries information such as hard-disk geometry, CD track layouts, and laserdisc A/V parameters.

All multibyte values on disk are **big-endian**. The file starts with the magic tag `MComprHD`.

---

## 2. Version history (V1 → V5)

| Version | Header size | Highlights |
|---------|-------------|------------|
| **V1** | 76 bytes | Hard-coded CHS geometry (512-byte sectors); only zlib/raw; MD5 only; **no metadata**; 8-byte packed map entries. |
| **V2** | 80 bytes | Same as V1 plus `seclen` (bytes per sector) at offset 76. |
| **V3** | 120 bytes | Major redesign: `logicalbytes`, `metaoffset`, explicit `hunkbytes`, SHA1 hashes, `ZLIB_PLUS` (type 2, secondary codec), 16-byte map entries with CRC32. |
| **V4** | 108 bytes | Drops MD5; `sha1` becomes the *combined* raw+metadata hash; adds `rawsha1` (raw data only); adds A/V compression type 3. |
| **V5** | 124 bytes | Current MAME format: up to **4 codecs** identified by 4-char tags, `mapoffset` (map at end of file), `unitbytes` (sub-hunk granularity for parent refs), CRC16 map entries, compressed (Huffman+RLE) or uncompressed maps. |

---

## 3. Headers

### V5 header (124 bytes)

```
[0-7]    "MComprHD"                magic
[8-11]   124                       header length
[12-15]  5                         version
[16-19]  compressors[0]           4-char codec tag, e.g. 'lzma'
[20-23]  compressors[1]           e.g. 'zlib'
[24-27]  compressors[2]           e.g. 'huff'
[28-31]  compressors[3]           e.g. 'flac'  (all zero => uncompressed image)
[32-39]  logicalbytes             total uncompressed size
[40-47]  mapoffset                file offset of the map (0 while being written)
[48-55]  metaoffset               file offset of the first metadata entry (0 = none)
[56-59]  hunkbytes                size of each hunk
[60-63]  unitbytes                size of each unit (parent-reference granularity)
[64-83]  rawsha1                  SHA1 of the raw decompressed data
[84-103] sha1                     SHA1 of raw data + checksummed metadata
[104-123] parentsha1              SHA1 of the parent (all zero => standalone)
```

### Header validation performed by CHDSharp

`Chd.CheckHeader()` verifies the magic, that the declared header length matches the version (`76/80/120/108/124`), and that the version is 1–5. The version-specific parsers additionally enforce:

- `hunkbytes` is non-zero and ≤ 128 MiB; `hunkbytes × totalhunks ≤ 1 TiB` (`ValidateSizeLimits`).
- V3/V4: known compression type (0 = none, 1 = zlib, 2 = zlib+), and for type 2 the secondary codec (FLAC CDDA) is initialized.
- V5: each 4-char codec tag must be a known codec (`IsValidCodec`), otherwise `Chderrinvaliddata`.
- V1/V2: the obsolete CHS geometry is persisted so CHDSharp can synthesize GDDD metadata (see [Metadata](metadata.md)).

---

## 4. Maps

### V1/V2 map — 8 bytes per entry

A packed 64-bit word: **top 20 bits = length, low 44 bits = file offset**. The hunk is uncompressed iff `length == hunkbytes`. V1/V2 entries carry no CRC.

### V3/V4 map — 16 bytes per entry

```
[0-7]   uint64   offset      file offset of data
[8-11]  uint32   crc32       CRC-32 of the uncompressed hunk
[12-14] uint24   length      compressed length
[15]    uint8    flags       type (low nibble) | NO_CRC (0x10)
```

Entry types (flags & 0x0F):

| Value | Type | Meaning |
|-------|------|---------|
| 0 | invalid | — |
| 1 | compressed | decompress with codec #0 (zlib or secondary for type 6) |
| 2 | uncompressed | raw copy |
| 3 | mini | repeat the 8-byte big-endian pattern `offset` |
| 4 | self | copy hunk `offset` in this file |
| 5 | parent | copy from parent CHD |
| 6 | 2nd compressed | compressed with the *secondary* codec (FLAC CDDA, V3/V4 `ZLIB_PLUS`) |

When the **NO_CRC** flag (0x10) is set, CHDSharp drops the CRC so the hunk is accepted without validation (matching MAME semantics). When present, the CRC32 **is** verified after decompression.

### V5 compressed map

The map itself is stored compressed at `mapoffset`:

```
[0-3]    uint32   length          compressed byte length
[4-9]    UINT48   datastart       file offset of the first data block
[10-11]  uint16   crc             CRC-16 of the *uncompressed* map
[12]     uint8    lengthbits      bits per compressed-length field
[13]     uint8    hunkbits        bits per self-reference field
[14]     uint8    parentunitbits  bits per parent-unit field
[15]     uint8    reserved
[16+]             Huffman-encoded map entries
```

Each **expanded** entry is 12 bytes:

```
[0]      uint8   compression type
[1-3]    UINT24  compressed length
[4-9]    UINT48  file offset
[10-11]  uint16  crc16            CRC-16 (CCITT) of the uncompressed hunk
```

The map encoding applies RLE to compression types (runs of 3–18 and 19–290 become `RLE_SMALL`/`RLE_LARGE`), promotes consecutive self/parent references to `SELF_0/1` and `PARENT_0/1/PARENT_SELF` pseudo-types, Huffman-encodes the type stream, then writes per-entry auxiliary data (lengths, CRC16s, offsets) at fixed bit widths. The whole uncompressed map's CRC16 is verified against the map header.

**Encoder quirk (small maps):** chdman sizes the bitstream buffer as `(8*16 + (12 + max(lengthbits+16, hunkbits, parentunitbits)) * hunkcount) / 8 + 1` bytes *including* the 16-byte header. For small hunk counts that area is smaller than the actual tree + symbol + auxiliary bits, so MAME's `bitstream_out` silently drops whole trailing bytes while `flush()` still counts them in the map's compressed-length field; the dropped positions read back as zeroes. When a dropped byte is nonzero the stored map no longer matches its header CRC16 and the file cannot be re-opened — not even by chdman itself (reproducible with a single-hunk `createraw`, e.g. `-hs 65536` over random data). CHDSharp's encoder replicates the allocation and clipping byte-for-byte whenever chdman's output is well-formed, and falls back to the full bitstream when clipping would corrupt the map.

### V5 uncompressed map — 4 bytes per entry

Used when all four codec slots are zero: each entry is a `uint32` **hunk index** (`offset = index × hunkbytes`); `0` means *take the hunk from the parent* (or zero-fill when there is no parent).

### V5 per-hunk compression types

| Value | Name | Meaning |
|-------|------|---------|
| 0–3 | `TYPE_0..3` | compressed with codec #0..#3 |
| 4 | `NONE` | uncompressed (length = hunkbytes) |
| 5 | `SELF` | copy from hunk `offset` in this file |
| 6 | `PARENT` | copy from parent **unit** `offset` (see below) |
| 7–13 | pseudo-types | RLE/promotion encodings used inside the compressed map stream |

### Unit-based parent references (V5)

A `PARENT` entry stores a **parent-unit index**, not a hunk index. With `units_in_hunk = hunkbytes / unitbytes`:

- aligned reference → read parent hunk `unit / units_in_hunk`;
- unaligned reference → **straddles two parent hunks**: CHDSharp stitches the tail of parent hunk N with the head of parent hunk N+1.

---

## 5. Codecs

| FourCC | Name | Notes |
|--------|------|-------|
| `zlib` | Deflate | Raw deflate (no zlib header, `-MAX_WBITS`). |
| `lzma` | LZMA | Headerless single-call LZMA; properties fixed at lc=3, lp=0, pb=2; dictionary = hunk size. |
| `huff` | Huffman | Custom static 8-bit Huffman; tree itself Huffman-encoded. |
| `flac` | FLAC | Raw FLAC frames, 16-bit, preceded by a 1-byte endianness marker (`'L'`/`'B'`). |
| `zstd` | Zstandard | Single-frame zstd blocks. |
| `avhu` | A/V Huffman | Laserdisc A/V: FLAC or delta-Huffman audio + delta-RLE Huffman video. |
| `cdzl` | CD zlib | zlib for sector data + zlib for subcode; ECC/sync regenerated. |
| `cdlz` | CD LZMA | LZMA for sector data + zlib for subcode; ECC/sync regenerated. |
| `cdfl` | CD FLAC | FLAC for sector data + zlib for subcode. |
| `cdzs` | CD Zstd | zstd for sector data + zstd for subcode; ECC/sync regenerated. |

See [Codecs](codecs.md) for implementation details.

---

## 6. Metadata

Metadata is a linked list of tagged binary blobs:

```
[0-3]   uint32   metatag    4-char tag ('GDDD', 'CHT2', 'DVD ', ...)
[4]     uint8    flags      bit 0 (CHD_MDFLAGS_CHECKSUM): included in the combined SHA1
[5-7]   UINT24   length     payload length
[8-15]  uint64   next       file offset of the next entry (0 = end)
```

Standard tags:

| Tag | Meaning |
|-----|---------|
| `GDDD` | Hard-disk geometry: `CYLS:%d,HEADS:%d,SECS:%d,BPS:%d` |
| `IDNT` | Raw 512-byte ATA IDENTIFY data |
| `KEY ` / `CIS ` | Hard-disk key / PCMCIA CIS data |
| `CHCD` | Legacy CD-ROM metadata (binary track records) |
| `CHTR` | CD-ROM tracks v1: `TRACK:%d TYPE:%s SUBTYPE:%s FRAMES:%d` |
| `CHT2` | CD-ROM tracks v2: adds `PREGAP`, `PGTYPE`, `PGSUB`, `POSTGAP` |
| `CHGT` / `CHGD` | Legacy / current GD-ROM track metadata |
| `DVD ` | DVD-ROM marker |
| `AVAV` | A/V metadata: `FPS:%d.%06d WIDTH:%d HEIGHT:%d INTERLACED:%d CHANNELS:%d SAMPLERATE:%d` |
| `AVLD` | Laserdisc VBI frame data |

CHDSharp reads metadata lazily, guards against cyclic chains, caps entries at 1 MiB, and exposes flags. V1/V2 files — which have no metadata section — get a **synthesized `GDDD` entry** built from the obsolete header geometry. See [Metadata](metadata.md).

---

## 7. Hashing & integrity

| Hash | Scope | Used in |
|------|-------|---------|
| MD5 | raw data | V1–V3 |
| SHA1 (`rawsha1`) | raw decompressed data | V3–V5 |
| SHA1 (`sha1`) | `rawsha1` ‖ sorted hashes of checksummed metadata entries | V4–V5 |
| CRC32 | per-hunk (V3/V4 map) | V3–V4 |
| CRC16 | per-hunk + map (V5) | V5 |

The combined SHA1 is computed as:

```
sha1 = SHA1( rawsha1 ‖ sorted([ SHA1(metatag ‖ metadata) for each checksummed entry ]) )
```

CHDSharp's deep verification recomputes all of these; see [Verification](verification.md).

---

## 8. Parent/child (delta) CHDs

A child CHD stores only hunks that differ from its parent; identical hunks become `PARENT` references. The child header stores the parent's `sha1`/`md5`. When a `PARENT` entry is read and no parent was supplied, CHDSharp returns `Chderrrequiresparent`; if the supplied parent's hashes do not match, it returns `Chderrinvalidparent`.

See [Parent/Child CHDs](parent-child-chds.md).

---

## 9. File layout (V5 example)

```
[0]                     V5 header (124 bytes)
[124]                   compressed hunk data block 0
[...]                   compressed hunk data block 1
[...]                   ... more data blocks (variable size)
[...]                   metadata linked list (if any)
[end-of-file]           compressed V5 map (at mapoffset)
```
