# Performance

CHDSharp is designed for both **batch verification throughput** (parallel) and **low-latency random access** (single-threaded, cached). This page documents the knobs and what to expect.

---

## Typical throughput

Measured on a modern desktop with the bundled corpus and typical game images (single-threaded I/O, Release build):

| Scenario | Throughput | Notes |
|----------|------------|-------|
| `CheckFile(deepCheck: true)` | ~200–400 MB/s | 8 parallel workers, bounded memory |
| `CheckFile(deepCheck: false)` | > 1 GB/s | Header-only |
| `ChdFile.Read()` sequential | ~150–300 MB/s | Single-threaded, last-hunk cache |
| `ChdFile.ReadHunk()` random | ~50–150 MB/s | Per-hunk re-decompression |

Actual numbers depend on the mix of codecs (LZMA is the slowest, raw/zstd the fastest), hunk size, and storage speed.

---

## Tuning parallelism

`Chd.TaskCount` controls the worker count for `CheckFile(deepCheck: true)` (default 8, valid range 1–64). It is a process-global setting — set it **before** calling:

```csharp
Chd.TaskCount = Environment.ProcessorCount;   // or a value you benchmarked
var result = Chd.CheckFile(stream, "game.chd", deepCheck: true);
```

Memory is bounded even with many workers:

- pooled buffers (`ArrayPool`) for compressed input, decompressed output, and caches;
- a semaphore capping in-flight repeat-block cache to **512 MiB** (`blocksToKeep = (512 MiB) / hunkbytes`).

---

## Caching model

### Repeat-block cache (verification)

`ChdBlockRead` detects hunks referenced multiple times (deduplicated `SELF` entries) and:

1. computes a **usage weight** per block (LZMA/Huffman/CD-LZMA blocks are the most expensive to re-decompress, so they get the highest priority),
2. keeps the top-N blocks' decompressed copies within the 512 MiB budget (`KeepMostRepeatedBlocks`),
3. serves repeated hunks from the cache instead of re-decompressing.

### Last-hunk cache (random access)

`ChdFile.Read()` caches the most recently read hunk, so byte ranges that stay within one hunk cost a single decompression.

### Multi-hunk LRU cache (libchdr #36)

`ChdFile.ReadHunk()` retains decompressed hunks in a configurable LRU cache so random reads that revisit hunks avoid re-decompression. Default size is 1 (equivalent to the single-hunk slot); set `CacheSize` or call `ConfigureCache(n)` to keep the last `n` distinct hunks (`n <= 1` disables it). Memory is capped at `CacheSize * HunkBytes`.

```csharp
chd.ConfigureCache(16);        // keep 16 decompressed hunks
chd.ReadHunk(100, buf);        // decompressed
chd.ReadHunk(100, buf);        // served from cache
```

Use a larger `CacheSize` when a workload performs random/scattered reads that repeatedly touch the same subset of hunks.

### Precache (whole file in RAM)

`ChdFile.Precache()` reads the **entire compressed file** into memory once. Every subsequent hunk read is served from RAM — no stream seeks, no disk I/O. Ideal for:

- random-access workloads over slow/remote storage,
- many small reads across a large image,
- repeated passes over the same file.

```csharp
var err = chd.Precache();
if (err != ChdError.Chderrnone) { /* >2 GiB file → out of memory; IO failure */ }

// from here on, ReadHunk/Read serve from RAM
```

`Precache()` is idempotent, restores the stream position, and returns `Chderroutofmemory` for files larger than 2 GiB or when allocation fails.

---

## Reusable codec state

`ChdCodecState` keeps per-codec scratch alive across hunks:

- LZMA dictionary window (sized to the hunk),
- zstd decompressor instance,
- FLAC decoder + audio buffers,
- Huffman lookup tables (1 MiB each for AVHuff contexts).

This avoids reallocating the most expensive buffers on every hunk — the main reason sequential `Read` stays in the hundreds of MB/s.

---

## Memory profile

| Operation | Peak memory |
|-----------|-------------|
| `CheckFile(deepCheck: true)` | ~workers × hunkbytes × 3 (pooled) + ≤ 512 MiB repeat cache |
| `ReadHunk` / `Read` | ~2–3 × hunkbytes + codec state |
| `Precache` | file size + codec state |
| `ReadAllBytes` | image size (fails with `Chderroutofmemory` > 2 GiB) |

---

## Practical tips

- **Batch-verify in parallel** with `CheckFile`, not `ReadAllBytes` per file.
- **Extract** with `ExtractToDirectory` (streams hunk-by-hunk) rather than `ReadAllBytes` for large images.
- **Random access patterns** benefit from `Precache` when the underlying stream is slow; on local NVMe the difference is usually small.
- **Hunk size matters**: larger hunks amortize per-hunk overhead but waste space on small reads. This is a property of the CHD file itself (set by `chdman`), not the library.
- For **sequential streaming**, prefer `EnumerateHunks()` (single buffer, no per-call allocation) over repeated `Read` calls.
