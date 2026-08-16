namespace CHDSharp.Models;

/// <summary>
/// Snapshot of a CHD file header, parsed without opening the file for hunk reads
/// (libchdr <c>chd_read_header</c> parity). Returned by
/// <see cref="CHDSharp.Chd.ReadHeader(string, out ChdHeaderInfo?)"/> and its overloads.
/// </summary>
public sealed record ChdHeaderInfo
{
    /// <summary>Length of the on-disk header in bytes (76 / 80 / 120 / 108 / 124 for V1-V5).</summary>
    public uint Length { get; init; }

    /// <summary>CHD format version (1-5).</summary>
    public uint Version { get; init; }

    /// <summary>
    /// Raw CHD global flags field (V1-V4): bit 0 (<c>0x01</c>) = has parent, bit 1 (<c>0x02</c>) = writable.
    /// V5 has no flags field on disk, so this is always 0 for V5.
    /// </summary>
    public uint Flags { get; init; }

    /// <summary>
    /// The compression codec slots used by this CHD (up to 4 for V5; V1-V4 use slot 0 only).
    /// An uncompressed V5 CHD has all slots <see cref="ChdCodec.None"/>.
    /// </summary>
    public ChdCodec[] Compression { get; init; } = [];

    /// <summary>Size of each hunk (block) in bytes.</summary>
    public uint HunkBytes { get; init; }

    /// <summary>Total number of hunks (blocks) in the image.</summary>
    public uint TotalHunks { get; init; }

    /// <summary>Total decompressed size of the image in bytes.</summary>
    public ulong TotalBytes { get; init; }

    /// <summary>File offset of the first metadata entry, or 0 if the CHD has no metadata. Always 0 for V1/V2.</summary>
    public ulong MetaOffset { get; init; }

    /// <summary>File offset of the block map. Only populated for V5; 0 for V1-V4.</summary>
    public ulong MapOffset { get; init; }

    /// <summary>MD5 hash of the raw data (V1-V3); <c>null</c> for V4/V5, which dropped MD5.</summary>
    public byte[]? Md5 { get; init; }

    /// <summary>MD5 hash of the parent file (V1-V3); <c>null</c> for V4/V5.</summary>
    public byte[]? ParentMd5 { get; init; }

    /// <summary>SHA1 hash of the full image including metadata (V4/V5), or the raw SHA1 for V3; <c>null</c> for V1/V2.</summary>
    public byte[]? Sha1 { get; init; }

    /// <summary>SHA1 hash of only the raw (decompressed) image data (V3-V5); <c>null</c> for V1/V2.</summary>
    public byte[]? RawSha1 { get; init; }

    /// <summary>SHA1 hash of the parent file (V3-V5); <c>null</c> for V1/V2.</summary>
    public byte[]? ParentSha1 { get; init; }

    /// <summary>
    /// Size of a unit in bytes, used for parent block address translation. For V5 this is read
    /// from the header; for V1-V4 it is derived from metadata (GDDD <c>BPS</c>, CD frame size 2448,
    /// or <see cref="HunkBytes"/>), matching <see cref="ChdFile.UnitBytes"/> and libchdr's
    /// <c>header_guess_unitbytes</c>.
    /// </summary>
    public uint UnitBytes { get; init; }

    /// <summary>Total number of units in the image (<c>ceil(TotalBytes / UnitBytes)</c>); 0 if <see cref="UnitBytes"/> is 0.</summary>
    public ulong UnitCount { get; init; }

    /// <summary>
    /// <c>true</c> if this CHD is a differential child that requires a parent CHD to read
    /// (derived from the parent MD5/SHA1 hashes).
    /// </summary>
    public bool HasParent => !IsAllZero(ParentMd5) || !IsAllZero(ParentSha1);

    /// <summary>Obsolete hard-disk geometry: cylinders. Only populated for V1/V2 headers.</summary>
    public uint ObsoleteCylinders { get; init; }

    /// <summary>Obsolete hard-disk geometry: heads. Only populated for V1/V2 headers.</summary>
    public uint ObsoleteHeads { get; init; }

    /// <summary>Obsolete hard-disk geometry: sectors per track. Only populated for V1/V2 headers.</summary>
    public uint ObsoleteSectors { get; init; }

    /// <summary>Obsolete hunk size in sectors. Only populated for V1/V2 headers.</summary>
    public uint ObsoleteHunksize { get; init; }

    private static bool IsAllZero(byte[]? bytes)
    {
        if (bytes == null)
            return true;

        foreach (var b in bytes)
        {
            if (b != 0)
                return false;
        }

        return true;
    }

    /// <summary>Returns a string representation of the header including version, size, and hunk count.</summary>
    public override string ToString()
    {
        return $"V{Version}: {TotalBytes:N0} bytes, {TotalHunks:N0} hunks x {HunkBytes:N0}";
    }
}