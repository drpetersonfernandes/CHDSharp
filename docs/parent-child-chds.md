---
layout: default
---

# Parent/Child CHDs

CHD supports **delta (incremental) images**: a *child* CHD stores only the hunks that differ from its *parent*; identical hunks become parent references. This is how MAME ships multi-disc or regional variants without duplicating identical data.

---

## How it works

- The child header stores the parent's `md5` (V1–V3) and/or `sha1` (V3–V5) hashes.
- Map entries of type `PARENT` point into the **parent's data** instead of local storage.
- In V1–V4, a parent reference is a direct **hunk index** into the parent.
- In V5, a parent reference is a **unit index** (unit = `hunkbytes / unitbytes` subdivision, e.g. 512-byte sectors inside 4096-byte hunks). References can be **unaligned** — a hunk may need the tail of parent hunk *N* and the head of parent hunk *N+1*; CHDSharp stitches the two halves.

```
child.chd                       parent.chd
┌───────────────────┐           ┌───────────────────┐
│ hunk 0: compressed│           │ hunk 0: data      │
│ hunk 1: PARENT→u4 │──────────▶│ hunk 1: data      │
│ hunk 2: compressed│           │ hunk 2: data      │
│ hunk 3: PARENT→u7 │──────┐    │ hunk 3: data      │
└───────────────────┘      └───▶│ hunk 4: data      │
                                └───────────────────┘
```

---

## Opening child CHDs

Three ways, matching the three `Open` overloads:

```csharp
// 1. Path-based: the library opens the parent and owns it.
var err = ChdFile.Open("child.chd", "parent.chd", out var child);
using (child) { ... }

// 2. External parent instance: caller keeps ownership, may share it.
ChdFile.Open("parent.chd", out var parent);
using (parent)
{
    foreach (var childPath in new[] { "child1.chd", "child2.chd" })
    {
        ChdFile.Open(childPath, parent, out var c);
        using (c) { /* read hunks; parent hunks resolve through `parent` */ }
    }
}

// 3. From streams.
using var childStream = File.OpenRead("child.chd");
ChdFile.Open(childStream, leaveOpen: false, parent, out var child2);
```

Async twins exist for all three (`OpenAsync`).

---

## Error semantics

| Situation | Result |
|-----------|--------|
| Child opened without a parent | `Chderrrequiresparent` |
| Supplied parent's `md5`/`sha1` does not match the child's stored parent hashes | `Chderrinvalidparent` |
| Parent-referenced hunk read when no parent is attached | `Chderrrequiresparent` (from `ReadHunk`) |

Parent validation happens at **open time**: the child's stored `parentmd5`/`parentsha1` is compared against the actual parent's `Md5`/`Sha1` (when both are non-empty).

---

## Verification

```csharp
var result = Chd.CheckFileWithParent("child.chd", "parent.chd");
if (result.IsSuccess)
    Console.WriteLine($"child V{result.Version} verified against parent");
```

`CheckFileWithParent` decompresses the child **and** the referenced parent hunks and validates every hash — this is the single-threaded counterpart of `CheckFile` (which is standalone-only).

---

## Reading through a child

From the consumer's point of view, reading is transparent:

```csharp
var err = ChdFile.Open("child.chd", "parent.chd", out var child);
using (child)
{
    var hunk = new byte[child.HunkBytes];
    for (uint i = 0; i < child.HunkCount; i++)
    {
        var herr = child.ReadHunk(i, hunk);   // local or parent data, same call
        if (herr != ChdError.Chderrnone) break;
    }
}
```

`Read`, `ReadAllBytes`, `EnumerateHunks`, and extraction all work identically on child CHDs.

---

## Hunk resolution details

`ReadHunk` resolves a map entry as follows:

1. `PARENT` → `ReadParentHunk`:
   - V1–V4 (and uncompressed V5 maps): direct parent hunk index.
   - V5 compressed maps: convert the **unit index** to parent hunk(s):
     - aligned → one parent hunk;
     - unaligned → two adjacent parent hunks stitched at the unit boundary.
2. `SELF` → follow the self-reference to the entry that holds real data (and use the decompressed cache when the block is repeated).
3. Otherwise → read local compressed bytes (stream or `Precache` buffer) and decompress.

The child keeps a reference to the parent (`_parent`); when the child was opened with a parent **path**, it owns the parent and disposes it together with itself.
