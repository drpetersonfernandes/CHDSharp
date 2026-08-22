namespace CHDSharpEncoder;

/// <summary>
/// Predefined hard disk geometry templates, matching MAME's <c>s_hd_templates</c> table
/// (<c>chdman.cpp</c>). Used by <c>chdman createhd -tp &lt;id&gt;</c> to write exact CHS geometry
/// into the CHD's 'GDDD' metadata instead of guessing from the file size.
/// </summary>
public static class HardDiskTemplates
{
    /// <summary>A single hard disk geometry template.</summary>
    /// <param name="Manufacturer">Drive manufacturer name.</param>
    /// <param name="Model">Drive model string.</param>
    /// <param name="Cylinders">Number of cylinders.</param>
    /// <param name="Heads">Number of heads.</param>
    /// <param name="Sectors">Sectors per track.</param>
    /// <param name="SectorSize">Bytes per sector.</param>
    public record HardDiskTemplate(string Manufacturer, string Model,
        uint Cylinders, uint Heads, uint Sectors, uint SectorSize)
    {
        /// <summary>Total image size in bytes (Cylinders * Heads * Sectors * SectorSize).</summary>
        public ulong TotalBytes => (ulong)Cylinders * Heads * Sectors * SectorSize;

        /// <summary>Total image size in megabytes (for display).</summary>
        public long TotalMB => (long)(TotalBytes / (1024 * 1024));
    }

    /// <summary>
    /// The built-in hard disk geometry templates (13 entries), matching MAME's
    /// <c>s_hd_templates</c> array in <c>chdman.cpp</c>.
    /// </summary>
    public static readonly HardDiskTemplate[] Templates =
    [
        new("Conner",     "CFA170A",    332, 16, 63, 512),
        new("Rodime",     "R0201",      321,  2, 16, 512),
        new("Rodime",     "R0202",      321,  4, 16, 512),
        new("Rodime",     "R0203",      321,  6, 16, 512),
        new("Rodime",     "R0204",      321,  8, 16, 512),
        new("Seagate",    "ST-213",     615,  2, 17, 512),
        new("Seagate",    "ST-225",     615,  4, 17, 512),
        new("Seagate",    "ST-251",     820,  6, 17, 512),
        new("Seagate",    "ST-3600N",  1877,  7, 76, 512),
        new("Maxtor",     "LXT-213S",  1314,  7, 53, 512),
        new("Maxtor",     "LXT-340S",  1574,  7, 70, 512),
        new("Maxtor",     "MXT-540SL", 2466,  7, 87, 512),
        new("Micropolis", "1528",      2094, 15, 83, 512),
    ];

    /// <summary>
    /// Looks up a template by its zero-based index.
    /// </summary>
    /// <param name="id">Template ID (0-based).</param>
    /// <returns>The template at the given index.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is out of range.</exception>
    public static HardDiskTemplate GetTemplate(int id)
    {
        if (id < 0 || id >= Templates.Length)
            throw new ArgumentOutOfRangeException(nameof(id), id,
                $"Template ID must be between 0 and {Templates.Length - 1}");
        return Templates[id];
    }
}
