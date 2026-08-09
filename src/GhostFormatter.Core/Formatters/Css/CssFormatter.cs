using System.Text;
using System.Text.RegularExpressions;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.Css;

/// <summary>
/// Formats CSS files with proper indentation, spacing, and structure.
/// Supports modern CSS features including nesting, custom properties, and media queries.
/// </summary>
public sealed partial class CssFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.Css;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".css"];

    /// <summary>Initializes a new instance of the <see cref="CssFormatter"/> class.</summary>
    public CssFormatter(ILogger<CssFormatter> logger)
        : base(logger) { }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sb = new StringBuilder();
        var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);
        var indentLevel = 0;
        var inComment = false;

        var lines = text.Split('\n');

        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line))
            {
                sb.Append('\n');
                continue;
            }

            // Handle block comments
            if (inComment)
            {
                sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
                sb.Append(line.StartsWith('*') ? " " + line : line);
                sb.Append('\n');

                if (line.Contains("*/", StringComparison.Ordinal))
                {
                    inComment = false;
                }
                continue;
            }

            if (
                line.StartsWith("/*", StringComparison.Ordinal)
                && !line.Contains("*/", StringComparison.Ordinal)
            )
            {
                inComment = true;
                sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
                sb.Append(line);
                sb.Append('\n');
                continue;
            }

            // Closing brace: decrease indent first
            if (line.StartsWith('}'))
            {
                indentLevel = Math.Max(0, indentLevel - 1);
                sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
                sb.Append('}');

                // Handle text after closing brace (e.g., "} else {")
                var afterBrace = line[1..].Trim();
                if (afterBrace.Length > 0)
                {
                    sb.Append(' ');
                    sb.Append(afterBrace);
                }

                sb.Append('\n');

                if (line.EndsWith('{'))
                {
                    indentLevel++;
                }
                continue;
            }

            // Normal line
            sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));

            // Format declarations: ensure space after colon
            if (
                line.Contains(':')
                && !line.StartsWith("//", StringComparison.Ordinal)
                && !line.StartsWith("/*", StringComparison.Ordinal)
                && !line.Contains("://", StringComparison.Ordinal)
            )
            {
                line = FormatDeclaration(line);
            }

            sb.Append(line);
            sb.Append('\n');

            // Opening brace: increase indent after
            if (line.EndsWith('{'))
            {
                indentLevel++;
            }
        }

        // Remove extra trailing newlines
        var result = sb.ToString().TrimEnd('\n');
        return Task.FromResult(result);
    }

    private static string FormatDeclaration(string line)
    {
        var colonIdx = line.IndexOf(':');
        if (colonIdx < 0 || colonIdx >= line.Length - 1)
        {
            return line;
        }

        // Skip pseudo-classes and pseudo-elements (:hover, ::before, :has(...), etc.)
        // A colon that's part of a selector has no preceding property-name-style content —
        // the character immediately before the colon is either another colon (::) or the
        // end of a selector identifier, not followed by a semicolon on the same line in a
        // way that looks like "property: value;".
        var beforeColon = line[..colonIdx].TrimEnd();
        if (beforeColon.Length > 0)
        {
            var lastChar = beforeColon[^1];
            // If the character right before the colon is a letter/digit/hyphen/underscore/
            // close-paren/close-bracket, AND the line ends with '{' (i.e. it's a selector
            // line, not a declaration line), treat it as a pseudo-selector and leave it alone.
            if (line.TrimEnd().EndsWith('{'))
            {
                return line;
            }
        }

        // Skip URLs
        if (
            beforeColon.EndsWith("http", StringComparison.OrdinalIgnoreCase)
            || beforeColon.EndsWith("https", StringComparison.OrdinalIgnoreCase)
        )
        {
            return line;
        }

        // Only format lines that look like declarations (end with ;)
        if (!line.TrimEnd().EndsWith(';'))
        {
            return line;
        }

        // Format: property: value;
        if (line[colonIdx + 1] != ' ')
        {
            return line[..(colonIdx + 1)] + " " + line[(colonIdx + 1)..].TrimStart();
        }

        return line;
    }
}
