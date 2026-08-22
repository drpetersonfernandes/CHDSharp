using System.Diagnostics;
using System.Security.Cryptography;
using CHDSharp.Models;
using System.IO.Hashing;

namespace CHDSharp;

/// <summary>Hash algorithms that <see cref="Chd.ComputeHashes"/> can compute over CHD content.</summary>
[Flags]
public enum ChdHashType
{
    /// <summary>No hash algorithms selected.</summary>
    None = 0x0000,

    /// <summary>SHA-1 (20 bytes) — the hash stored in V3-V5 CHD headers.</summary>
    Sha1 = 0x0001,

    /// <summary>SHA-256 (32 bytes).</summary>
    Sha256 = 0x0002,

    /// <summary>CRC-32 (IEEE 802.3, 4 bytes).</summary>
    Crc32 = 0x0004,

    /// <summary>XXH3-64 (8 bytes), the fast non-cryptographic hash used by Redump/CHDlite.</summary>
    Xxh3 = 0x0008
}

/// <summary>Hashes of one contiguous region of a CHD's decompressed content.</summary>
/// <param name="TrackNumber">The 1-based CD track number for per-track hashing, or <c>null</c> for the whole image.</param>
/// <param name="StartOffset">Byte offset of the region within the decompressed image.</param>
/// <param name="Length">Length of the region in bytes.</param>
/// <param name="Sha1">SHA-1 of the region, or <c>null</c> if not requested.</param>
/// <param name="Sha256">SHA-256 of the region, or <c>null</c> if not requested.</param>
/// <param name="Crc32">CRC-32 of the region, or <c>null</c> if not requested.</param>
/// <param name="Xxh3">XXH3-64 of the region, or <c>null</c> if not requested.</param>
public sealed record ChdHashResult(int? TrackNumber, ulong StartOffset, long Length, byte[]? Sha1, byte[]? Sha256, uint? Crc32, ulong? Xxh3)
{
    /// <summary>Formats a hex string for one of the hashes, or <c>null</c> when unavailable.</summary>
    public string? ToHex(ChdHashType type)
    {
        return type switch
        {
            ChdHashType.Sha1 => Sha1 is null ? null : Convert.ToHexString(Sha1).ToLowerInvariant(),
            ChdHashType.Sha256 => Sha256 is null ? null : Convert.ToHexString(Sha256).ToLowerInvariant(),
            ChdHashType.Crc32 => Crc32?.ToString("X8").ToLowerInvariant(),
            ChdHashType.Xxh3 => Xxh3?.ToString("X16").ToLowerInvariant(),
            _ => null
        };
    }
}

public static partial class Chd
{
    /// <summary>
    /// Computes hash digests over a CHD's decompressed content (CHDlite <c>hash_content</c>
    /// parity). For CD/GD-ROM images with <paramref name="perTrack"/> set, one hash per track is
    /// returned (track boundaries from <see cref="ChdFile.Tracks"/>); otherwise a single
    /// whole-image hash is returned. Reading and hashing happen in one sequential pass.
    /// </summary>
    /// <param name="filename">Path to the CHD file (standalone; child CHDs need
    /// <paramref name="parentFilename"/>).</param>
    /// <param name="types">The hash algorithms to compute (bitwise OR of <see cref="ChdHashType"/>).</param>
    /// <param name="parentFilename">Parent CHD path for a child CHD, or <c>null</c> for standalone.</param>
    /// <param name="perTrack">For CD/GD-ROM images, hash each track separately instead of the whole image.</param>
    /// <param name="progress">An optional <see cref="IProgress{T}"/> receiving a <see cref="ChdProgress"/>
    /// report after each decompressed hunk.</param>
    /// <param name="cancellationToken">A token to cancel the hashing. <see cref="OperationCanceledException"/>
    /// is thrown if cancellation is requested.</param>
    /// <returns>One <see cref="ChdHashResult"/> per hashed region (track or whole image), in order.
    /// Empty when <paramref name="types"/> is <see cref="ChdHashType.None"/>.</returns>
    /// <exception cref="InvalidDataException">The CHD cannot be opened or a hunk fails to decompress.</exception>
    public static IReadOnlyList<ChdHashResult> ComputeHashes(string filename, ChdHashType types,
        string? parentFilename = null, bool perTrack = false, IProgress<ChdProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);
        if (types == ChdHashType.None)
            return [];

        var err = ChdFile.Open(filename, parentFilename, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
            throw new InvalidDataException($"Cannot open CHD '{filename}' ({err.GetMessage()} ({err}))");

        using (chd)
        {
            var regions = new List<(int? Track, ulong Offset, long Length)>();
            if (perTrack && chd.Tracks is { Count: > 0 })
            {
                var unitBytes = chd.UnitBytes;
                foreach (var track in chd.Tracks)
                {
                    var start = track.StartFrame * unitBytes;
                    var length = (track.Frames + track.ExtraFrames) * unitBytes;
                    regions.Add((track.TrackNumber, start, length));
                }
            }
            else
            {
                regions.Add((null, 0, (long)chd.TotalBytes));
            }

            var results = new List<ChdHashResult>(regions.Count);
            foreach (var (track, offset, length) in regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(HashRegion(chd, offset, (ulong)length, types, track, progress, cancellationToken));
            }

            return results;
        }
    }

    private static ChdHashResult HashRegion(ChdFile chd, ulong offset, ulong length, ChdHashType types,
        int? trackNumber, IProgress<ChdProgress>? progress, CancellationToken cancellationToken)
    {
        using var sha1 = (types & ChdHashType.Sha1) != ChdHashType.None ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1) : null;
        using var sha256 = (types & ChdHashType.Sha256) != ChdHashType.None ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        var crc32 = (types & ChdHashType.Crc32) != ChdHashType.None ? new Crc32() : null;
        var xxh3 = (types & ChdHashType.Xxh3) != ChdHashType.None ? new XxHash3() : null;

        var sw = progress != null ? Stopwatch.StartNew() : null;
        var buffer = new byte[chd.HunkBytes];
        var remaining = length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = (int)Math.Min((ulong)buffer.Length, remaining);
            var err = chd.Read(offset, buffer, 0, chunk, cancellationToken);
            if (err != ChdError.Chderrnone)
                throw new InvalidDataException($"Failed to read CHD content at offset {offset}: {err.GetMessage()} ({err})");

            sha1?.AppendData(buffer, 0, chunk);
            sha256?.AppendData(buffer, 0, chunk);
            crc32?.Append(buffer.AsSpan(0, chunk));
            xxh3?.Append(buffer.AsSpan(0, chunk));

            offset += (ulong)chunk;
            remaining -= (ulong)chunk;

            progress?.Report(new ChdProgress(
                (uint)((offset - (length - remaining)) / chd.HunkBytes),
                (uint)((length + chd.HunkBytes - 1) / chd.HunkBytes),
                (long)(length - remaining),
                (long)length,
                sw!.Elapsed));
        }

        return new ChdHashResult(
            trackNumber,
            offset - length,
            (long)length,
            types.HasFlag(ChdHashType.Sha1) ? sha1!.GetHashAndReset() : null,
            types.HasFlag(ChdHashType.Sha256) ? sha256!.GetHashAndReset() : null,
            types.HasFlag(ChdHashType.Crc32) ? crc32!.GetCurrentHashAsUInt32() : null,
            types.HasFlag(ChdHashType.Xxh3) ? xxh3!.GetCurrentHashAsUInt64() : null);
    }
}