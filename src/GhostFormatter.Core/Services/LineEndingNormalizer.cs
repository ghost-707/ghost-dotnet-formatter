using GhostFormatter.Abstractions.Enums;

namespace GhostFormatter.Core.Services;

/// <summary>
/// Normalizes line endings in text content.
/// </summary>
public sealed class LineEndingNormalizer
{
    /// <summary>
    /// Normalizes line endings to the specified style.
    /// </summary>
    public string Normalize(string text, LineEndingStyle style)
    {
        // First normalize to LF
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");

        return style switch
        {
            LineEndingStyle.Lf => normalized,
            LineEndingStyle.CrLf => normalized.Replace("\n", "\r\n"),
            LineEndingStyle.Preserve => text,
            _ => normalized
        };
    }

    /// <summary>
    /// Detects the dominant line ending style in the text.
    /// </summary>
    public LineEndingStyle Detect(string text)
    {
        var crlfCount = 0;
        var lfCount = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                crlfCount++;
                i++; // Skip the \n
            }
            else if (text[i] == '\n')
            {
                lfCount++;
            }
        }

        return crlfCount >= lfCount ? LineEndingStyle.CrLf : LineEndingStyle.Lf;
    }
}
