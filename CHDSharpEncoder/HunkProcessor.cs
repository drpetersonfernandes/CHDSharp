using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using CHDSharpEncoder.Interfaces;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoder;

/// <summary>
/// The result of compressing one hunk, delivered by <see cref="HunkProcessor.CompressAll"/>
/// to its consumer callback **in hunk order**. The <see cref="Data"/> array is rented from a
/// buffer pool and is only valid for the duration of the callback invocation.
/// </summary>
/// <param name="HunkIndex">The zero-based hunk index.</param>
/// <param name="Compression">The winning compression type: a codec index (0-3) or <see cref="MapEntry.CompressionNone"/>.</param>
/// <param name="CompLength">The number of bytes to write from <see cref="Data"/> (the hunk size for <see cref="MapEntry.CompressionNone"/>).</param>
/// <param name="Crc16">CRC-16 of the uncompressed hunk data.</param>
/// <param name="Sha1">SHA-1 of the uncompressed hunk (20 bytes), for SELF-dedup lookups.</param>
/// <param name="Data">The data to append to the output file, or <c>null</c> when the consumer decides
/// this hunk is a SELF reference (nothing is stored on disk).</param>
public readonly record struct HunkResult(
    uint HunkIndex,
    byte Compression,
    uint CompLength,
    ushort Crc16,
    byte[] Sha1,
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
        for (var t = 0; t < taskCount; t++)
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
        var bestCodec = -1;
        byte[]? bestData = null;
        for (var i = 0; i < _syncCodecs.Length; i++)
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
    /// shape as CHDSharpLib's <see cref="CHDSharp.Chd.CheckFile(Stream,string,bool,IProgress{CHDSharp.Models.ChdProgress}?,System.Threading.CancellationToken)"/>): a single producer reads the
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

        using var pipeline = new CompressionPipeline(this, _taskCount, hunkCount, readHunk, rawSha1,
            onHunkConsumed, cancellationToken);
        pipeline.Run();
    }

    /// <summary>
    /// Owns the queues, cancellation, and tasks of one <see cref="CompressAll"/> run. The worker
    /// closures capture this container's fields rather than local variables that are disposed in
    /// the enclosing scope; <see cref="Dispose"/> is invoked by the caller's <c>using</c> only
    /// after <see cref="Run"/> has returned and all tasks are known to have completed.
    /// </summary>
    private sealed class CompressionPipeline : IDisposable
    {
        private readonly HunkProcessor _owner;
        private readonly int _taskCount;
        private readonly uint _hunkCount;
        private readonly Func<uint, byte[], int> _readHunk;
        private readonly Sha1 _rawSha1;
        private readonly Action<HunkResult> _onHunkConsumed;
        private readonly CancellationToken _cancellationToken;
        private readonly BlockingCollection<int> _toCompress;
        private readonly BlockingCollection<int> _toWrite;
        private readonly HunkItem[] _items;
        private readonly CancellationTokenSource _ts;
        private readonly List<Task> _tasks;
        private Exception? _error;
#pragma warning disable MA0158 // Use System.Threading.Lock — not available on net8.0
        private readonly object _errorLock = new();
#pragma warning restore MA0158

        public CompressionPipeline(HunkProcessor owner, int taskCount, uint hunkCount,
            Func<uint, byte[], int> readHunk, Sha1 rawSha1, Action<HunkResult> onHunkConsumed,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _taskCount = taskCount;
            _hunkCount = hunkCount;
            _readHunk = readHunk;
            _rawSha1 = rawSha1;
            _onHunkConsumed = onHunkConsumed;
            _cancellationToken = cancellationToken;
            _toCompress = new BlockingCollection<int>(taskCount * 8);
            _toWrite = new BlockingCollection<int>(taskCount * 8);
            _items = new HunkItem[hunkCount];
            for (var i = 0; i < hunkCount; i++)
            {
                _items[i] = new HunkItem();
            }

            _ts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _tasks = new List<Task>(taskCount + 1);
        }

        public void Run()
        {
            _tasks.Add(Task.Factory.StartNew(
                () => ProducerLoop(),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));

            for (var t = 0; t < _taskCount; t++)
            {
                var workerIndex = t;
                _tasks.Add(Task.Factory.StartNew(
                    () => WorkerLoop(workerIndex),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }

            try
            {
                // single consumer: results may arrive out of order, but are emitted in hunk order
                // so map offsets, dedup state, and block writes stay strictly sequential
                var next = 0;
                while (next < _hunkCount)
                {
                    var h = _toWrite.Take(_ts.Token);
                    _items[h].Done = true;
                    while (next < _hunkCount && _items[next].Done)
                    {
                        var item = _items[next];
                        _onHunkConsumed(new HunkResult(
                            (uint)next, item.Compression, item.CompLength, item.Crc16, item.Sha1, item.Data));
                        _owner.Reclaim(item);
                        next++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (_cancellationToken.IsCancellationRequested)
                    throw;
                // internal cancellation: an error was recorded and is rethrown below
            }

            if (_error != null)
                ExceptionDispatchInfo.Capture(_error).Throw();
            _cancellationToken.ThrowIfCancellationRequested();
        }

        public void Dispose()
        {
            _ts.Cancel();
            try
            {
                Task.WaitAll(_tasks.ToArray());
            }
            catch (Exception)
            {
                // tasks swallow their exceptions; nothing to rethrow here
            }

            _ts.Dispose();
            _toCompress.Dispose();
            _toWrite.Dispose();
        }

        private void ProducerLoop()
        {
            try
            {
                for (uint h = 0; h < _hunkCount; h++)
                {
                    var buffer = _owner._rawPool.Rent();
                    Array.Clear(buffer, 0, buffer.Length);
                    var hashBytes = _readHunk(h, buffer);

                    // the running raw SHA-1 is appended in hunk order on the producer thread
                    // (one serial pass; per-hunk hashing for dedup runs on the workers)
                    _rawSha1.Append(buffer, 0, hashBytes);

                    _items[h].Raw = buffer;
                    _toCompress.Add((int)h, _ts.Token);
                }

                // sentinels tell every worker to stop and return
                for (var i = 0; i < _taskCount; i++)
                    _toCompress.Add(-1, _ts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }
        }

        private void WorkerLoop(int workerIndex)
        {
            var codecs = _owner._workerCodecSets![workerIndex];
            try
            {
                while (true)
                {
                    var h = _toCompress.Take(_ts.Token);
                    if (h == -1)
                        return;

                    var item = _items[h];
                    var raw = item.Raw!;

                    // per-hunk hashing runs on the workers (parallel), not the producer
                    item.Sha1 = Sha1.Compute(raw);
                    item.Crc16 = Crc16.Compute(raw);

                    // try every codec and keep the smallest result that saves space
                    var bestCodec = -1;
                    var bestLen = int.MaxValue;
                    byte[]? bestData = null;
                    for (var i = 0; i < codecs.Length; i++)
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
                        _owner._rawPool.Return(raw);
                    }
                    else
                    {
                        // nothing compresses: hand the raw hunk buffer over as the stored block
                        item.Compression = MapEntry.CompressionNone;
                        item.CompLength = _owner._hunkBytes;
                        item.Data = raw;
                        item.DataIsRaw = true;
                        item.Raw = null;
                    }

                    _toWrite.Add(h, _ts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }
        }

        private void RecordError(Exception ex)
        {
            lock (_errorLock)
            {
                _error ??= ex;
            }

            _ts.Cancel();
        }
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
#pragma warning disable MA0158 // Use System.Threading.Lock — not available on net8.0
        private readonly object _lock = new();
#pragma warning restore MA0158

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