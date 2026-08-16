using System.Globalization;

namespace CHDSharpEncoder;

/// <summary>
/// Parses a cdrdao-style .toc descriptor into a table of contents, matching MAME's
/// fallback TOC parser in <c>cdrom_file::parse_toc</c>. Supports TRACK, DATAFILE/
/// AUDIOFILE/FILE (with SWAP, #decimal and MSF offsets/lengths), START, and the
/// COPY/PRE_EMPHASIS/CHANNEL control lines.
/// </summary>
public class TocParser
{
    /// <summary>
    /// Parses a .toc descriptor into a table of contents.
    /// </summary>
    /// <param name="tocPath">Path to the .toc file; referenced data files are resolved relative to it.</param>
    /// <returns>The parsed table of contents.</returns>
    /// <exception cref="FileNotFoundException">The TOC file does not exist.</exception>
    /// <exception cref="InvalidDataException">The TOC file is malformed or uses an unknown track type.</exception>
    public CdToc Parse(string tocPath)
    {
        ArgumentNullException.ThrowIfNull(tocPath);
        if (!File.Exists(tocPath))
            throw new FileNotFoundException($"TOC file not found: {tocPath}", tocPath);

        var toc = new CdToc();
        var tracks = toc.Tracks;
        int trackIndex = -1;

        foreach (string rawLine in File.ReadAllLines(tocPath))
        {
            var tokens = CdImageParser.Tokenize(rawLine);
            if (tokens.Count == 0)
                continue;

            switch (tokens[0])
            {
                case "NO":
                    // NO COPY / NO PRE_EMPHASIS: control flags, not represented in the model
                    break;
                case "COPY":
                case "PRE_EMPHASIS":
                case "TWO_CHANNEL_AUDIO":
                case "FOUR_CHANNEL_AUDIO":
                    break;
                case "DATAFILE":
                case "AUDIOFILE":
                case "FILE":
                {
                    if (trackIndex < 0)
                        throw new InvalidDataException($"FILE command without a preceding TRACK: {rawLine}");
                    if (tokens.Count < 2)
                        throw new InvalidDataException($"Malformed FILE command: {rawLine}");

                    var track = tracks[trackIndex];
                    track.FileName = CdImageParser.ResolveFileName(tocPath, tokens[1]);

                    int tokenIndex = 2;
                    if (tokenIndex < tokens.Count && tokens[tokenIndex] == "SWAP")
                    {
                        track.Swap = true;
                        tokenIndex++;
                    }
                    else
                    {
                        track.Swap = false;
                    }

                    long fileOffset = 0;
                    if (tokenIndex < tokens.Count)
                    {
                        string offsetToken = tokens[tokenIndex++];
                        if (offsetToken.StartsWith('#'))
                        {
                            // decimal byte offset
                            fileOffset = long.Parse(offsetToken.AsSpan(1), CultureInfo.InvariantCulture);
                        }
                        else if (char.IsDigit(offsetToken[0]))
                        {
                            // MSF offset in bytes
                            fileOffset = (long)CueParser.ParseMsfToFrames(offsetToken) * (track.DataSize + track.SubSize);
                        }
                    }

                    track.FileOffset = fileOffset;

                    // next token: track length in frames, or an additional offset
                    int frames = 0;
                    if (tokenIndex < tokens.Count && char.IsDigit(tokens[tokenIndex][0]))
                    {
                        frames = CueParser.ParseMsfToFrames(tokens[tokenIndex++]);
                        if (tokenIndex < tokens.Count && char.IsDigit(tokens[tokenIndex][0]))
                        {
                            // the previous token was an offset, this one is the length
                            fileOffset += (long)frames * (track.DataSize + track.SubSize);
                            track.FileOffset = fileOffset;
                            frames = CueParser.ParseMsfToFrames(tokens[tokenIndex]);
                        }
                    }
                    else if (trackIndex == 0 && track.FileOffset != 0)
                    {
                        // the 1st track might have a length with no offset
                        frames = (int)(track.FileOffset / (track.DataSize + track.SubSize));
                        track.FileOffset = 0;
                    }

                    track.Frames = frames;
                    tracks[trackIndex] = track;
                    break;
                }
                case "TRACK":
                {
                    if (tokens.Count < 2)
                        throw new InvalidDataException($"Malformed TRACK command: {rawLine}");

                    var track = new CdTrack
                    {
                        Number = tracks.Count + 1,
                        TrackType = CdTrackType.Mode1,
                        DataSize = 0,
                        SubType = CdSubType.None,
                        SubSize = 0,
                        PgSub = CdSubType.None,
                        PadFrames = 0,
                        Index00 = 0,
                        Index01 = 0,
                    };

                    CueParser.ParseTrackType(tokens[1], ref track);
                    if (track.DataSize == 0)
                        throw new InvalidDataException($"Unknown track type [{tokens[1]}]");

                    if (tokens.Count >= 3)
                        CueParser.ParseSubType(tokens[2], ref track);

                    tracks.Add(track);
                    trackIndex++;
                    break;
                }
                case "START":
                {
                    if (trackIndex < 0)
                        throw new InvalidDataException($"START command without a preceding TRACK: {rawLine}");
                    if (tokens.Count < 2)
                        throw new InvalidDataException($"Malformed START command: {rawLine}");

                    var track = tracks[trackIndex];
                    track.Pregap = CueParser.ParseMsfToFrames(tokens[1]);
                    tracks[trackIndex] = track;
                    break;
                }
                default:
                    // CD_TEXT, SILENCE, ZERO, FIFO, CATALOG, ISRC and other lines are ignored,
                    // matching MAME's "Unimplemented" list
                    break;
            }
        }

        return toc;
    }
}