using System.Text;

namespace CHDSharp.Models;

/// <summary>Represents a single metadata entry from a CHD file header (e.g. game name, disc label, hardware info).</summary>
/// <param name="Tag">Four-character tag identifying the metadata type (e.g. "GAME", "DISC", "HARD").</param>
/// <param name="Data">The raw metadata payload bytes. May be ASCII text or binary data.</param>
public sealed record ChdMetadataEntry(string Tag, byte[] Data)
{
    /// <summary>Returns the ASCII text representation of the metadata data, if applicable.</summary>
    public string GetText()
    {
        return Encoding.ASCII.GetString(Data);
    }

    /// <summary><c>true</c> if <see cref="Data"/> appears to be printable ASCII text.</summary>
    public bool IsText => Data.All(b => b is 0 or >= 32);

    /// <summary>Returns a human-readable representation: tag plus text or byte count.</summary>
    public override string ToString()
    {
        return IsText ? $"{Tag}: {GetText()}" : $"{Tag}: {Data.Length} bytes";
    }
}
