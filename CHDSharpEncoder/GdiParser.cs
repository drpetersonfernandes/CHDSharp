using System.Globalization;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoder;

/// <summary>
/// Parses a Sega GD-ROM GDI descriptor into a table of contents, matching MAME's
/// <c>cdrom_file::parse_gdi</c>. Track lines are
/// <c>&lt;track&gt; &lt;lba&gt; &lt;type&gt; &lt;sector size&gt; "&lt;file&gt;" &lt;offset&gt;</c>;
/// gaps between track LBAs become zero-filled pad frames at the end of the previous track.
/// </summary>
public class GdiParser
{
    /// <summary>
    /// Parses a GDI descriptor into a GD-ROM table of contents.
    /// </summary>
    /// <param name="gdiPath">Path to the .gdi file; referenced data files are resolved relative to it.</param>
    /// <returns>The parsed table of contents (with <see cref="CdTocFlags.GdRom"/> set).</returns>
    /// <exception cref="FileNotFoundException">The GDI file or a referenced data file does not exist.</exception>
    /// <exception cref="InvalidDataException">The GDI file is malformed or uses unsupported track types.</exception>
    public CdToc Parse(string gdiPath)
    {
        ArgumentNullException.ThrowIfNull(gdiPath);
        if (!File.Exists(gdiPath))
            throw new FileNotFoundException($"GDI file not found: {gdiPath}", gdiPath);

        var lines = File.ReadAllLines(gdiPath);

        // first line: track count
        var headerTokens = CdImageParser.Tokenize(lines.Length > 0 ? lines[0] : string.Empty);
        if (headerTokens.Count == 0 || !int.TryParse(headerTokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out var numTracks) || numTracks <= 0)
            throw new InvalidDataException("GDI header specifies no tracks");

        var toc = new CdToc
        {
            Flags = CdTocFlags.GdRom
        };
        var tracks = new CdTrack?[numTracks];
        var trackCount = 0;

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var tokens = CdImageParser.Tokenize(lines[lineIndex]);
            if (tokens.Count == 0)
                continue;

            if (!int.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out var trackNumber) ||
                trackNumber < 1 || trackNumber > numTracks)
                throw new InvalidDataException($"Track {tokens[0]} is out of expected range of 1 to {numTracks}");

            if (tokens.Count != 6)
                throw new InvalidDataException($"GDI track entry should have 6 parameters, found {tokens.Count}");

            var trknum = trackNumber - 1;
            if (tracks[trknum] != null)
            {
                throw new InvalidDataException($"Track {trackNumber} defined multiple times");
            }

            var physframeofs = int.Parse(tokens[1], CultureInfo.InvariantCulture);
            var trktype = int.Parse(tokens[2], CultureInfo.InvariantCulture);
            var trksize = int.Parse(tokens[3], CultureInfo.InvariantCulture);
            var fileName = CdImageParser.ResolveFileName(gdiPath, tokens[4]);
            // tokens[5] = offset parameter, unused (matching MAME)

            var track = new CdTrack
            {
                Number = trackNumber,
                PhysicalFrameOffset = physframeofs,
                FileName = fileName,
                FileOffset = 0,
                SubType = CdSubType.None,
                SubSize = 0,
                PgSub = CdSubType.None
            };

            switch (trktype)
            {
                case 4 when trksize == 2352:
                    track.TrackType = CdTrackType.Mode1Raw;
                    track.DataSize = 2352;
                    break;
                case 4 when trksize == 2048:
                    track.TrackType = CdTrackType.Mode1;
                    track.DataSize = 2048;
                    break;
                case 0:
                    track.TrackType = CdTrackType.Audio;
                    track.DataSize = 2352;
                    track.Swap = true;
                    break;
                default:
                    throw new InvalidDataException($"Unknown track type {trktype} and track size {trksize} combination encountered");
            }

            if (!File.Exists(fileName))
                throw new FileNotFoundException($"Couldn't find data file [{fileName}]", fileName);

            track.Frames = (int)(new FileInfo(fileName).Length / trksize);
            track.PadFrames = 0;

            // the gap between this track's LBA and the end of the previous track becomes
            // zero-filled pad frames appended to the previous track (MAME's parse_gdi)
            if (trknum != 0 && tracks[trknum - 1] is { } previous)
            {
                var dif = physframeofs - (previous.Frames + previous.PhysicalFrameOffset);
                previous.Frames += dif;
                previous.PadFrames = dif;
                tracks[trknum - 1] = previous;
            }

            tracks[trknum] = track;
            trackCount++;
        }

        if (trackCount != numTracks)
            throw new InvalidDataException("GDI is missing tracks");

        foreach (var track in tracks)
        {
            if (track is null)
                throw new InvalidDataException("GDI is missing tracks");

            toc.Tracks.Add(track.Value);
        }

        return toc;
    }
}