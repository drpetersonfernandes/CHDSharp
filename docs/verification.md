---
layout: default
---

# Verification

CHDSharp offers four levels of integrity checking: a fast header sniff, a full header DTO read, a header-only check, and a **full deep verification** that decompresses every hunk and validates every checksum the format defines.

---

## Levels of checking

| Level | API | What it does | Cost |
|-------|-----|--------------|------|
| Sniff | `Chd.IsChdFile(path)` / `Chd.CheckHeader(stream, ...)` | Validates magic + version + header length. | Reads 16–20 bytes. |
| Header DTO | `Chd.ReadHeader(path, out ChdHeaderInfo?)` | Parses the **full** header into a DTO (codec slots, sizes, hashes, parent linkage, unit info) without keeping the file open. | Reads the header (plus a small metadata peek for V1–V4 unit size). |
| Header-only | `Chd.CheckFile(stream, name, deepCheck: false)` | Parses and validates the full header (codec slots, sizes, parent linkage). | Reads the header. |
| **Deep** | `Chd.CheckFile(stream, name, deepCheck: true)` | Decompresses **every hunk** in parallel, verifies per-hunk CRCs, and recomputes MD5 / SHA1 / rawsha1 / combined metadata SHA1. | Reads the whole file; ~200–400 MB/s typical. |
| Chain | `Chd.CheckFileWithParent(child, parent)` | Deep verification of a (possibly child) CHD, resolving parent hunks. Single-threaded. | Reads child + referenced parent hunks. |

---

## Deep verification semantics

For every hunk, CHDSharp verifies:

- **V3/V4:** CRC-32 of the decompressed hunk against the map entry (unless the NO_CRC flag is set).
- **V5:** CRC-16 of the decompressed hunk against the map entry.

Then, across the whole image:

- **MD5** of the raw decompressed data — verified when the header stores one (V1–V3).
- **`rawsha1`** — SHA1 of the raw decompressed data (V3–V5).
- **`sha1`** — the combined hash: `SHA1(rawsha1 ‖ sorted entry hashes)` over checksummed metadata entries (V4–V5).
- **Metadata chain integrity** — cycle detection, entry-size caps, and readable offsets.

A mismatch at any level produces `Chderrdecompressionerror` (per-hunk CRC) or `Chderrinvalidmetadata` (combined hash), and the offending hunk index/bytes are not required to be exposed — the caller gets the error code.

### Hashing rules per version

| Version | MD5 | rawsha1 | combined sha1 | per-hunk |
|---------|-----|---------|---------------|----------|
| V1 | raw data | — | — | none |
| V2 | raw data | — | — | none |
| V3 | raw data | raw data | — | CRC32 |
| V4 | — | raw data | raw + metadata | CRC32 |
| V5 | — | raw data | raw + metadata | CRC16 |

---

## Parallelism and memory

`CheckFile(deepCheck: true)` runs a bounded pipeline (see [Architecture](architecture.md#parallel-verification)):

- **Producer** reads compressed hunks sequentially from the stream.
- **Workers** (default **8**, configurable via `Chd.TaskCount`, range 1–64) decompress hunks in parallel using pooled buffers.
- **Hasher** consumes decompressed hunks **in order**, feeds the hash algorithms, and releases buffers.

Memory is bounded by:

- an `ArrayPool` per buffer class (input/output/cache), and
- a semaphore limiting in-flight decompressed repeat-blocks to a **512 MiB budget** (`blocksToKeep = 512 MiB / hunkbytes`).

`Chd.TaskCount` is a process-global setting — set it **before** calling `CheckFile`:

```csharp
Chd.TaskCount = 16;   // tune for your CPU/memory
var result = Chd.CheckFile(stream, "game.chd", deepCheck: true);
```

### Progress reporting

All long operations accept an optional `IProgress<ChdProgress>` and report after every decompressed hunk:

```csharp
var progress = new Progress<ChdProgress>(p =>
    Console.WriteLine($"{p.Percent:F0}% — {p.BytesProcessed:N0}/{p.TotalBytes:N0} bytes"));

var result = Chd.CheckFile(stream, "game.chd", deepCheck: true, progress);
```

For `CheckFile(deepCheck: true)` the reports are emitted in hunk order from the hasher stage; `new Progress<ChdProgress>(...)` marshals them to the capturing thread/context. The same parameter exists on `CheckFileWithParent`, `ReadAllBytes`, `EnumerateHunks`, and `ExtractToDirectory`. Defaults to `null` (no reporting) everywhere.

All of these also accept an optional trailing `CancellationToken` and throw `OperationCanceledException` when cancelled. For deep verification the token is linked into the pipeline's internal `CancellationTokenSource`, so cancelling stops the workers immediately and the method throws OCE instead of reporting a bogus partial-hash mismatch.

---

## Child CHD verification

```csharp
var result = Chd.CheckFileWithParent("child.chd", "parent.chd");

// Or with an already-open parent (library does NOT take ownership):
var perr = ChdFile.Open("parent.chd", out var parent);
var r2 = Chd.CheckFileWithParent("child.chd", "parent.chd"); // path-based is simplest

// Out-parameter variant
var err = Chd.CheckFileWithParent("child.chd", null,
    out var version, out var sha1, out var md5);
```

Parent mismatches are caught at **open time** (`Chderrinvalidparent`), and missing parents produce `Chderrrequiresparent`. During deep verification, parent-referenced hunks are read through the parent instance and hashed as if they were local.

> `CheckFileWithParent` is single-threaded by design (it walks parent chains); use `CheckFile` for standalone parallel verification.

---

## Reading verification results

```csharp
var result = Chd.CheckFile(File.OpenRead("game.chd"), "game.chd", deepCheck: true);

if (result.IsSuccess)
{
    Console.WriteLine($"V{result.Version}");
    Console.WriteLine($"SHA1: {result.Sha1Hex}");
    Console.WriteLine($"MD5:  {result.Md5Hex}");
}
else
{
    Console.WriteLine($"Failed: {result.Error.GetMessage()}");
}

// Deconstruction
var (err, ver, sha1, md5) = result;
```

---

## CLI

The same verification logic is exposed by the CLI:

```bash
CHDSharpCli D:\CHD            # deep-verify every .chd under D:\CHD
CHDSharpCli --list files.txt  # deep-verify a list of paths
CHDSharpCli --parent child.chd parent.chd
```

A successful run prints the A/V metadata and reports `Valid`; failures print the offending file and the `ChdError`.

---

## Integrity caveats

- V1/V2 entries have no per-hunk CRC by design; only the whole-image MD5 is checked when present.
- V3/V4 CRC32 validation is **stricter than libchdr**, which stores but never checks V3/V4 CRCs — CHDSharp follows MAME's behavior and validates them (respecting the NO_CRC flag).
- If the header hashes are all-zero (a valid but unverified CHD), deep verification still decompresses everything and validates per-hunk CRCs, but cannot cross-check the whole image.
