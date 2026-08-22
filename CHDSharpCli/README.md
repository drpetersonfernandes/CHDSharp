# CHDSharpCli

**Command-line tool for verifying and inspecting MAME CHD files using the CHDSharp library.**

---

## Usage

```bash
# Verify all .chd files in one or more directories (recursive)
CHDSharpCli D:\CHD
CHDSharpCli D:\CHD E:\MoreCHDs

# Random-access read test on a single CHD
CHDSharpCli --random game.chd

# Verify every .chd path listed in a text file (one path per line)
CHDSharpCli --list chd_paths.txt

# Verify a child (differential) CHD against its parent
CHDSharpCli --parent child.chd parent.chd

# Print table of contents for CD/GD-ROM CHD
CHDSharpCli --toc game.chd

# Generate CUE sheet for CD CHD
CHDSharpCli --cue game.chd [optional .bin filename]

# Classify CHD type (cd/dvd/hdd/gd-rom)
CHDSharpCli --classify game.chd

# Create a CHD from a raw binary
CHDSharpCli --create in.bin out.chd [-c zlib,zstd,lzma,none] [-hs 65536] [-us 4096] [-t 8] [-ip parent.chd] [-v]

# Create a CD CHD from a CUE/BIN (also GDI/ISO/TOC) image
CHDSharpCli --createcd in.cue out.chd [-c zlib,zstd,lzma,none] [-hs N] [-us N] [-t 8] [-ip parent.chd] [-v]

# Create a blank HD CHD (zero-filled, no input file required)
CHDSharpCli --createhd out.chd --size N [-c zlib,zstd,lzma,none] [-hs N] [-us N] [-chs C,H,S] [-ss N] [--ident ident.bin] [-t 8] [-v]

# Re-compress an existing CHD (V1-V5, child sources via -ip)
CHDSharpCli --copy in.chd out.chd [-c zlib,zstd,lzma,none] [-t 8] [-ip parent.chd] [-op parent.chd] [--no-upgrade] [-v]
```

### Commands

| Mode | Description |
|------|-------------|
| `<directory>` | Recursively scan directories for `*.chd` files and verify each one. Multiple directories can be specified. |
| `--random <file.chd>` | Opens a single CHD, reads hunk 0, mid, and last, then reads the entire decompressed image sequentially while computing SHA1/MD5 and comparing against the header. |
| `--list <file.txt>` | Reads a text file of CHD paths (one per line) and verifies each, reporting per-file results with a summary. |
| `--parent <child.chd> <parent.chd>` | Opens a child CHD with its parent, reads hunks, and calls `CheckFileWithParent` for full verification. Also tests that opening without the parent returns `CHDERR_REQUIRES_PARENT`. |
| `--toc <file.chd>` | Parses and prints the table of contents for CD-ROM and GD-ROM CHD files, showing track numbers, types, sector sizes, and frame counts. |
| `--cue <file.chd> [binfile]` | Generates a CUE sheet for CD-ROM CHDs. Optionally specify a custom .bin filename (defaults to the CHD filename with .bin extension). |
| `--classify <file.chd>` | Detects and prints the CHD media type: CD-ROM, DVD-ROM, HDD, GD-ROM, or unknown/raw. |
| `--create <in.bin> <out.chd>` | Creates a CHD v5 from a raw binary via `CHDSharpEncoder`. Options: `-c <codecs>` (comma-separated `zlib,zstd,lzma,cdfl,none`), `-hs <bytes>`, `-us <bytes>`, `-t <n>` (parallel workers), `-ip <parent.chd>` (output delta child), `-v` (per-hunk ratio logging). Deep-verifies the result before exiting. |
| `--createcd <in.cue> <out.chd>` | Creates a CD CHD from a CUE/GDI/ISO/TOC image via `CHDSharpEncoder` (CHT2 metadata, audio byte-swap, 4-frame track padding). Same options as `--create`. |
| `--createhd <out.chd> --size N` | Creates a blank, zero-filled hard disk CHD without reading from an input file. Options: `--size N` (total size, supports K/M/G suffixes), `-chs C,H,S` (explicit CHS geometry), `-ss N` (sector size), `--ident <path>` (512-byte ATA IDENTIFY DEVICE file for IDNT metadata), `-c <codecs>`, `-hs <bytes>`, `-us <bytes>`, `-t <n>`, `-v`. Writes GDDD hard disk metadata with auto-derived or explicit CHS geometry. Deep-verifies the result before exiting. |
| `--copy <in.chd> <out.chd>` | Re-compresses an existing CHD via `ChdEncoder.Copy`: every hunk is re-encoded with the target codecs and all source metadata is cloned. `-ip <parent.chd>` resolves a child source; `-op <parent.chd>` makes the output a delta child of a different parent. Legacy CD/GD-ROM metadata (`CHCD`, `CHTR`, `CHGT`) is automatically upgraded to modern equivalents (`CHT2`, `CHGD`); use `--no-upgrade` to preserve legacy tags. Deep-verifies the result before exiting. |

---

## Output

### Directory / list verification

```
[PASS] V5 game.chd  sha1=abc123...def  (2.3s)
[FAIL] bad.chd  CHDERR_DECOMPRESSION_ERROR  (0.5s)
[SKIP] missing.chd  (not found)

==== Summary: 10 passed, 1 failed, 2 skipped, 13 total ====
```

While a file verifies, progress is reported in 10% steps via `IProgress<ChdProgress>`:

```
   10% game.chd  (67,108,864 / 671,088,640 bytes, 0.8s)
   50% game.chd  (335,544,320 / 671,088,640 bytes, 3.1s)
  100% game.chd  (671,088,640 / 671,088,640 bytes, 5.9s)
```

### Random-access test

```
Opened V5: 681574400 bytes, 10408 hunks x 65536 bytes
  ReadHunk(0) => CHDERR_NONE
  ReadHunk(5204) => CHDERR_NONE
  ReadHunk(10407) => CHDERR_NONE
  Full-image raw SHA1 MATCHES header raw SHA1
  Full-image MD5 MATCHES header MD5
```

### Parent/child test

```
Child:  child.chd
Parent: parent.chd
  Opened V5: 4019791872 bytes, 61440 hunks x 65536 bytes
  ReadHunk(0) => CHDERR_NONE
  ReadHunk(30720) => CHDERR_NONE
  ReadHunk(61439) => CHDERR_NONE
  CheckFileWithParent => CHDERR_NONE  (V5, sha1=abc123...)
  Open(child, no parent) => CHDERR_REQUIRES_PARENT  (expected)
```

### TOC (table of contents)

```
CD-ROM: 2 tracks
Total frames: 152262, 2448 bytes/frame (2352 data + 96 subcode)
ROM
Track  1: MODE1/2048  RW              frames=   6576 pre=    0 post=    0 pgtype=MODE1/2048 po=   0
Track  2: AUDIO       RW_RAW          frames=  43493 pre=  150 post=    0 pgtype=AUDIO       po=   2
```

### CUE sheet

```
FILE "game.bin" BINARY
  TRACK 01 MODE1/2048
    INDEX 01 00:00:00
  TRACK 02 AUDIO
    PREGAP 00:02:00
    INDEX 01 01:27:66
```

### Classify

```
game.chd: cd-rom
parent.chd: hdd
grom.chd: gd-rom
```

### Creating CHDs (`-v` verbose per-hunk logging)

```
Creating CHD: in.bin -> out.chd  (hunk 65536B, unit 4096B, codecs zlib)
  hunk      0/  1600  none       65536 ->      65536 B  (100.0 %)
  hunk      1/  1600  zlib       65536 ->       1234 B  (  1.9 %)
  ...
  Ratio: 52,431,872 / 104,857,600 bytes = 50.0 %  [none: 534, zlib: 1066]
  Created 52,442,112 bytes
  Verified OK (V5, sha1=abc123...)
```

Without `-v`, only the input/output line, file size, and verification result are printed.

---

## Building

```bash
# Build (requires .NET 10.0 SDK)
dotnet build CHDSharpCli/CHDSharpCli.csproj -c Release

# Run
dotnet run --project CHDSharpCli -- D:\CHD
# or run the built executable
CHDSharpCli/bin/Release/net10.0/CHDSharpCli.exe D:\CHD
```

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [Serilog](https://www.nuget.org/packages/Serilog/) | 4.4.0 | Structured logging |
| [Serilog.Extensions.Logging](https://www.nuget.org/packages/Serilog.Extensions.Logging/) | 10.0.0 | Bridges Serilog to `ILoggerFactory` |
| [Serilog.Sinks.Console](https://www.nuget.org/packages/Serilog.Sinks.Console/) | 6.1.1 | Console log output |
| `CHDSharpLib` | (project reference) | Core CHD library |
