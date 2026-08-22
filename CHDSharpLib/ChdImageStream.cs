using CHDSharp.Models;

namespace CHDSharp;

/// <summary>
/// A read-only, seekable <see cref="Stream"/> that wraps a <see cref="ChdFile"/>, providing
/// sequential or random access to the decompressed image as a flat byte stream.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="ChdFile.OpenAsStream"/> or <see cref="ChdFile.OpenAsStreamAsync"/> to create
/// an instance. The stream decompresses hunks on demand via the underlying <see cref="ChdFile"/>;
/// a single hunk is cached, so sequential reads within the same hunk avoid re-decoding.
/// </para>
/// <para>
/// Disposing the stream optionally disposes the underlying <see cref="ChdFile"/> depending on
/// how the stream was created. The stream is NOT thread-safe; callers must serialize access.
/// </para>
/// </remarks>
public sealed class ChdImageStream : Stream
{
    private readonly ChdFile _chd;
    private readonly bool _ownsChd;
    private ulong _position;
    private bool _disposed;

    internal ChdImageStream(ChdFile chd, bool ownsChd)
    {
        _chd = chd ?? throw new ArgumentNullException(nameof(chd));
        _ownsChd = ownsChd;
    }

    /// <inheritdoc/>
    public override bool CanRead => !_disposed;

    /// <inheritdoc/>
    public override bool CanSeek => !_disposed;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length
    {
        get
        {
            ThrowIfDisposed();
            return (long)_chd.TotalBytes;
        }
    }

    /// <inheritdoc/>
    public override long Position
    {
        get
        {
            ThrowIfDisposed();
            return (long)_position;
        }
        set
        {
            ThrowIfDisposed();
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Position cannot be negative.");

            _position = (ulong)value;
        }
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        // Read-only stream; nothing to flush.
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count == 0 || _position >= _chd.TotalBytes)
            return 0;

        var available = (int)Math.Min((ulong)count, _chd.TotalBytes - _position);
        var err = _chd.Read(_position, buffer, offset, available);
        if (err != ChdError.Chderrnone)
            throw new IOException($"CHD read failed at offset {_position}: {err}");

        _position += (ulong)available;
        return available;
    }

#if NET7_0_OR_GREATER
    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();

        if (buffer.IsEmpty || _position >= _chd.TotalBytes)
            return 0;

        var available = (int)Math.Min((ulong)buffer.Length, _chd.TotalBytes - _position);
        var err = _chd.Read(_position, buffer, available);
        if (err != ChdError.Chderrnone)
            throw new IOException($"CHD read failed at offset {_position}: {err}");

        _position += (ulong)available;
        return available;
    }
#endif

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count == 0 || _position >= _chd.TotalBytes)
            return 0;

        var available = (int)Math.Min((ulong)count, _chd.TotalBytes - _position);
        var err = await _chd.ReadAsync(_position, buffer, offset, available, cancellationToken).ConfigureAwait(false);
        if (err != ChdError.Chderrnone)
            throw new IOException($"CHD read failed at offset {_position}: {err}");

        _position += (ulong)available;
        return available;
    }

#if NET7_0_OR_GREATER
    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (buffer.IsEmpty || _position >= _chd.TotalBytes)
            return 0;

        var available = (int)Math.Min((ulong)buffer.Length, _chd.TotalBytes - _position);
        var temp = new byte[available];
        var err = await _chd.ReadAsync(_position, temp, 0, available, cancellationToken).ConfigureAwait(false);
        if (err != ChdError.Chderrnone)
            throw new IOException($"CHD read failed at offset {_position}: {err}");

        temp.AsSpan(0, available).CopyTo(buffer.Span);
        _position += (ulong)available;
        return available;
    }
#endif

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();

        var newPos = origin switch
        {
            SeekOrigin.Begin => offset < 0
                ? throw new ArgumentOutOfRangeException(nameof(offset), "Seek offset cannot be negative from beginning.")
                : (ulong)offset,
            SeekOrigin.Current => offset < 0
                ? (ulong)(-(long)_position < offset ? throw new ArgumentOutOfRangeException(nameof(offset), "Seek before beginning of stream.") : _position + (ulong)offset)
                : _position + (ulong)offset,
            SeekOrigin.End => offset > 0
                ? throw new ArgumentOutOfRangeException(nameof(offset), "Seek offset cannot be positive from end.")
                : _chd.TotalBytes - (ulong)(-offset),
            _ => throw new ArgumentException("Invalid SeekOrigin.", nameof(origin))
        };

        _position = newPos;
        return (long)_position;
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException("ChdImageStream is read-only.");
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("ChdImageStream is read-only.");
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing && _ownsChd)
                _chd.Dispose();
        }

        base.Dispose(disposing);
    }

#if NET7_0_OR_GREATER
    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_ownsChd)
                await _chd.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
#endif

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
