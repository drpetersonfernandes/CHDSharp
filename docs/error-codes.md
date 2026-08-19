---
layout: default
---

# Error Codes

Every public API returns (or reports) a `ChdError` value instead of throwing. This page lists all values, their meaning, and where they are produced.

| Value | `GetMessage()` | Produced when |
|-------|----------------|----------------|
| `Chderrnone` | "No error" | Success. |
| `Chderrnointerface` | "No interface available" | (reserved for MAME API parity) |
| `Chderroutofmemory` | "Out of memory" | Image > 2 GiB for `ReadAllBytes`/`Precache`, or allocation failure. |
| `Chderrinvalidfile` | "Not a valid CHD file" | Bad magic, bad header length, or unreadable header. |
| `Chderrinvalidparameter` | "Invalid parameter" | Non-seekable/unreadable stream, buffer too small, out-of-range read args. |
| `Chderrinvaliddata` | "Invalid or corrupt data" | Header validation failures, corrupt map/metadata, malformed compressed hunk structure. |
| `Chderrfilenotfound` | "File not found" | `Open(path)` on a missing file. |
| `Chderrrequiresparent` | "Child CHD requires a parent" | Opening a child without a parent; reading a parent hunk with none attached. |
| `Chderrfilenotwriteable` | "File is not writable" | (reserved) |
| `Chderrreaderror` | "Read error" | IO failure while reading the stream (header, metadata, precache). |
| `Chderrwriteerror` | "Write error" | IO failure while writing extraction output. |
| `Chderrcodecerror` | "Codec error" | A codec slot is missing/uninitialized (e.g. secondary codec not set). |
| `Chderrinvalidparent` | "Invalid or incompatible parent CHD" | Supplied parent's hashes do not match the child's stored parent hashes. |
| `Chderrhunkoutofrange` | "Hunk index out of range" | `ReadHunk` with `hunknum >= HunkCount`. |
| `Chderrdecompressionerror` | "Decompression failed" | CRC mismatch after decompression, codec failure, or unexpected exception inside hunk decoding. |
| `Chderrcompressionerror` | "Compression failed" | (reader-side: decompression failure in `ReadBlock`; the encoder throws instead) |
| `Chderrcantcreatefile` | "Cannot create file" | (reserved) |
| `Chderrcantverify` | "Cannot verify CHD" | (reserved) |
| `Chderrnotsupported` | "Feature not supported" | (reserved) |
| `Chderrmetadatanotfound` | "Metadata not found" | `GetMetadata` found no entry matching the tag/index. |
| `Chderrinvalidmetadatasize` | "Invalid metadata size" | (reserved) |
| `Chderrunsupportedversion` | "Unsupported CHD version" | Version outside 1–5. |
| `Chderrverifyincomplete` | "Verification incomplete" | (reserved) |
| `Chderrinvalidmetadata` | "Invalid or corrupt metadata" | Combined-SHA1 (raw data + metadata) mismatch in deep verification. |
| `Chderrinvalidstate` | "Invalid state" | (reserved) |
| `Chderroperationpending` | "Operation already pending" | (reserved) |
| `Chderrnoasyncoperation` | "No async operation in progress" | (reserved) |
| `Chderrunsupportedformat` | "Unsupported format" | (reserved for unknown codec tags/unknown formats) |
| `Chderrcannotopenfile` | "Cannot open file" | `Open(path)` hit an IO/access error creating the `FileStream`. |

> Values marked *(reserved)* exist for **MAME/libchdr API parity** — they are defined with matching messages but are not currently produced by any code path. The enum intentionally mirrors the C `chd_error` list 1:1 (plus `Chderrcannotopenfile`).

---

## Getting the message

```csharp
using CHDSharp;

ChdError err = ChdFile.Open("missing.chd", out _);
Console.WriteLine(err.GetMessage());          // "File not found"
Console.WriteLine((int)err);                  // numeric value
```

The `GetMessage()` extension covers every value; unknown values fall back to `"Unknown error (<value>)"`.

---

## Error-handling patterns

```csharp
// Open errors
var err = ChdFile.Open(path, out var chd);
switch (err)
{
    case ChdError.Chderrnone:
        break;
    case ChdError.Chderrfilenotfound:
    case ChdError.Chderrcannotopenfile:
        Console.WriteLine("Cannot open the file");
        return;
    case ChdError.Chderrrequiresparent:
        Console.WriteLine("This is a child CHD — supply its parent");
        return;
    case ChdError.Chderrinvalidparent:
        Console.WriteLine("The supplied parent does not match");
        return;
    default:
        Console.WriteLine(err.GetMessage());
        return;
}

// Read errors
var herr = chd!.ReadHunk(0, hunk);
if (herr == ChdError.Chderrdecompressionerror)
{
    // log + report; the library already logged the inner exception via ILogger
}
```

See [Troubleshooting](troubleshooting.md) for the common error paths and their causes.
