using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace CHDSharpEncoder;

/// <summary>
/// The result of compressing one hunk, delivered by <see cref="HunkProcessor.CompressAll"/>
/// to its consumer callback **in hunk order**. The <see cref="Data"/> array is rented from a
/// buffer pool and is only valid for the duration of the callback invocation.
/// </summary>
public readonly record struct HunkResult(
    /// <summary>The zero-based hunk index.</summary>
    uint HunkIndex,
    /// <summary>The winning compression type: a codec index (0-3) or <see cref="MapEntry.COMPRESSION_NONE"/>.</summary>
    byte Compression,
    /// <summary>The number of bytes to write from <see cref="Data"/> (the hunk size for <see cref="MapEntry.COMPRESSION_NONE"/>).</summary>
    uint CompLength,
    /// <summary>CRC-16 of the uncompressed hunk data.</summary>
    ushort Crc16,
    /// <summary>SHA-1 of the uncompressed hunk (20 bytes), for SELF-dedup lookups.</summary>
    byte[] Sha1,
    /// <summary>The data to append to the output file, or <c>null</c> when the consumer decides
    /// this hunk is a SELF reference (nothing is stored on disk).</summary>
    byte[]? Data);

/// <summary>Processes raw hunk data for CHD v5 encoding, handling compression and map entry generation.</summary>
public class HunkProcessor
{
    private readonly uint _hunkBytes;
    private readonly int _taskCount;
    private readonly IChdCodec[] _syncCodecs;
    private readonly IChdCodec[][]? _workerCodecSets;
    private readonly ByteArrayPool _rawPool;

    /// <summary>Initializes a new <see cref="HunkProcessor"/> for the specified hunk size.</summary>
    /// <param name="hunkBytes">The expected size of each hunk in bytes.</param>
    public HunkProcessor(uint hunkBytes)
        : this(hunkBytes, [new ZlibCodec()])
    {
    }

    /// <summary>Initializes a new <see cref="HunkProcessor"/> with the given codecs.</summary>
    /// <param name="hunkBytes">The expected size of each hunk in bytes.</param>
    /// <param name="codecs">The codecs to try per hunk, in order; the smallest output wins
    /// (compression types 0..3 map to codec indices, like MAME's <c>find_best_compressor</c>).</param>
    /// <remarks>This constructor is single-threaded: the supplied instances are used as-is
    /// (codecs are not guaranteed thread-safe, so they must not be shared across threads).
    /// Use the codec-tag constructor for parallel compression.</remarks>
    public HunkProcessor(uint hunkBytes, IReadOnlyList<IChdCodec> codecs)
    {
        _hunkBytes = hunkBytes;
        _syncCodecs = codecs.ToArray();
        _taskCount = 1;
        _rawPool = new ByteArrayPool((int)hunkBytes);
    }

    /// <summary>
    /// Initializes a parallel <see cref="HunkProcessor"/>: one persistent set of codec instances
    /// per worker, so stateful codecs (zstd handles, FLAC scratch buffers, ...) are never
    /// shared across threads.
    /// </summary>
    /// <param name="hunkBytes">The expected size of each hunk in bytes.</param>
    /// <param name="codecTags">The codec tags to instantiate per worker (see <see cref="ChdCodecs.CreateAll"/>).</param>
    /// <param name="taskCount">The number of parallel compression workers (1-64).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="taskCount"/> is less than 1.</exception>
    public HunkProcessor(uint hunkBytes, IReadOnlyList<uint> codecTags, int taskCount)
    {
        if (taskCount < 1)
            throw new ArgumentOutOfRangeException(nameof(taskCount), taskCount, "TaskCount must be between 1 and 64.");

        _hunkBytes = hunkBytes;
        _taskCount = taskCount;
        _workerCodecSets = new IChdCodec[taskCount][];
        for (int t = 0; t < taskCount; t++)
        {
            _workerCodecSets[t] = ChdCodecs.CreateAll(codecTags, hunkBytes);
        }

        _syncCodecs = _workerCodecSets[0];
        _rawPool = new ByteArrayPool((int)hunkBytes);
    }

    /// <summary>Compresses a raw hunk with the best available codec and produces its map entry and output data.</summary>
    /// <param name="rawHunk">The uncompressed hunk data.</param>
    /// <param name="fileOffset">The byte offset of this hunk in the output file.</param>
    /// <returns>A tuple containing the map entry and the data to write (compressed or raw).</returns>
    /// <remarks>Synchronous, single-threaded path; see <see cref="CompressAll"/> for the parallel pipeline.</remarks>
    public (MapEntry Entry, byte[] Data) ProcessHunk(byte[] rawHunk, long fileOffset)
    {
        if (rawHunk.Length != _hunkBytes)
            throw new ArgumentException($"Hunk size mismatch: expected {_hunkBytes}, got {rawHunk.Length}");

        var crc16 = Crc16.Compute(rawHunk);

        // try every codec and keep the smallest result that saves space
        int bestCodec = -1;
        byte[]? bestData = null;
        for (int i = 0; i < _syncCodecs.Length; i++)
        {
            var candidate = _syncCodecs[i].Compress(rawHunk);
            if (candidate != null && (bestData == null || candidate.Length < bestData.Length))
            {
                bestCodec = i;
                bestData = candidate;
            }
        }

        if (bestCodec >= 0)
        {
            return (
                new MapEntry
                {
                    Compression = (byte)bestCodec,
                    CompLength = (uint)bestData!.Length,
                    Offset = (ulong)fileOffset,
                    Crc16 = crc16
                },
                bestData
            );
        }

        return (
            new MapEntry
            {
                Compression = MapEntry.CompressionNone,
                CompLength = _hunkBytes,
                Offset = (ulong)fileOffset,
                Crc16 = crc16
            },
            (byte[])rawHunk.Clone()
        );
    }

    /// <summary>
    /// Compresses all hunks of an image with a producer→worker→consumer pipeline (the same
    /// shape as CHDSharpLib's <see cref="CHDSharp.Chd.CheckFile"/>): a single producer reads the
    /// raw hunks in order and maintains the running raw SHA-1; <see cref="_taskCount"/> workers
    /// hash (SELF-dedup SHA-1 + CRC-16) and compress each hunk with their own persistent codec
    /// instances; a single consumer delivers the results to <paramref name="onHunkConsumed"/> in
    /// hunk order, so map offsets, dedup, and block writes stay strictly sequential.
    /// </summary>
    /// <param name="hunkCount">The number of hunks in the image.</param>
    /// <param name="readHunk">Reads hunk <c>hunkIndex</c> into <c>buffer</c> (exactly
    /// <c>hunkBytes</c>; the tail of a partial final hunk must be zero-filled). Returns the
    /// number of valid bytes to fold into <paramref name="rawSha1"/> (the hunk size for
    /// whole-hunk sources, the partial read length for a non-aligned final hunk).</param>
    /// <param name="rawSha1">The running raw SHA-1 of the image; the producer appends each hunk
    /// in order, so call <see cref="Sha1.Finish"/> after this method returns.</param>
    /// <param name="onHunkConsumed">Invoked once per hunk, in hunk order, on the calling thread.
    /// The <see cref="HunkResult.Data"/> buffer is owned by this processor and reclaimed when the
    /// callback returns; do not retain it.</param>
    /// <param name="cancellationToken">Cancels the pipeline; <see cref="OperationCanceledException"/>
    /// is thrown when cancellation is requested.</param>
    /// <exception cref="InvalidOperationException">The processor was built with the instance-based
    /// constructor, which cannot share codecs across worker threads.</exception>
    public void CompressAll(uint hunkCount, Func<uint, byte[], int> readHunk, Sha1 rawSha1,
        Action<HunkResult> onHunkConsumed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readHunk);
        ArgumentNullException.ThrowIfNull(rawSha1);
        ArgumentNullException.ThrowIfNull(onHunkConsumed);
        if (hunkCount == 0)
            return;

        if (_workerCodecSets == null)
            throw new InvalidOperationException(
                "Parallel compression requires the codec-tag constructor; codec instances are not thread-safe to share.");

        var taskCount = _taskCount;
        var queueCapacity = taskCount * 8;
        using var toCompress = new BlockingCollection<int>(queueCapacity);
        using var toWrite = new BlockingCollection<int>(queueCapacity);
        var items = new HunkItem[hunkCount];
        for (int i = 0; i < hunkCount; i++)
        {
            items[i] = new HunkItem();
        }

        var ts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new List<Task>(taskCount + 1);
        Exception? error = null;
        var errorLock = new object();

        void RecordError(Exception ex)
        {
            lock (errorLock)
            {
                error ??= ex;
            }

            ts.Cancel();
        }

        tasks.Add(Task.Factory.StartNew(
            () => ProducerLoop(hunkCount, readHunk, rawSha1, toCompress, items, ts, RecordError),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));

        for (int t = 0; t < taskCount; t++)
        {
            int workerIndex = t;
            tasks.Add(Task.Factory.StartNew(
                () => WorkerLoop(workerIndex, toCompress, toWrite, items, ts, RecordError),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        }

        try
        {
            // single consumer: results may arrive out of order, but are emitted in hunk order
            // so map offsets, dedup state, and block writes stay strictly sequential
            int next = 0;
            while (next < hunkCount)
            {
                var h = toWrite.Take(ts.Token);
                items[h].Done = true;
                while (next < hunkCount && items[next].Done)
                {
                    var item = items[next];
                    onHunkConsumed(new HunkResult(
                        (uint)next, item.Compression, item.CompLength, item.Crc16, item.Sha1, item.Data));
                    Reclaim(item);
                    next++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            // internal cancellation: an error was recorded and is rethrown below
        }
        finally
        {
            ts.Cancel();
            try
            {
                Task.WaitAll(tasks.ToArray());
            }
            catch (Exception)
            {
                // tasks swallow their exceptions; nothing to rethrow here
            }

            ts.Dispose();
        }

        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Per-hunk state shared between the producer, workers, and consumer.</summary>
    private sealed class HunkItem
    {
        /// <summary>The rented raw hunk buffer; <c>null</c> once handed off (COMPRESSION_NONE) or returned to the pool.</summary>
        public byte[]? Raw;
        /// <summary>SHA-1 of the raw hunk (20 bytes), computed by a worker for SELF-dedup.</summary>
        public byte[] Sha1 = Array.Empty<byte>();
        /// <summary>CRC-16 of the raw hunk, computed by a worker.</summary>
        public ushort Crc16;
        /// <summary>The winning compression type (codec index or COMPRESSION_NONE), set by a worker.</summary>
        public byte Compression;
        /// <summary>The number of bytes to write from <see cref="Data"/>.</summary>
        public uint CompLength;
        /// <summary>The result data (rented compressed buffer, or the raw buffer for COMPRESSION_NONE).</summary>
        public byte[]? Data;
        /// <summary><c>true</c> when <see cref="Data"/> is the raw hunk buffer (COMPRESSION_NONE), which returns to the raw pool.</summary>
        public bool DataIsRaw;
        /// <summary>Set by the consumer when the result has been taken from the results queue.</summary>
        public bool Done;
    }

    private void ProducerLoop(uint hunkCount, Func<uint, byte[], int> readHunk, Sha1 rawSha1,
        BlockingCollection<int> toCompress, HunkItem[] items, CancellationTokenSource ts, Action<Exception> recordError)
    {
        try
        {
            for (uint h = 0; h < hunkCount; h++)
            {
                var buffer = _rawPool.Rent();
                Array.Clear(buffer, 0, buffer.Length);
                int hashBytes = readHunk(h, buffer);

                // the running raw SHA-1 is appended in hunk order on the producer thread
                // (one serial pass; per-hunk hashing for dedup runs on the workers)
                rawSha1.Append(buffer, 0, hashBytes);

                items[h].Raw = buffer;
                toCompress.Add((int)h, ts.Token);
            }

            // sentinels tell every worker to stop and return
            for (int i = 0; i < _taskCount; i++)
                toCompress.Add(-1, ts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            recordError(ex);
        }
    }

    private void WorkerLoop(int workerIndex, BlockingCollection<int> toCompress, BlockingCollection<int> toWrite,
        HunkItem[] items, CancellationTokenSource ts, Action<Exception> recordError)
    {
        var codecs = _workerCodecSets![workerIndex];
        try
        {
            while (true)
            {
                var h = toCompress.Take(ts.Token);
                if (h == -1)
                    return;

                var item = items[h];
                var raw = item.Raw!;

                // per-hunk hashing runs on the workers (parallel), not the producer
                item.Sha1 = Sha1.Compute(raw);
                item.Crc16 = Crc16.Compute(raw);

                // try every codec and keep the smallest result that saves space
                int bestCodec = -1;
                int bestLen = int.MaxValue;
                byte[]? bestData = null;
                for (int i = 0; i < codecs.Length; i++)
                {
                    var candidate = codecs[i].Compress(raw);
                    if (candidate != null && candidate.Length < bestLen)
                    {
                        bestLen = candidate.Length;
                        bestCodec = i;
                        bestData = candidate;
                    }
                }

                if (bestCodec >= 0)
                {
                    item.Compression = (byte)bestCodec;
                    item.CompLength = (uint)bestLen;
                    item.Data = ArrayPool<byte>.Shared.Rent(bestLen);
                    Array.Copy(bestData!, 0, item.Data, 0, bestLen);
                    item.DataIsRaw = false;
                    item.Raw = null;
                    _rawPool.Return(raw);
                }
                else
                {
                    // nothing compresses: hand the raw hunk buffer over as the stored block
                    item.Compression = MapEntry.CompressionNone;
                    item.CompLength = _hunkBytes;
                    item.Data = raw;
                    item.DataIsRaw = true;
                    item.Raw = null;
                }

                toWrite.Add(h, ts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            recordError(ex);
        }
    }

    /// <summary>Returns the buffers of a consumed hunk to their pools (called after the consumer callback returns).</summary>
    private void Reclaim(HunkItem item)
    {
        if (item.Data != null)
        {
            if (item.DataIsRaw)
                _rawPool.Return(item.Data);
            else
                ArrayPool<byte>.Shared.Return(item.Data);
            item.Data = null;
        }

        item.Sha1 = Array.Empty<byte>();
        item.Raw = null;
    }

    /// <summary>A small thread-safe pool of fixed-size byte arrays (raw hunk buffers).</summary>
    private sealed class ByteArrayPool
    {
        private readonly int _arraySize;
        private readonly Stack<byte[]> _pool = new();
        private readonly object _lock = new();

        public ByteArrayPool(int arraySize)
        {
            _arraySize = arraySize;
        }

        public byte[] Rent()
        {
            lock (_lock)
            {
                if (_pool.Count > 0)
                    return _pool.Pop();
            }

            return new byte[_arraySize];
        }

        public void Return(byte[] buffer)
        {
            if (buffer.Length != _arraySize)
                throw new ArgumentException($"Pooled buffer size mismatch: expected {_arraySize}, got {buffer.Length}");

            lock (_lock)
            {
                _pool.Push(buffer);
            }
        }
    }
}