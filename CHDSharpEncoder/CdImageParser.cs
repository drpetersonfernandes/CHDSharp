using System.Text;

namespace CHDSharpEncoder;

/// <summary>
/// Dispatches CD image descriptor parsing by file extension, mirroring MAME's
/// <c>cdrom_file::parse_toc</c>: .cue, .gdi, .iso/.cdr/.toast, .toc, and a cdrdao-style
/// fallback for unknown extensions.
/// </summary>
public static class CdImageParser
{
    /// <summary>
    /// Parses a CD image descriptor (CUE, GDI, ISO or cdrdao TOC) into a table of contents.
    /// </summary>
    /// <param name="descriptorPath">Path to the descriptor file.</param>
    /// <returns>The parsed table of contents.</returns>
    /// <exception cref="FileNotFoundException">The descriptor or a referenced data file does not exist.</exception>
    /// <exception cref="InvalidDataException">The descriptor is malformed or unsupported.</exception>
    public static CdToc Parse(string descriptorPath)
    {
        ArgumentNullException.ThrowIfNull(descriptorPath);

        string extension = Path.GetExtension(descriptorPath).ToLowerInvariant();
        switch (extension)
        {
            case ".gdi":
                return new GdiParser().Parse(descriptorPath);
            case ".cue":
                return CueParser.Parse(descriptorPath);
            case ".iso":
            case ".cdr":
            case ".toast":
                return new IsoParser().Parse(descriptorPath);
            default:
                // MAME treats unknown extensions as cdrdao-style TOC files
                return new TocParser().Parse(descriptorPath);
        }
    }

    /// <summary>
    /// Splits a descriptor line into tokens, honoring single and double quotes
    /// (matching MAME's <c>tokenize</c> helper).
    /// </summary>
    internal static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        bool singleQuote = false;
        bool doubleQuote = false;
        var sb = new StringBuilder();

        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (!singleQuote && c == '"')
            {
                doubleQuote = !doubleQuote;
            }
            else if (!doubleQuote && c == '\'')
            {
                singleQuote = !singleQuote;
            }
            else if (!singleQuote && !doubleQuote && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }

                while (i + 1 < line.Length && char.IsWhiteSpace(line[i + 1]))
                {
                    i++;
                }
            }
            else
            {
                sb.Append(c);
            }

            i++;
        }

        if (sb.Length > 0)
            tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>
    /// Resolves a descriptor-relative file name against the descriptor's directory
    /// (matching MAME's <c>get_file_path</c> + append).
    /// </summary>
    internal static string ResolveFileName(string descriptorPath, string fileName)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(descriptorPath)) ?? string.Empty;
        return Path.Combine(baseDir, fileName);
    }
}