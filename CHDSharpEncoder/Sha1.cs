using System.Security.Cryptography;

namespace CHDSharpEncoder;

/// <summary>Computes SHA-1 (160-bit) hash digests, backed by the platform's native
/// <see cref="System.Security.Cryptography.SHA1"/> implementation (the digest is
/// identical to any conforming SHA-1 implementation).</summary>
public class Sha1 : IDisposable
{
    private IncrementalHash? _hash;

    /// <summary>Resets the hasher to its initial state for reuse.</summary>
    public void Reset()
    {
        _hash?.Dispose();
        _hash = null;
    }

    /// <summary>Appends data to the hash computation.</summary>
    /// <param name="data">The source byte array.</param>
    /// <param name="offset">The starting offset within <paramref name="data"/>.</param>
    /// <param name="length">The number of bytes to process.</param>
    public void Append(byte[] data, int offset, int length)
    {
        _hash ??= IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        _hash.AppendData(data, offset, length);
    }

    /// <summary>Finalizes the hash and returns the 20-byte SHA-1 digest. The hasher is reset
    /// and can be reused (like the classic <c>final</c>/<c>finish</c> semantics).</summary>
    /// <returns>A 20-byte array containing the SHA-1 hash.</returns>
    public byte[] Finish()
    {
        if (_hash == null)
            return SHA1.HashData(Array.Empty<byte>());

        var digest = _hash.GetHashAndReset();
        _hash.Dispose();
        _hash = null;
        return digest;
    }

    /// <summary>Computes the SHA-1 hash of the given data in one call.</summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>A 20-byte array containing the SHA-1 hash.</returns>
    public static byte[] Compute(byte[] data)
    {
        return SHA1.HashData(data);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Reset();
    }
}