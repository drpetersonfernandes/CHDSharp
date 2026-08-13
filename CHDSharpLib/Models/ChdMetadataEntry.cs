using System.Text;

namespace CHDSharp.Models;

/// <summary>Represents a single metadata entry from a CHD file header (e.g. game name, disc label, hardware info).</summary>
/// <param name="Tag">Four-character tag identifying the metadata type (e.g. "GAME", "DISC", "HARD").</param>
/// <param name="Data">The raw metadata payload bytes. May be ASCII text or binary data.</param>
public record ChdMetadataEntry(string Tag, byte[] Data)
{
    private const int MaxTextDataLength = 1024 * 1024;

    /// <summary>
    /// Metadata flags from the entry header (the top byte of the stored length field).
    /// Bit 0 (<c>CHD_MDFLAGS_CHECKSUM</c>) indicates the entry is covered by the
    /// combined-SHA1 verification in <c>Chd.CheckFile</c>.
    /// </summary>
    public byte Flags { get; init; }

    /// <summary>Value equality is based on <see cref="Tag"/> and <see cref="Data"/> only; <see cref="Flags"/> is metadata and excluded.</summary>
    public virtual bool Equals(ChdMetadataEntry? other)
    {
        return other is not null && string.Equals(Tag, other.Tag, StringComparison.Ordinal) && ReferenceEquals(Data, other.Data);
    }

    /// <inheritdoc cref="Equals(ChdMetadataEntry?)"/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Tag);
    }

    /// <summary>Returns the ASCII text representation of the metadata data, if applicable.</summary>
    public string GetText()
    {
        if (Data.Length > MaxTextDataLength)
            return string.Empty;

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
