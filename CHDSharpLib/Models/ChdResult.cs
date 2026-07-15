namespace CHDSharp.Models;

/// <summary>Result of a CHD verification or validation operation, consolidating error code, version, and hashes.</summary>
public sealed record ChdResult
{
    /// <summary>The error code indicating success or failure of the operation.</summary>
    public ChdError Error { get; }

    /// <summary>The CHD format version (1-5), or <c>null</c> if the file was invalid.</summary>
    public uint? Version { get; }

    /// <summary>The SHA1 hash from the header, or <c>null</c> if not available.</summary>
    public byte[]? Sha1 { get; }

    /// <summary>The MD5 hash from the header, or <c>null</c> if not available.</summary>
    public byte[]? Md5 { get; }

    /// <summary><c>true</c> if the operation completed successfully (<see cref="Error"/> is <see cref="ChdError.Chderrnone"/>).</summary>
    public bool IsSuccess => Error == ChdError.Chderrnone;

    /// <summary>Creates a new <see cref="ChdResult"/> with the given error, version, SHA1, and MD5 values.</summary>
    public ChdResult(ChdError error, uint? version, byte[]? sha1, byte[]? md5)
    {
        Error = error;
        Version = version;
        Sha1 = sha1;
        Md5 = md5;
    }

    /// <summary>SHA1 hash formatted as a lowercase hex string, or "(none)" if not available.</summary>
    public string Sha1Hex => Sha1 is not null ? Convert.ToHexString(Sha1).ToLowerInvariant() : "(none)";

    /// <summary>MD5 hash formatted as a lowercase hex string, or "(none)" if not available.</summary>
    public string Md5Hex => Md5 is not null ? Convert.ToHexString(Md5).ToLowerInvariant() : "(none)";

    /// <summary>Deconstructs the result into error, version, SHA1, and MD5 components for pattern matching.</summary>
    public void Deconstruct(out ChdError error, out uint? version, out byte[]? sha1, out byte[]? md5)
    {
        error = Error;
        version = Version;
        sha1 = Sha1;
        md5 = Md5;
    }
}
