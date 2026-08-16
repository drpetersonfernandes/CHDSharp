namespace CHDSharpEncoder;

/// <summary>
/// Parses a raw ISO image into a single-track table of contents, matching MAME's
/// <c>cdrom_file::parse_iso</c>: MODE1/2048 when the size is divisible by 2048,
/// MODE2/2336, or MODE2_RAW/2352.
/// </summary>
public class IsoParser
{
    /// <summary>
    /// Parses an ISO image into a single-track table of contents.
    /// </summary>
    /// <param name="isoPath">Path to the .iso/.cdr/.toast file.</param>
    /// <returns>The parsed table of contents.</returns>
    /// <exception cref="FileNotFoundException">The ISO file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file size matches no known sector size.</exception>
    public CdToc Parse(string isoPath)
    {
        ArgumentNullException.ThrowIfNull(isoPath);
        if (!File.Exists(isoPath))
            throw new FileNotFoundException($"ISO file not found: {isoPath}", isoPath);

        long size = new FileInfo(isoPath).Length;

        var track = new CdTrack
        {
            Number = 1,
            FileName = Path.GetFullPath(isoPath),
            FileOffset = 0,
            Index00 = 0,
            Index01 = 0,
            SubType = CdSubType.None,
            SubSize = 0,
            Swap = false,
        };

        if (size % 2048 == 0)
        {
            track.TrackType = CdTrackType.Mode1;
            track.DataSize = 2048;
            track.Frames = (int)(size / 2048);
        }
        else if (size % 2336 == 0)
        {
            // 2336-byte mode 2
            track.TrackType = CdTrackType.Mode2;
            track.DataSize = 2336;
            track.Frames = (int)(size / 2336);
        }
        else if (size % 2352 == 0)
        {
            // 2352-byte mode 2 raw
            track.TrackType = CdTrackType.Mode2Raw;
            track.DataSize = 2352;
            track.Frames = (int)(size / 2352);
        }
        else
        {
            throw new InvalidDataException($"Unrecognized ISO sector size ({size} bytes is not a multiple of 2048, 2336 or 2352)");
        }

        var toc = new CdToc();
        toc.Tracks.Add(track);
        return toc;
    }
}