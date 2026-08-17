using System.Text;
using GhostFormatter.Abstractions.Enums;

namespace GhostFormatter.Core.Services;

/// <summary>
/// Normalizes whitespace including indentation, trailing spaces, and blank lines.
/// </summary>
public sealed class WhitespaceNormalizer
{
    /// <summary>
    /// Converts between tabs and spaces in leading whitespace.
    /// </summary>
    public string ConvertIndentation(string text, bool useTabs, int indentSize)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var leadingEnd = 0;

            while (leadingEnd < line.Length && (line[leadingEnd] == ' ' || line[leadingEnd] == '\t'))
            {
                leadingEnd++;
            }

            if (leadingEnd == 0)
            {
                sb.Append(line);
            }
            else
            {
                var leading = line[..leadingEnd];
                var rest = line[leadingEnd..];

                // Calculate column position
                var column = 0;
                foreach (var c in leading)
                {
                    if (c == '\t')
                    {
                        column += indentSize - (column % indentSize);
                    }
                    else
                    {
                        column++;
                    }
                }

                if (useTabs)
                {
                    var tabs = column / indentSize;
                    var spaces = column % indentSize;
                    sb.Append('\t', tabs);
                    sb.Append(' ', spaces);
                }
                else
                {
                    sb.Append(' ', column);
                }

                sb.Append(rest);
            }

            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Trims trailing whitespace from each line.
    /// </summary>
    public string TrimTrailingWhitespace(string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd(' ', '\t');
        }
        return string.Join('\n', lines);
    }
}
