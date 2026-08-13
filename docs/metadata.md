# Metadata

CHD files carry an extensible **metadata chain**: a linked list of tagged binary blobs that describes the image (hard-disk geometry, CD track layout, DVD marker, laserdisc A/V parameters, and arbitrary application data such as game names).

---

## On-disk format

Each entry starts with a 16-byte header, all big-endian:

```
[0-3]   uint32   metatag    4-char tag, e.g. 'GDDD', 'CHT2', 'DVD '
[4]     uint8    flags      bit 0 (CHD_MDFLAGS_CHECKSUM): included in combined SHA1
[5-7]   UINT24   length     payload length (≤ 1 MiB enforced by CHDSharp)
[8-15]  uint64   next       file offset of the next entry (0 = end of chain)
```

The chain is anchored at `metaoffset` in the header (0 = no metadata).

---

## Standard tags

| Tag | Meaning | Payload example |
|-----|---------|-----------------|
| `GDDD` | Hard-disk geometry | `CYLS:6144,HEADS:16,SECS:63,BPS:512` |
| `IDNT` | ATA IDENTIFY data | 512 raw bytes |
| `KEY ` | Hard-disk key data | binary |
| `CIS ` | PCMCIA CIS info | binary |
| `CHCD` | Legacy CD-ROM metadata | binary track records |
| `CHTR` | CD tracks v1 | `TRACK:1 TYPE:MODE1 SUBTYPE:NONE FRAMES:600` |
| `CHT2` | CD tracks v2 | `TRACK:1 TYPE:MODE1 SUBTYPE:NONE FRAMES:600 PREGAP:0 PGTYPE:MODE1 PGSUB:NONE POSTGAP:0` |
| `CHGT` / `CHGD` | GD-ROM metadata | track records incl. `PAD` frames |
| `DVD ` | DVD-ROM marker | — |
| `AVAV` | Laserdisc A/V | `FPS:29.970030 WIDTH:512 HEIGHT:262 INTERLACED:1 CHANNELS:2 SAMPLERATE:44100` |
| `AVLD` | Laserdisc VBI frame data | binary (per-frame packed VBI) |

---

## Reading metadata

### The full list

```csharp
using CHDSharp;

var err = ChdFile.Open("game.chd", out var chd);
using (chd)
{
    foreach (var entry in chd.Metadata)      // IReadOnlyList<ChdMetadataEntry>
    {
        Console.WriteLine($"{entry.Tag}  flags=0x{entry.Flags:X2}  {entry}");
        if (entry.IsText)
            Console.WriteLine("  " + entry.GetText());
    }
}
```

- `Metadata` is **lazy-loaded** on first access and cached.
- Entries are returned in file order.
- Corrupt chains (cycles, oversized entries) are handled: the list returns what was readable and the error is logged; the query API below surfaces the error instead.

### Querying by tag and index

```csharp
// First GDDD entry (hard-disk geometry)
var result = chd.GetMetadata("GDDD", index: 0, out var gddd);
if (result == ChdError.Chderrnone)
    Console.WriteLine(gddd!.GetText());

// Wildcard: first entry of any tag
chd.GetMetadata(null, 0, out var any);

// Second CHT2 entry (track 2)
chd.GetMetadata("CHT2", 1, out var track2);
```

`GetMetadata` mirrors libchdr's `chd_get_metadata`:

- `tag` — 4-char tag; `null` or empty string matches **any** tag (wildcard).
- `index` — zero-based occurrence index among matching entries.
- Returns `Chderrnone` with the entry, `Chderrmetadatanotfound` when nothing matches, or `Chderrreaderror`/`Chderrinvaliddata` when the chain could not be read.

### Flags

`ChdMetadataEntry.Flags` exposes the header flags byte. Bit 0 (`CHD_MDFLAGS_CHECKSUM`) marks entries that participate in the combined-SHA1 verification (see [Verification](verification.md)).

---

## Synthesized GDDD for V1/V2

V1 and V2 CHDs have **no metadata section at all** (the format predates it). To keep the API uniform — and to make `UnitBytes`/`IsHdd`/extraction work for ancient images — CHDSharp **synthesizes** a `GDDD` entry from the obsolete header geometry, exactly like libchdr does:

```text
CYLS:<cylinders>,HEADS:<heads>,SECS:<sectors>,BPS:<bytes-per-sector>
```

where `BPS = hunkbytes / obsolete_hunksize` (512 for V1, `seclen` for V2). The entry has `Flags = 0` and appears in both `Metadata` and `GetMetadata("GDDD", 0)`.

> The synthesized entry is **not** part of the raw metadata chain, so it never affects `CheckFile`'s checksum verification (V1/V2 have no SHA1 to verify anyway).

---

## How the library consumes metadata

| Consumer | Tags | Purpose |
|----------|------|---------|
| `UnitBytes` | `GDDD` (BPS), `CHCD`/`CHTR`/`CHT2`/`CHGT`/`CHGD` | Sector size for parent-block translation |
| `Tracks` / `IsCd` / `IsGdRom` | `CHT2` > `CHTR` > `CHGT` > `CHCD` (priority order) | Track layout parsing |
| `IsDvd` | `DVD ` | DVD detection |
| `IsHdd` | `GDDD` | Hard-disk detection |
| `Chd.CheckFile` (deep) | checksummed entries | Combined SHA1 verification |

---

## Checksum semantics

For V4/V5 files, the header's `sha1` covers the raw data **plus** the checksummed metadata:

```
sha1 = SHA1( rawsha1 ‖ sorted([ SHA1(metatag ‖ metadata) for each entry with flag bit 0 ]) )
```

CHDSharp recomputes this during deep verification and reports `Chderrinvalidmetadata` on mismatch.
