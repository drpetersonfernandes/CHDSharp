---
layout: default
---

# Extraction & Track Information

CHDSharp can parse the CD/GD-ROM track layout stored in the metadata and extract the decompressed image to standard files.

---

## Classification

`Chd.Classify` returns the media type without decompressing:

```csharp
var err = Chd.Classify("game.chd", out var kind);
// kind: "cd" | "dvd" | "hdd" | "gd-rom" | null (unknown)
```

The same logic is exposed per-instance via the `IsCd`, `IsGdRom`, `IsDvd`, and `IsHdd` properties.

| Media | Detected by | Extraction output |
|-------|-------------|-------------------|
| CD-ROM | `CHT2`/`CHTR`/`CHCD` metadata | `.bin` + `.cue` |
| GD-ROM (Dreamcast) | `CHGD`/`CHGT` metadata | per-track `.bin` files + `.gdi` |
| DVD | `DVD ` metadata | `.iso` |
| Hard disk | `GDDD` geometry (V1/V2: synthesized) | `.img` |
| Other (e.g. laserdisc A/V) | none of the above | `.raw` |

---

## Track layout (TOC)

`ChdFile.Tracks` returns `IReadOnlyList<ChdTrackInfo>?` (`null` for non-disc images):

```csharp
var err = ChdFile.Open("game.chd", out var chd);
using (chd)
{
    if (chd.Tracks is null)
    {
        Console.WriteLine("Not a CD/GD-ROM image");
        return;
    }

    foreach (var track in chd.Tracks)
    {
        Console.WriteLine(
            $"Track {track.TrackNumber}: {track.GetTypeString()} " +
            $"{track.Frames} frames @ {track.StartFrame}, " +
            $"pregap {track.PreGap}, postgap {track.PostGap}");
    }
}
```

The TOC parser (`ChdTocParser`) supports every metadata format, in priority order:

1. `CHGT` — GD-ROM (legacy) — also sets `IsLittleEndianAudio`
2. `CHGD` — GD-ROM (current)
3. `CHT2` — CD tracks v2 (current, with pregap/postgap)
4. `CHTR` — CD tracks v1
5. `CHCD` — legacy binary track records (4-byte track count, 6×4-byte records per track, endianness auto-detected)

Tracks are padded to 4-frame alignment (`ExtraFrames`). GD-ROM images carry `PadFrames`.

### Sector reads by LBA / MSF

Instead of raw byte offsets, CD/GD-ROM sectors can be read directly by logical block address (or BCD MSF):

```csharp
using CHDSharp.Utils;

var sector = new byte[2352];                    // 2352-byte sector data
chd.ReadSector(0, sector);                      // LBA 0 = first track's INDEX 01
chd.ReadSectorMsf(0x00, 0x02, 0x00, sector);    // same sector, BCD MSF 00:02:00

var frame = new byte[chd.UnitBytes];            // full 2448-byte frame (data + subcode)
chd.ReadFrame(0, frame);

int lba = CdRomAddress.MsfToLba(0x02, 0x00, 0x00);   // 8850
var (m, s, f) = CdRomAddress.LbaToMsf(lba);           // (0x02, 0x00, 0x00)
```

LBA 0 maps to the first data track's INDEX 01: `PreGap` frames into the decompressed image when the pregap is stored physically (metadata `PGTYPE:V...`, e.g. CUE sheets with `INDEX 00`), and at image frame 0 otherwise (Redump-style CUEs, `PREGAP`-keyword CUEs, NRG, TOC, GDI). Non-CD/GD-ROM images return `Chderrinvaliddata`; out-of-range addresses or undersized buffers return `Chderrinvalidparameter`.

### Legacy GD-ROM little-endian CDDA (`CHGT` / `CD_FLAG_GDROMLE`)

Legacy GD-ROMs detected via the `CHGT` tag store their CDDA audio tracks in **little-endian byte order** (Sega CD / PCEngine CD). `ChdFile.IsLittleEndianAudio` is `true` for these. To match MAME's playback behavior, `ExtractToDirectory` byte-swaps the 2352-byte sector-data portion of each 2448-byte frame **only for `AUDIO` tracks** when writing the per-track `.bin` files (subcode is left untouched). Raw `Read()` and hash/verification output are unchanged.

---

## Descriptor generation

### CUE sheet (CD)

```csharp
var cue = chd.GenerateCueSheet("game.bin");   // single-bin format
File.WriteAllText("game.cue", cue);
```

Example output:

```
FILE "game.bin" BINARY
  TRACK 01 MODE1/2048
    INDEX 01 00:00:00
  TRACK 02 AUDIO
    INDEX 01 01:00:00
```

### GDI descriptor (GD-ROM)

```csharp
var gdi = chd.GenerateGdiDescriptor(["track01.bin", "track02.bin", "track03.bin"]);
```

---

## Extraction

### Simple variant

```csharp
var created = chd.ExtractToDirectory("output", "game");
// returns the list of created files; throws InvalidDataException on failure
```

### Reporting variant (no exceptions)

```csharp
var result = chd.ExtractToDirectoryWithReporting("output", "game");
Console.WriteLine(result.Error.GetMessage());
foreach (var t in result.TrackResults)
    Console.WriteLine($"track {t.TrackNumber}: {(t.IsSuccess ? t.FilePath : t.Error.GetMessage())}");
```

### Output mapping

| Image type | Files written |
|------------|---------------|
| CD | `<base>.bin` (whole disc, 2352-byte frames + subcode) + `<base>.cue` |
| GD-ROM | `track01.bin` … `trackNN.bin` (per track) + `<base>.gdi` |
| DVD | `<base>.iso` |
| HDD | `<base>.img` |
| Other | `<base>.raw` (raw decompressed image; e.g. laserdisc `chav` frames) |

Extraction is sequential and streams hunk-by-hunk (`WriteAllBytesSlow`), so it works for images of any size without loading them into memory. Laserdisc A/V CHDs are not CD images — `extractcd`-style tools cannot convert them; CHDSharp extracts the raw A/V frame data (`.raw`), which is what `chdman extractld` consumes.

---

## TOC export

```csharp
string toc = chd.ExportToc();   // human-readable table of contents
Console.WriteLine(toc);
```

---

## Notes

- Extraction requires a seekable stream and works on parent/child chains transparently (parent hunks are resolved through the parent instance).
- `GenerateCueSheet`/`GenerateGdiDescriptor` do **not** write the binary data — pass the actual output filenames so the descriptors reference them.
- Per-track extraction (`TryWriteTrackToFile`) uses `UnitBytes` for frame addressing; for CD images the unit size is the 2448-byte frame.
