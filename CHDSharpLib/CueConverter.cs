using System.Security.Cryptography;
using System.Text;

namespace CHDSharp;

/// <summary>CUE sheet output styles (CHDlite <c>CueStyle</c> parity).</summary>
public enum CueStyle
{
    /// <summary>chdman-style: single-track discs get a " (Track 1)" file suffix, no CATALOG line.</summary>
    Chdman = 0,

    /// <summary>Redump-style: no CATALOG line, " (Track 1)" suffixes removed.</summary>
    Redump = 1,

    /// <summary>Redump style with a CATALOG line prepended.</summary>
    RedumpCatalog = 2
}

/// <summary>The result of matching a CUE sheet against a known hash (CHDlite <c>match_cue</c> parity).</summary>
/// <param name="Style">The style whose normalized output hashes to <paramref name="Hash"/>.</param>
/// <param name="CueData">The normalized CUE sheet in <see cref="Style"/> form.</param>
/// <param name="Hash">The hash of <see cref="CueData"/> (hex, lowercase).</param>
public sealed record CueMatchResult(CueStyle? Style, string? CueData, string? Hash);

/// <summary>
/// Converts CUE sheets between the three common styles — chdman, Redump, and Redump+CATALOG —
/// and matches generated CUE text against a database hash (CHDlite <c>convert_cue_style</c> /
/// <c>match_cue</c> parity, <c>chd_extractor.cpp:497-670</c>). Style differences are limited to
/// the single-track file name suffix (" (Track 1)" in chdman output) and the CATALOG line.
/// </summary>
public static class CueConverter
{
    /// <summary>
    /// Converts a CUE sheet to the requested style. Line endings are normalized to CRLF and
    /// trailing empty lines are stripped. A leading CATALOG line is removed for the
    /// non-CATALOG styles; the Redump+CATALOG style prepends <c>CATALOG 0000000000000</c>.
    /// Single-track discs get/keep the " (Track 1)" suffix in the chdman style and lose it in
    /// the Redump styles.
    /// </summary>
    /// <param name="cueText">The CUE sheet text.</param>
    /// <param name="style">The target style.</param>
    /// <returns>The converted CUE sheet (CRLF line endings).</returns>
    public static string ConvertCue(string cueText, CueStyle style)
    {
        ArgumentNullException.ThrowIfNull(cueText);

        var lines = NormalizeLines(cueText);
        var start = 0;
        if (lines.Count > 0 && lines[0].StartsWith("CATALOG", StringComparison.Ordinal))
        {
            start = 1;
        }

        var fileCount = 0;
        for (var i = start; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("FILE ", StringComparison.Ordinal))
            {
                fileCount++;
            }
        }

        var singleTrack = fileCount == 1;
        var sb = new StringBuilder(cueText.Length + 32);
        if (style == CueStyle.RedumpCatalog)
            sb.Append("CATALOG 0000000000000\r\n");

        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];

            // Adjust the single-track FILE line's name.
            if (singleTrack && line.StartsWith("FILE ", StringComparison.Ordinal))
            {
                var q1 = line.IndexOf('"');
                var q2 = line.LastIndexOf('"');
                if (q1 >= 0 && q1 != q2)
                {
                    var fname = line.Substring(q1 + 1, q2 - q1 - 1);
                    var prefix = line.Substring(0, q1 + 1);
                    var suffix = line.Substring(q2);

                    if (style == CueStyle.Chdman)
                    {
                        if (!fname.Contains(" (Track ", StringComparison.Ordinal))
                        {
                            var dot = fname.LastIndexOf('.');
                            fname = dot >= 0
                                ? fname[..dot] + " (Track 1)" + fname[dot..]
                                : fname + " (Track 1)";
                        }
                    }
                    else
                    {
                        foreach (var pattern in new[] { " (Track 1)", " (Track 01)" })
                        {
                            var pos = fname.IndexOf(pattern, StringComparison.Ordinal);
                            if (pos >= 0)
                            {
                                fname = fname.Remove(pos, pattern.Length);
                                break;
                            }
                        }
                    }

                    line = prefix + fname + suffix;
                }
            }

            sb.Append(line).Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Tries each CUE style and returns the first whose normalized output hashes to
    /// <paramref name="dbHash"/> (case-insensitive hex compare). Used to detect whether an
    /// existing CUE was generated in chdman, Redump, or Redump+CATALOG form.
    /// </summary>
    /// <param name="cueText">The CUE sheet text.</param>
    /// <param name="dbHash">The reference hash (hex string, any case).</param>
    /// <returns>A <see cref="CueMatchResult"/> with the matching style and normalized CUE, or
    /// <c>Style = null</c> when no style matches.</returns>
    public static CueMatchResult MatchCue(string cueText, string dbHash)
    {
        ArgumentNullException.ThrowIfNull(dbHash);
        foreach (var style in new[] { CueStyle.Chdman, CueStyle.Redump, CueStyle.RedumpCatalog })
        {
            var converted = ConvertCue(cueText, style);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(converted))).ToLowerInvariant();
            if (string.Equals(hash, dbHash, StringComparison.OrdinalIgnoreCase))
                return new CueMatchResult(style, converted, hash);
        }

        return new CueMatchResult(null, null, null);
    }

    private static List<string> NormalizeLines(string text)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\r':
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    lines.Add(sb.ToString());
                    sb.Clear();
                    break;
                }
                case '\n':
                    lines.Add(sb.ToString());
                    sb.Clear();
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        if (sb.Length > 0)
            lines.Add(sb.ToString());

        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}